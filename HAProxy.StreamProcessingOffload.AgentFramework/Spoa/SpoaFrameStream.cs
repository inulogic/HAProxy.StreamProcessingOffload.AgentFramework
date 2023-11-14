namespace HAProxy.StreamProcessingOffload.AgentFramework.Spoa;
using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using HAProxy.StreamProcessingOffload.AgentFramework.Spop;
using Microsoft.Extensions.Logging;

// class to process notify frame
internal sealed class SpoaFrameStream(ILogger logger, ISpoaApplication application) : IThreadPoolWorkItem
{
    internal readonly ISpoaApplication application = application;
    internal readonly ILogger logger = logger;

    // allow to buffer the payload
    internal Pipe PayloadPipe { get; set; }

    internal PipeReader Input => this.PayloadPipe.Reader;

    internal MemoryPool<byte> memoryPool;
    internal SpopFrameWriter writer;

    public long StreamId { get; internal set; }
    public long FrameId { get; internal set; }

    internal ISpopFrameStreamLifetimeHandler streamLifetimeHandler;

    internal SpopFrameProducer output;

    internal SpopPeerSettings peerSettings;
    internal ExecutionContext initialExecutionContext;

    public void Initialize(
        SpopFrameWriter writer,
        MemoryPool<byte> memoryPool,
        ExecutionContext initialExecutionContext,
        ISpopFrameStreamLifetimeHandler streamLifetimeHandler,
        long streamId, long frameId, SpopPeerSettings peerSettings)
    {
        this.streamLifetimeHandler = streamLifetimeHandler;

        if (this.PayloadPipe == null)
        {
            this.StreamId = streamId;
            this.FrameId = frameId;

            this.writer = writer;
            this.output = new SpopFrameProducer(this.logger, this.writer);
            this.peerSettings = peerSettings;
            this.initialExecutionContext = initialExecutionContext;

            this.memoryPool = memoryPool;
            this.PayloadPipe = this.CreatePayloadPipe();
        }
        else
        {
            this.StreamId = streamId;
            this.FrameId = frameId;

            this.writer = writer;
            this.output = new SpopFrameProducer(this.logger, this.writer);
            this.peerSettings = peerSettings;
            this.initialExecutionContext = initialExecutionContext;

            this.PayloadPipe.Reset();
        }
    }

    public void Execute() => _ = this.ProcessPayloadAsync(this.application);

    internal async Task ProcessPayloadAsync(ISpoaApplication application)
    {
        // restore connection context (mainly for logging scope)
        ExecutionContext.Restore(this.initialExecutionContext);

        try
        {
            // Read will resume only when pipe is completed with a complete notify frame
            ReadResult readResult;
            while (true)
            {
                readResult = await this.Input.ReadAsync();

                if (readResult.IsCanceled)
                {
                    break;
                }

                if (!readResult.IsCompleted)
                {
                    continue;
                }

                var buffer = readResult.Buffer;

                try
                {
                    FrameReader.DecodeListOfMessagesPayload(readResult.Buffer, out var messages);

                    try
                    {
                        var actions = await application.ProcessMessagesAsync(this.StreamId, messages);

                        await this.output.WriteAgentAckAsync(this.StreamId, this.FrameId, actions, this.peerSettings);
                    }
                    catch (Exception ex)
                    {
                        // level debug to avoid log flooding if reccuring errors under heavy load
                        this.logger.LogDebug(ex, "error processing message");
                        // SPOP has no frame type to notify an error occured for a given frame only
                        // there is nothing we can do apart letting the frame timeout on engine side
                        // await _output.WriteAgentAckAsync(StreamId, FrameId, Enumerable.Empty<SpopAction>(), _peerSettings);
                    }

                    break;
                }
                finally
                {
                    this.PayloadPipe.Reader.AdvanceTo(readResult.Buffer.End, readResult.Buffer.End);
                }
            }
        }
        finally
        {
            await this.PayloadPipe.Reader.CompleteAsync();

            // allow this stream to be returned
            this.streamLifetimeHandler.OnStreamCompleted(this);
        }
    }

    public Task OnDataAsync(in ReadOnlySequence<byte> payload, bool completed)
    {
        foreach (var segment in payload)
        {
            this.PayloadPipe.Writer.Write(segment.Span);
        }

        if (!completed)
        {
            // Flushing is not necessary as the reader will let the buffer grows until the frame is completed
            return Task.CompletedTask;
        }
        else
        {
            // frame is complete, flush and complete will resume the reader thread
            return this.PayloadPipe.Writer.CompleteAsync().AsTask();
        }
    }

    internal Pipe CreatePayloadPipe()
        => new(new PipeOptions
        (
            pool: this.memoryPool,
            readerScheduler: PipeScheduler.ThreadPool,
            writerScheduler: PipeScheduler.Inline,
            useSynchronizationContext: false,
            minimumSegmentSize: this.memoryPool.GetMinimumSegmentSize()
        ));

    public void Abort() => this.Input.CancelPendingRead();
}
