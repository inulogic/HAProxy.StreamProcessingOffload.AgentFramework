namespace HAProxy.StreamProcessingOffload.AgentFramework.Spop;

public partial class SpopFrameMetadata
{
    public void PrepareAgentDisconnect()
    {
        this.Type = FrameType.AgentDisconnect;
        this.Flags = Frame.Fin;
        this.StreamId = 0;
        this.FrameId = 0;
    }
}
