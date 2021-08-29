using HAProxy.StreamProcessingOffload.AgentFramework.Spop;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace HAProxy.StreamProcessingOffload.AgentFramework.Spoa
{
    // class to process notify frame
    internal class SpoaFrameStream : IThreadPoolWorkItem
    {
        private readonly ISpoaApplication _application;
        private readonly ILogger _logger;

        // allow to buffer the payload
        private Pipe PayloadPipe { get; set; }

        private PipeReader Input => PayloadPipe.Reader;

        private MemoryPool<byte> _memoryPool;
        private SpopFrameWriter _writer;

        public long StreamId { get; private set; }
        public long FrameId { get; private set; }

        private ISpopFrameStreamLifetimeHandler _streamLifetimeHandler;

        private SpopFrameProducer _output;

        private SpopPeerSettings _peerSettings;
        private ExecutionContext _initialExecutionContext;

        public SpoaFrameStream(ILogger logger, ISpoaApplication application)
        {
            _logger = logger;
            _application = application;
        }

        public void Initialize(
            SpopFrameWriter writer,
            MemoryPool<byte> memoryPool,
            ExecutionContext initialExecutionContext,
            ISpopFrameStreamLifetimeHandler streamLifetimeHandler,
            long streamId, long frameId, SpopPeerSettings peerSettings)
        {
            _streamLifetimeHandler = streamLifetimeHandler;

            if (PayloadPipe == null)
            {
                StreamId = streamId;
                FrameId = frameId;

                _writer = writer;
                _output = new SpopFrameProducer(_logger, _writer);
                _peerSettings = peerSettings;
                _initialExecutionContext = initialExecutionContext;

                _memoryPool = memoryPool;
                PayloadPipe = CreatePayloadPipe();
            }
            else
            {
                StreamId = streamId;
                FrameId = frameId;

                _writer = writer;
                _output = new SpopFrameProducer(_logger, _writer);
                _peerSettings = peerSettings;
                _initialExecutionContext = initialExecutionContext;

                PayloadPipe.Reset();
            }
        }

        public void Execute()
        {
            _ = ProcessPayloadAsync(_application);
        }

        private async Task ProcessPayloadAsync(ISpoaApplication application)
        {
            // restore connection context (mainly for logging scope)
            ExecutionContext.Restore(_initialExecutionContext);

            try
            {
                // Read will resume only when pipe is completed with a complete notify frame
                ReadResult readResult;
                while (true)
                {
                    readResult = await Input.ReadAsync();

                    if (readResult.IsCanceled) break;
                    if (!readResult.IsCompleted) continue;

                    var buffer = readResult.Buffer;

                    try
                    {
                        FrameReader.DecodeListOfMessagesPayload(readResult.Buffer, out var messages);

                        try
                        {
                            var actions = await application.ProcessMessagesAsync(StreamId, messages);

                            await _output.WriteAgentAckAsync(StreamId, FrameId, actions, _peerSettings);
                        }
                        catch(Exception ex)
                        {
                            // level debug to avoid log flooding if reccuring errors under heavy load
                            _logger.LogDebug(ex, "error processing message");
                            // SPOP has no frame type to notify an error occured for a given frame only
                            // there is nothing we can do apart letting the frame timeout on engine side
                            // await _output.WriteAgentAckAsync(StreamId, FrameId, Enumerable.Empty<SpopAction>(), _peerSettings);
                        }

                        break;
                    }
                    finally
                    {
                        PayloadPipe.Reader.AdvanceTo(readResult.Buffer.End, readResult.Buffer.End);
                    }
                }
            }
            finally
            {
                await PayloadPipe.Reader.CompleteAsync();

                // allow this stream to be returned
                _streamLifetimeHandler.OnStreamCompleted(this);
            }
        }

        public Task OnDataAsync(in ReadOnlySequence<byte> payload, bool completed)
        {
            foreach (var segment in payload)
            {
                PayloadPipe.Writer.Write(segment.Span);
            }

            if (!completed)
            {
                // Flushing is not necessary as the reader will let the buffer grows until the frame is completed
                return Task.CompletedTask;
            }
            else
            {
                // frame is complete, flush and complete will resume the reader thread
                return PayloadPipe.Writer.CompleteAsync().AsTask();
            }
        }

        private Pipe CreatePayloadPipe()
            => new Pipe(new PipeOptions
            (
                pool: _memoryPool,
                readerScheduler: PipeScheduler.ThreadPool,
                writerScheduler: PipeScheduler.Inline,
                useSynchronizationContext: false,
                minimumSegmentSize: _memoryPool.GetMinimumSegmentSize()
            ));

        public void Abort()
        {
            Input.CancelPendingRead();
        }
    }
}
