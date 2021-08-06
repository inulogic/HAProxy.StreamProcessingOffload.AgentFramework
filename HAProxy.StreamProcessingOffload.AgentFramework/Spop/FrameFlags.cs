using System;

namespace HAProxy.StreamProcessingOffload.AgentFramework.Spop
{
    [Flags]
    public enum FrameFlags : byte
    {
        None = 0x0000,
        Fin = 0x0001,
        Abort = 0x0010
    }
}
