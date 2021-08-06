namespace HAProxy.StreamProcessingOffload.AgentFramework.Spop
{
    internal enum DataType : byte
    {
        Null = 0,
        Boolean = 1,
        Int32 = 2,
        Uint32 = 3,
        Int64 = 4,
        Uint64 = 5,
        Ipv4 = 6,
        Ipv6 = 7,
        String = 8,
        Binary = 9,
        ReservedBegin = 10,
        ReservedEnd = 15
    }
}
