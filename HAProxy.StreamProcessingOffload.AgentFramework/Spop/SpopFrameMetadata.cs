namespace HAProxy.StreamProcessingOffload.AgentFramework.Spop
{
    public partial class SpopFrameMetadata
    {
        public FrameType Type { get; internal set; }

        public FrameFlags Flags { get; internal set; }
        public long StreamId { get; internal set; }
        public long FrameId { get; internal set; }

        public override string ToString()
        {
            return $"{StreamId}/{FrameId} {Type}";
        }
    }
}
