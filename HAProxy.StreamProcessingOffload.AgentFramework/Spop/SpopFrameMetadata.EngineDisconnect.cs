namespace HAProxy.StreamProcessingOffload.AgentFramework.Spop;

public partial class SpopFrameMetadata
{
    public void PrepareEngineDisconnect()
    {
        this.Type = FrameType.HaproxyDisconnect;
        this.Flags = Frame.Fin;
        this.StreamId = 0;
        this.FrameId = 0;
    }
}
