namespace HAProxy.StreamProcessingOffload.AgentFramework.Spop
{
    partial class SpopFrameMetadata
    {
        public void PrepareAgentHello()
        {
            Type = FrameType.AgentHello;
            Flags = FrameFlags.Fin;
            StreamId = 0;
            FrameId = 0;
        }
    }
}
