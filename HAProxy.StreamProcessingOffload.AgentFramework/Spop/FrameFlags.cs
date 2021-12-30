namespace HAProxy.StreamProcessingOffload.AgentFramework.Spop;
using System;

[Flags]
public enum Frame : byte
{
    None = 0x0000,
    Fin = 0x0001,
    Abort = 0x0010
}
