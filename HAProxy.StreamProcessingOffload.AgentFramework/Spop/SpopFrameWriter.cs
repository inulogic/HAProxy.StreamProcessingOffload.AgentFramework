namespace HAProxy.StreamProcessingOffload.AgentFramework.Spop;
using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

public class SpopFrameWriter
{
    private readonly object writeLock = new();
    private readonly ILogger logger;
    private readonly PipeWriter outputWriter;

    private bool completed;

    public SpopFrameWriter(
        ILogger logger,
        PipeWriter outputPipeWriter
        )
    {
        this.logger = logger;
        this.outputWriter = outputPipeWriter;
    }

    public ValueTask<FlushResult> WriteFrame(in ReadOnlySpan<byte> metadataSpan, in ReadOnlySpan<byte> framePayload)
    {
        lock (this.writeLock)
        {
            if (this.completed)
            {
                return default;
            }

            WriteFrame(this.outputWriter, metadataSpan, framePayload);
            return this.outputWriter.FlushAsync();
        }
    }

    public void Complete()
    {
        lock (this.writeLock)
        {
            if (this.completed)
            {
                return;
            }

            this.completed = true;
            this.outputWriter.CancelPendingFlush();
        }
    }

    private static void WriteFrame(IBufferWriter<byte> output, in ReadOnlySpan<byte> metadataSpan, in ReadOnlySpan<byte> framePayload)
    {
        var totalLength = metadataSpan.Length + framePayload.Length;
        var currentSpan = output.GetSpan(totalLength);

        metadataSpan.CopyTo(currentSpan);
        currentSpan = currentSpan[metadataSpan.Length..];
        // <FRAME-PAYLOAD:*>
        framePayload.CopyTo(currentSpan);

        output.Advance(totalLength);
    }
}
