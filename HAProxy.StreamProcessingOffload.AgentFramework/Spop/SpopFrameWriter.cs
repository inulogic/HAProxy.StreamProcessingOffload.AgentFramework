using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Threading.Tasks;

namespace HAProxy.StreamProcessingOffload.AgentFramework.Spop
{
    public class SpopFrameWriter
    {
        private readonly object _writeLock = new object();
        private readonly ILogger _logger;
        private readonly PipeWriter _outputWriter;

        private bool _completed;

        public SpopFrameWriter(
            ILogger logger,
            PipeWriter outputPipeWriter
            )
        {
            _logger = logger;
            _outputWriter = outputPipeWriter;
        }

        public ValueTask<FlushResult> WriteFrame(in ReadOnlySpan<byte> metadataSpan, in ReadOnlySpan<byte> framePayload)
        {
            lock (_writeLock)
            {
                if (_completed)
                {
                    return default;
                }

                WriteFrame(_outputWriter, metadataSpan, framePayload);
                return _outputWriter.FlushAsync();
            }
        }

        public void Complete()
        {
            lock (_writeLock)
            {
                if (_completed)
                {
                    return;
                }

                _completed = true;
                _outputWriter.CancelPendingFlush();
            }
        }

        private static void WriteFrame(IBufferWriter<byte> output, in ReadOnlySpan<byte> metadataSpan, in ReadOnlySpan<byte> framePayload)
        {
            int totalLength = metadataSpan.Length + framePayload.Length;
            var currentSpan = output.GetSpan(totalLength);

            metadataSpan.CopyTo(currentSpan);
            currentSpan = currentSpan.Slice(metadataSpan.Length);
            // <FRAME-PAYLOAD:*>
            framePayload.CopyTo(currentSpan);

            output.Advance(totalLength);
        }
    }
}
