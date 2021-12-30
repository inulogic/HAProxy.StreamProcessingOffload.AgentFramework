namespace HAProxy.StreamProcessingOffload.AgentFramework.Spop;

public partial class SpopFrameMetadata
{
    public void PrepareAgentHello()
    {
        this.Type = FrameType.AgentHello;
        this.Flags = Frame.Fin;
        this.StreamId = 0;
        this.FrameId = 0;
    }
}
