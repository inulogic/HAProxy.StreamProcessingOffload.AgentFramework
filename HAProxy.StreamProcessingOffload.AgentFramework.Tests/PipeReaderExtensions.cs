namespace HAProxy.StreamProcessingOffload.AgentFramework.Tests;
using System.Buffers;
using System.IO.Pipelines;
using System.Linq;
using System.Threading.Tasks;

public static class PipeReaderExtensions
{
    public static async Task<byte[]> ReadForLengthAsync(this PipeReader pipeReader, int length)
    {
        while (true)
        {
            var result = await pipeReader.ReadAsync();
            var buffer = result.Buffer;

            if (!buffer.IsEmpty && buffer.Length >= length)
            {
                return buffer.Slice(0, length).ToArray();
            }

            pipeReader.AdvanceTo(buffer.Start, buffer.End);
        }
    }
}
