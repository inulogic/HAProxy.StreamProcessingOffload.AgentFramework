namespace HAProxy.StreamProcessingOffload.AgentFramework.Spop;

public partial class SpopFrameMetadata
{
    public void PrepareAgentAck(long streamId, long frameId)
    {
        this.Type = FrameType.AgentAck;
        this.Flags = Frame.None;
        this.StreamId = streamId;
        this.FrameId = frameId;
    }
}
