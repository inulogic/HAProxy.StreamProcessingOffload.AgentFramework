namespace HAProxy.StreamProcessingOffload.AgentFramework.Spop
{
    partial class SpopFrameMetadata
    {
        public void PrepareEngineHello()
        {
            Type = FrameType.HaproxyHello;
            Flags = FrameFlags.Fin;
            StreamId = 0;
            FrameId = 0;
        }
    }
}
