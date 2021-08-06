using HAProxy.StreamProcessingOffload.AgentFramework.Spop;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace HAProxy.StreamProcessingOffload.AgentFramework.Spoa
{
    // based on https://github.com/dotnet/aspnetcore/blob/main/src/Servers/Kestrel/Core/src/Internal/Http2/Http2Connection.cs
    public class SpoaConnection : ISpopFrameStreamLifetimeHandler
    {
        private readonly ILogger logger;

        // context
        private readonly IDuplexPipe _transport;
        private readonly MemoryPool<byte> _memoryPool;
        private readonly SpopFrameWriter _frameWriter;
        private readonly SpopFrameProducer _output;
        private readonly Pipe _input;
        private Task _inputTask;
        private readonly int _minAllocBufferSize;

        private readonly SpopPeerSettings _mySettings = SpopPeerSettings.AgentSettings;
        private readonly SpopPeerSettings _negotiatedSettings = new SpopPeerSettings();

        private readonly SpopFrameMetadata _incomingFrame = new SpopFrameMetadata();

        private readonly ConcurrentQueue<SpoaFrameStream> _completedStreams = new ConcurrentQueue<SpoaFrameStream>();
        private int _gracefulCloseInitiator;
        private bool _gracefulCloseStarted;
        private int _isClosed;

        // Each notify frames will spawn a stream to process the frame set.
        // Streams are thread pool work item to allow pipelining
        // The dictionary is accessed only from the main loop thread, no lock required
        internal readonly Dictionary<(long, long), SpoaFrameStream> _streams = new Dictionary<(long, long), SpoaFrameStream>();

        internal const int InitialStreamPoolSize = 5;
        internal const int MaxStreamPoolSize = 40; // max-waiting-frames in spoe conf must be less than this, default is 20

        public SpoaConnection(ILogger logger, IDuplexPipe transport)
        {
            this.logger = logger;
            _transport = transport;
            _memoryPool = MemoryPool<byte>.Shared;

            _frameWriter = new SpopFrameWriter(this.logger, _transport.Output);
            _output = new SpopFrameProducer(logger, _frameWriter);

            var inputOptions = new PipeOptions();
            _input = new Pipe(inputOptions);
            _minAllocBufferSize = _memoryPool.GetMinimumAllocSize();

            _inputTask = ReadInputAsync();
        }

        public PipeReader Input => _input.Reader;

        public async ValueTask ProcessConnectionAsync(ISpoaApplication application, CancellationToken cancellationToken = default)
        {
            Exception error = null;

            try
            {
                using var _ = cancellationToken.Register(() =>
                {
                    StopProcessingNextRequest(true);
                }, useSynchronizationContext: false);

                // frame processing
                while (_isClosed == 0)
                {
                    var result = await Input.ReadAsync();
                    var buffer = result.Buffer;

                    UpdateCompletedStreams();

                    try
                    {
                        while (FrameReader.TryReadFrame(ref buffer, _incomingFrame, out var framePayload))
                        {
                            logger.LogDebug("rcv frame {incomingFrame}", _incomingFrame);
                            if (cancellationToken.IsCancellationRequested) break;

                            await ProcessFrameAsync(application, framePayload);
                        }

                        if (result.IsCompleted)
                        {
                            return;
                        }
                    }
                    finally
                    {
                        Input.AdvanceTo(buffer.Start, buffer.End);

                        await UpdateConnectionState();
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex;
                logger.LogError(ex, "aborting connection");
            }
            finally
            {
                // disconnect
                try
                {
                    if (TryClose())
                    {
                        if (error != null)
                        {
                            var (status, message) = GetErrorStatus(error);
                            await _output.WriteAgentDisconnectAsync(status, message);
                        }
                        else await _output.WriteAgentDisconnectAsync();
                    }

                    foreach (var stream in _streams.Values)
                    {
                        stream.Abort();
                    }

                    while (_streams.Count > 0)
                    {
                        UpdateCompletedStreams();
                    }

                    _frameWriter.Complete();
                }
                catch
                {
                    // _frameWriter.Abort();
                    throw;
                }
                finally
                {
                    Input.Complete();
                    _transport.Input.CancelPendingRead();
                    await _inputTask;
                }
            }
        }

        private (int status, string message) GetErrorStatus(Exception ex)
        {
            switch (ex)
            {
                case IOException iOException:
                    return (Constants.StatusCode.IO, iOException.Message);
                default:
                    return (Constants.StatusCode.UnknownError, ex.Message);
            }
        }

        private void UpdateCompletedStreams()
        {
            while (_completedStreams.TryDequeue(out var stream))
            {
                RemoveStream(stream);
            }
        }

        private Task ProcessFrameAsync(ISpoaApplication application, in ReadOnlySequence<byte> payload)
        {
            return _incomingFrame.Type switch
            {
                FrameType.HaproxyHello => ProcessHandshake(payload),
                FrameType.HaproxyNotify => ProcessNotifyFrameAsync(application, payload),
                FrameType.Unset => ProcessContinuationFrameAsync(payload),
                FrameType.HaproxyDisconnect => ProcessDisconnectFrameAsync(payload),
                _ => ProcessUnknownFrameAsync(),
            };
        }

        private Task ProcessUnknownFrameAsync()
        {
            // discard (or should we disconnect with 4 | invalid frame received)
            return Task.CompletedTask;
        }

        private Task ProcessHandshake(in ReadOnlySequence<byte> payload)
        {
            var engineSettings = new Dictionary<string, object>();
            FrameReader.DecodeKeyValueListPayload(payload, engineSettings);

            if(engineSettings.TryGetValue(Constants.Handshake.ItemKeyNames.Healthcheck, out object healthcheck) && (bool)healthcheck)
            {
                logger.LogDebug("healthcheck");
                StopProcessingNextRequest(false);
                return Task.CompletedTask;
            }

            var engineSupportedVersionsItemValue = (string)engineSettings[Constants.Handshake.ItemKeyNames.SupportedVersions];
            var engineSupportedVersion = new HashSet<string>(engineSupportedVersionsItemValue.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
            if (engineSupportedVersion.Contains(_mySettings.SupportedSpopVersion))
            {
                _negotiatedSettings.SupportedSpopVersion = _mySettings.SupportedSpopVersion;
            }
            else
            {
                throw new Exception("unsupported version");
            }

            // negotiates frame size
            var engineMaxFrameSize = (uint)engineSettings[Constants.Handshake.ItemKeyNames.MaxFrameSize];
            if (engineMaxFrameSize < _mySettings.FrameSize)
            {
                _negotiatedSettings.FrameSize = engineMaxFrameSize;
            }

            // negotiates frame capabilities
            var engineFrameCapabilitiesItemValue = (string)engineSettings[Constants.Handshake.ItemKeyNames.FrameCapabilities];
            var engineFrameCapabilities = new HashSet<string>(engineFrameCapabilitiesItemValue.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));

            // can read fragmented payload only if we decide to (the other may or may not send fragmented payload
            _negotiatedSettings.FragmentationCapabilities.CanRead = _mySettings.FragmentationCapabilities.CanRead;

            // can write fragmented payload only if we decide to AND the other peer can receive them
            _negotiatedSettings.FragmentationCapabilities.CanWrite = _mySettings.FragmentationCapabilities.CanWrite && engineFrameCapabilities.Contains(Constants.Handshake.FrameCapabilities.Fragmentation);

            // pipelining only if both peers support it
            // TODO disconnect if pipelining not supported (or refactor ProcessNotifyFrameAsync to not use another thread to process the frame)
            _negotiatedSettings.HasPipeliningCapabilities = _mySettings.HasPipeliningCapabilities && engineFrameCapabilities.Contains(Constants.Handshake.FrameCapabilities.Pipelining);

            return _output.WriteAgentHelloAsync(_negotiatedSettings).AsTask();
        }

        private Task ProcessNotifyFrameAsync(ISpoaApplication application, in ReadOnlySequence<byte> payload)
        {
            // get and start a thread worker to process the payload
            var spopStream = GetFrameStream(application);
            StartFrameStream(spopStream);

            return spopStream.OnDataAsync(payload, (_incomingFrame.Flags & FrameFlags.Fin) == FrameFlags.Fin);
        }

        private Task ProcessContinuationFrameAsync(in ReadOnlySequence<byte> payload)
        {
            if (_streams.TryGetValue((_incomingFrame.StreamId, _incomingFrame.FrameId), out var spopStream))
            {
                if ((_incomingFrame.Flags & FrameFlags.Abort) == FrameFlags.Abort)
                {
                    spopStream.Abort();
                }
                else
                {
                    return spopStream.OnDataAsync(payload, (_incomingFrame.Flags & FrameFlags.Fin) == FrameFlags.Fin);
                }
            }

            // discard unknown frame (or should we disconnect with error 12 | frame-id not found (it does not match any referenced frame) ?)
            return Task.CompletedTask;
        }

        private Task ProcessDisconnectFrameAsync(in ReadOnlySequence<byte> payload)
        {
            var status = new Dictionary<string, object>();
            
            FrameReader.DecodeKeyValueListPayload(payload, status);
            var statusCode = (uint)status["status-code"];
            var message = (string)status["message"];
            
            logger.LogInformation($"HAProxy disconnect with status {statusCode}: {message}");

            StopProcessingNextRequest(agentInitiated: false);
            
            return Task.CompletedTask;
        }

        private void StopProcessingNextRequest(bool agentInitiated)
        {
            var initiator = agentInitiated ? GracefulCloseInitiator.Server : GracefulCloseInitiator.Client;

            if (Interlocked.CompareExchange(ref _gracefulCloseInitiator, initiator, GracefulCloseInitiator.None) == GracefulCloseInitiator.None)
            {
                Input.CancelPendingRead();
            }
        }

        private SpoaFrameStream GetFrameStream(ISpoaApplication application)
        {
            // TODO: pool to reuse instance
            // TODO: stream should expire
            var stream = new SpoaFrameStream(this.logger, application);

            stream.Initialize(_frameWriter, _memoryPool, this, _incomingFrame.StreamId, _incomingFrame.FrameId, _negotiatedSettings);
            return stream;
        }

        private void RemoveStream(SpoaFrameStream stream)
        {
            _streams.Remove((stream.StreamId, stream.FrameId));
            // stream.Dispose();
        }

        private void StartFrameStream(SpoaFrameStream spopStream)
        {
            _streams[(_incomingFrame.StreamId, _incomingFrame.FrameId)] = spopStream;
            ThreadPool.UnsafeQueueUserWorkItem(spopStream, preferLocal: false);
        }

        private async Task UpdateConnectionState()
        {
            if (_isClosed != 0)
            {
                return;
            }

            if (_gracefulCloseInitiator != GracefulCloseInitiator.None && !_gracefulCloseStarted)
            {
                _gracefulCloseStarted = true;
            }

            if (_gracefulCloseStarted)
            {
                if (TryClose())
                {
                    await _output.WriteAgentDisconnectAsync();
                }
            }
        }

        private bool TryClose()
        {
            if (Interlocked.Exchange(ref _isClosed, 1) == 0)
            {
                return true;
            }

            return false;
        }

        private async Task ReadInputAsync()
        {
            Exception error = null;
            try
            {
                while (true)
                {
                    var reader = _transport.Input;
                    var writer = _input.Writer;

                    var readResult = await reader.ReadAsync();

                    if (readResult.IsCompleted && readResult.Buffer.Length == 0 || readResult.IsCanceled)
                    {
                        // FIN
                        break;
                    }

                    var outputBuffer = writer.GetMemory(_minAllocBufferSize);

                    var copyAmount = (int)Math.Min(outputBuffer.Length, readResult.Buffer.Length);
                    var bufferSlice = readResult.Buffer.Slice(0, copyAmount);

                    bufferSlice.CopyTo(outputBuffer.Span);

                    reader.AdvanceTo(bufferSlice.End);
                    writer.Advance(copyAmount);

                    var result = await writer.FlushAsync();

                    if (result.IsCompleted || result.IsCanceled)
                    {
                        // flushResult should not be canceled.
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                // Don't rethrow the exception. will be handled by the Pipeline consumer.
                error = ex;
            }
            finally
            {
                await _transport.Input.CompleteAsync();
                _input.Writer.Complete(error);
            }
        }

        void ISpopFrameStreamLifetimeHandler.OnStreamCompleted(SpoaFrameStream stream)
        {
            _completedStreams.Enqueue(stream);
        }

        private static class GracefulCloseInitiator
        {
            public const int None = 0;
            public const int Server = 1;
            public const int Client = 2;
        }
    }

    internal interface ISpopFrameStreamLifetimeHandler
    {
        void OnStreamCompleted(SpoaFrameStream stream);
    }
}
