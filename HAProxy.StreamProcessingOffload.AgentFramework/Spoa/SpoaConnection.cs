namespace HAProxy.StreamProcessingOffload.AgentFramework.Spoa;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using HAProxy.StreamProcessingOffload.AgentFramework.Spop;
using Microsoft.Extensions.Logging;

// based on https://github.com/dotnet/aspnetcore/blob/main/src/Servers/Kestrel/Core/src/Internal/Http2/Http2Connection.cs
public class SpoaConnection : ISpopFrameStreamLifetimeHandler
{
    private readonly ILogger logger;

    // context
    private readonly IDuplexPipe transport;
    private readonly MemoryPool<byte> memoryPool;
    private readonly SpopFrameWriter frameWriter;
    private readonly SpopFrameProducer output;
    private readonly Pipe input;
    private readonly Task inputTask;
    private readonly ExecutionContext initialExecutionContext;
    private readonly int minAllocBufferSize;

    private readonly SpopPeerSettings mySettings = SpopPeerSettings.AgentSettings;
    private readonly SpopPeerSettings negotiatedSettings = new();

    private readonly SpopFrameMetadata incomingFrame = new();

    private readonly ConcurrentQueue<SpoaFrameStream> completedStreams = new();
    private int gracefulCloseInitiator;
    private bool gracefulCloseStarted;
    private int isClosed;

    // Each notify frames will spawn a stream to process the frame set.
    // Streams are thread pool work item to allow pipelining
    // The dictionary is accessed only from the main loop thread, no lock required
    private readonly Dictionary<(long, long), SpoaFrameStream> _streams = new();

    private const int InitialStreamPoolSize = 5;
    private const int MaxStreamPoolSize = 40; // max-waiting-frames in spoe conf must be less than this, default is 20

    public SpoaConnection(ILogger logger, IDuplexPipe transport)
    {
        this.logger = logger;
        this.transport = transport;
        this.memoryPool = MemoryPool<byte>.Shared;

        this.frameWriter = new SpopFrameWriter(this.logger, this.transport.Output);
        this.output = new SpopFrameProducer(logger, this.frameWriter);

        var inputOptions = new PipeOptions();
        this.input = new Pipe(inputOptions);
        this.minAllocBufferSize = this.memoryPool.GetMinimumAllocSize();

        this.inputTask = this.ReadInputAsync();

        // Capture the ExecutionContext so it can be restored in the stream thread
        this.initialExecutionContext = ExecutionContext.Capture();
    }

    public PipeReader Input => this.input.Reader;

    public async ValueTask ProcessConnectionAsync(ISpoaApplication application, CancellationToken cancellationToken = default)
    {
        Exception error = null;

        try
        {
            using var _ = cancellationToken.Register(() => this.StopProcessingNextRequest(true), useSynchronizationContext: false);

            // frame processing
            while (this.isClosed == 0)
            {
                var result = await this.Input.ReadAsync(cancellationToken);
                var buffer = result.Buffer;

                this.UpdateCompletedStreams();

                try
                {
                    while (FrameReader.TryReadFrame(ref buffer, this.incomingFrame, out var framePayload))
                    {
                        this.logger.LogDebug("rcv frame {IncomingFrame}", this.incomingFrame);
                        if (cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }

                        await this.ProcessFrameAsync(application, framePayload);
                    }

                    if (result.IsCompleted)
                    {
                        return;
                    }
                }
                finally
                {
                    this.Input.AdvanceTo(buffer.Start, buffer.End);

                    await this.UpdateConnectionState();
                }
            }
        }
        catch (Exception ex)
        {
            error = ex;
            this.logger.LogError(ex, "aborting connection");
        }
        finally
        {
            // disconnect
            try
            {
                if (this.TryClose())
                {
                    if (error != null)
                    {
                        var (status, message) = GetErrorStatus(error);
                        await this.output.WriteAgentDisconnectAsync(status, message);
                    }
                    else
                    {
                        await this.output.WriteAgentDisconnectAsync();
                    }
                }

                foreach (var stream in this._streams.Values)
                {
                    stream.Abort();
                }

                while (this._streams.Count > 0)
                {
                    this.UpdateCompletedStreams();
                }

                this.frameWriter.Complete();
            }
            catch
            {
                // _frameWriter.Abort();
                throw;
            }
            finally
            {
                this.Input.Complete();
                this.transport.Input.CancelPendingRead();
                await this.inputTask;
            }
        }
    }

    private static (int status, string message) GetErrorStatus(Exception ex) => ex switch
    {
        IOException iOException => (Constants.StatusCode.IO, iOException.Message),
        _ => (Constants.StatusCode.UnknownError, ex.Message),
    };

    private void UpdateCompletedStreams()
    {
        while (this.completedStreams.TryDequeue(out var stream))
        {
            this.RemoveStream(stream);
        }
    }

    private Task ProcessFrameAsync(ISpoaApplication application, in ReadOnlySequence<byte> payload) => this.incomingFrame.Type switch
    {
        FrameType.HaproxyHello => this.ProcessHandshake(payload),
        FrameType.HaproxyNotify => this.ProcessNotifyFrameAsync(application, payload),
        FrameType.Unset => this.ProcessContinuationFrameAsync(payload),
        FrameType.HaproxyDisconnect => this.ProcessDisconnectFrameAsync(payload),
        FrameType.AgentHello => throw new NotImplementedException(),
        FrameType.AgentDisconnect => throw new NotImplementedException(),
        FrameType.AgentAck => throw new NotImplementedException(),
        _ => ProcessUnknownFrameAsync(),
    };

    private static Task ProcessUnknownFrameAsync() =>
        // discard (or should we disconnect with 4 | invalid frame received)
        Task.CompletedTask;

    private Task ProcessHandshake(in ReadOnlySequence<byte> payload)
    {
        var engineSettings = new Dictionary<string, object>();
        FrameReader.DecodeKeyValueListPayload(payload, engineSettings);

        if (engineSettings.TryGetValue(Constants.Handshake.ItemKeyNames.Healthcheck, out var healthcheck) && (bool)healthcheck)
        {
            this.logger.LogDebug("healthcheck");
            this.StopProcessingNextRequest(false);
            return Task.CompletedTask;
        }

        var engineSupportedVersionsItemValue = (string)engineSettings[Constants.Handshake.ItemKeyNames.SupportedVersions];
        var engineSupportedVersion = new HashSet<string>(engineSupportedVersionsItemValue.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        if (engineSupportedVersion.Contains(this.mySettings.SupportedSpopVersion))
        {
            this.negotiatedSettings.SupportedSpopVersion = this.mySettings.SupportedSpopVersion;
        }
        else
        {
            throw new Exception("unsupported version");
        }

        // negotiates frame size
        var engineMaxFrameSize = (uint)engineSettings[Constants.Handshake.ItemKeyNames.MaxFrameSize];
        if (engineMaxFrameSize < this.mySettings.FrameSize)
        {
            this.negotiatedSettings.FrameSize = engineMaxFrameSize;
        }

        // negotiates frame capabilities
        var engineFrameCapabilitiesItemValue = (string)engineSettings[Constants.Handshake.ItemKeyNames.FrameCapabilities];
        var engineFrameCapabilities = new HashSet<string>(engineFrameCapabilitiesItemValue.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));

        // can read fragmented payload only if we decide to (the other may or may not send fragmented payload
        this.negotiatedSettings.FragmentationCapabilities.CanRead = this.mySettings.FragmentationCapabilities.CanRead;

        // can write fragmented payload only if we decide to AND the other peer can receive them
        this.negotiatedSettings.FragmentationCapabilities.CanWrite = this.mySettings.FragmentationCapabilities.CanWrite && engineFrameCapabilities.Contains(Constants.Handshake.FrameCapabilities.Fragmentation);

        // pipelining only if both peers support it
        // TODO disconnect if pipelining not supported (or refactor ProcessNotifyFrameAsync to not use another thread to process the frame)
        this.negotiatedSettings.HasPipeliningCapabilities = this.mySettings.HasPipeliningCapabilities && engineFrameCapabilities.Contains(Constants.Handshake.FrameCapabilities.Pipelining);

        return this.output.WriteAgentHelloAsync(this.negotiatedSettings).AsTask();
    }

    private Task ProcessNotifyFrameAsync(ISpoaApplication application, in ReadOnlySequence<byte> payload)
    {
        // get and start a thread worker to process the payload
        var spopStream = this.GetFrameStream(application);
        this.StartFrameStream(spopStream);

        return spopStream.OnDataAsync(payload, (this.incomingFrame.Flags & Frame.Fin) == Frame.Fin);
    }

    private Task ProcessContinuationFrameAsync(in ReadOnlySequence<byte> payload)
    {
        if (this._streams.TryGetValue((this.incomingFrame.StreamId, this.incomingFrame.FrameId), out var spopStream))
        {
            if ((this.incomingFrame.Flags & Frame.Abort) == Frame.Abort)
            {
                spopStream.Abort();
            }
            else
            {
                return spopStream.OnDataAsync(payload, (this.incomingFrame.Flags & Frame.Fin) == Frame.Fin);
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

        this.logger.LogInformation("HAProxy disconnect with status {StatusCode}: {Message}", statusCode, message);

        this.StopProcessingNextRequest(agentInitiated: false);

        return Task.CompletedTask;
    }

    private void StopProcessingNextRequest(bool agentInitiated)
    {
        var initiator = agentInitiated ? GracefulCloseInitiator.Server : GracefulCloseInitiator.Client;

        if (Interlocked.CompareExchange(ref this.gracefulCloseInitiator, initiator, GracefulCloseInitiator.None) == GracefulCloseInitiator.None)
        {
            this.Input.CancelPendingRead();
        }
    }

    private SpoaFrameStream GetFrameStream(ISpoaApplication application)
    {
        // TODO: pool to reuse instance
        // TODO: stream should expire
        var stream = new SpoaFrameStream(this.logger, application);

        stream.Initialize(this.frameWriter, this.memoryPool, this.initialExecutionContext, this, this.incomingFrame.StreamId, this.incomingFrame.FrameId, this.negotiatedSettings);
        return stream;
    }

    private void RemoveStream(SpoaFrameStream stream) => this._streams.Remove((stream.StreamId, stream.FrameId));// stream.Dispose();

    private void StartFrameStream(SpoaFrameStream spopStream)
    {
        this._streams[(this.incomingFrame.StreamId, this.incomingFrame.FrameId)] = spopStream;
        ThreadPool.UnsafeQueueUserWorkItem(spopStream, preferLocal: false);
    }

    private async Task UpdateConnectionState()
    {
        if (this.isClosed != 0)
        {
            return;
        }

        if (this.gracefulCloseInitiator != GracefulCloseInitiator.None && !this.gracefulCloseStarted)
        {
            this.gracefulCloseStarted = true;
        }

        if (this.gracefulCloseStarted)
        {
            if (this.TryClose())
            {
                await this.output.WriteAgentDisconnectAsync();
            }
        }
    }

    private bool TryClose()
    {
        if (Interlocked.Exchange(ref this.isClosed, 1) == 0)
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
                var reader = this.transport.Input;
                var writer = this.input.Writer;

                var readResult = await reader.ReadAsync();

                if ((readResult.IsCompleted && readResult.Buffer.Length == 0) || readResult.IsCanceled)
                {
                    // FIN
                    break;
                }

                var outputBuffer = writer.GetMemory(this.minAllocBufferSize);

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
            await this.transport.Input.CompleteAsync();
            this.input.Writer.Complete(error);
        }
    }

    void ISpopFrameStreamLifetimeHandler.OnStreamCompleted(SpoaFrameStream stream) => this.completedStreams.Enqueue(stream);

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
