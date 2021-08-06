namespace HAProxy.StreamProcessingOffload.AgentFramework.Spop
{
    partial class SpopFrameMetadata
    {
        public void PrepareEngineNotify(long streamId, long frameId)
        {
            Type = FrameType.HaproxyNotify;
            Flags = FrameFlags.None;
            StreamId = streamId;
            FrameId = frameId;
        }
    }
}
