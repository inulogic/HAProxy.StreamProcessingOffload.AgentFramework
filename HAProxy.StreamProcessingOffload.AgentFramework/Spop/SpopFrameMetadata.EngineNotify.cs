namespace HAProxy.StreamProcessingOffload.AgentFramework.Spop;

public partial class SpopFrameMetadata
{
    public void PrepareEngineNotify(long streamId, long frameId)
    {
        this.Type = FrameType.HaproxyNotify;
        this.Flags = Frame.None;
        this.StreamId = streamId;
        this.FrameId = frameId;
    }
}
