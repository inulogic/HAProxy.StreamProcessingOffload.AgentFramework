namespace HAProxy.StreamProcessingOffload.AgentFramework.Spop;

public partial class SpopFrameMetadata
{
    public void PrepareEngineHello()
    {
        this.Type = FrameType.HaproxyHello;
        this.Flags = Frame.Fin;
        this.StreamId = 0;
        this.FrameId = 0;
    }
}
