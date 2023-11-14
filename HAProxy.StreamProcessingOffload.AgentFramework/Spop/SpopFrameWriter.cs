namespace HAProxy.StreamProcessingOffload.AgentFramework.Spop;
using System;
using System.IO.Pipelines;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

public class SpopFrameWriter(
    ILogger logger,
    PipeWriter outputPipeWriter
        )
{
    internal readonly object writeLock = new();
    internal readonly ILogger logger = logger;
    internal readonly PipeWriter outputWriter = outputPipeWriter;

    internal bool completed;

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

    internal static void WriteFrame(PipeWriter output, in ReadOnlySpan<byte> metadataSpan, in ReadOnlySpan<byte> framePayload)
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
