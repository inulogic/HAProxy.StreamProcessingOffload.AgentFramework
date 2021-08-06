namespace HAProxy.StreamProcessingOffload.AgentFramework.Spop
{
    partial class SpopFrameMetadata
    {
        public void PrepareEngineDisconnect()
        {
            Type = FrameType.HaproxyDisconnect;
            Flags = FrameFlags.Fin;
            StreamId = 0;
            FrameId = 0;
        }
    }
}
