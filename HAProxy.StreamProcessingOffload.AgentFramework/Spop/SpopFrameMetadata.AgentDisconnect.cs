namespace HAProxy.StreamProcessingOffload.AgentFramework.Spop
{
    partial class SpopFrameMetadata
    {
        public void PrepareAgentDisconnect()
        {
            Type = FrameType.AgentDisconnect;
            Flags = FrameFlags.Fin;
            StreamId = 0;
            FrameId = 0;
        }
    }
}
