namespace HAProxy.StreamProcessingOffload.AgentFramework.Spop
{
    public enum FrameType : byte
    {
        Unset = 0,
        HaproxyHello = 1,
        HaproxyDisconnect = 2,
        HaproxyNotify = 3,
        AgentHello = 101,
        AgentDisconnect = 102,
        AgentAck = 103
    }
}
