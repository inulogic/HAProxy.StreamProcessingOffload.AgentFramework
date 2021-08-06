namespace HAProxy.StreamProcessingOffload.AgentFramework.Spop
{
    partial class SpopFrameMetadata
    {
        public void PrepareAgentAck(long streamId, long frameId)
        {
            Type = FrameType.AgentAck;
            Flags = FrameFlags.None;
            StreamId = streamId;
            FrameId = frameId;
        }
    }
}
