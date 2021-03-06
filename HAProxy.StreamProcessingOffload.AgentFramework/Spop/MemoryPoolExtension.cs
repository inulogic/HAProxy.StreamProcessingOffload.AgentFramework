namespace HAProxy.StreamProcessingOffload.AgentFramework.Spop;
using System;
using System.Buffers;

internal static class MemoryPoolExtensions
{
    /// <summary>
    /// Computes a minimum segment size
    /// </summary>
    /// <param name="pool"></param>
    /// <returns></returns>
    public static int GetMinimumSegmentSize(this MemoryPool<byte> pool)
    {
        if (pool == null)
        {
            return 4096;
        }

        return Math.Min(4096, pool.MaxBufferSize);
    }

    public static int GetMinimumAllocSize(this MemoryPool<byte> pool) =>
        // 1/2 of a segment
        pool.GetMinimumSegmentSize() / 2;
}
