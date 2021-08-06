namespace HAProxy.StreamProcessingOffload.AgentFramework.Spop
{
    public class SpopPeerSettings
    {
        /// <remarks>
        /// The maximum size supported by peers for a frame must be greater
        /// than or equal to 256 bytes.
        /// </remarks>
        internal const int MinAllowedMaxFrameSize = 256;

        internal const uint MaxAllowedMaxFrameSize = 16 * 1024 * 1024 - 1; // tune.bufsize-4

        //internal readonly string[] FrameCapabilities = new string[] { "fragmentation", "pipelining" };

        public static SpopPeerSettings AgentSettings { get; internal set; } = new SpopPeerSettings()
        {
            FragmentationCapabilities ={
                CanRead = true,
                CanWrite = true
            },
            HasPipeliningCapabilities = true,
            FrameSize = MaxAllowedMaxFrameSize,
            SupportedSpopVersion = "2.0"
        };

        /// <summary>
        /// Maximum allowed size for frames exchanged between HAProxy and SPOA.
        /// It is negotiated between HAProxy and agents during the HELLO handshake.
        /// </summary>
        /// <remarks>
        /// It must be in the range [256, tune.bufsize-4] (4 bytes are reserved for the
        /// frame length). By default, it is set to(tune.bufsize-4)
        /// </remarks>
        public uint FrameSize { get; set; }

        public string SupportedSpopVersion { get; set; }

        public Fragmentation FragmentationCapabilities { get; private set; } = new Fragmentation();

        public bool HasPipeliningCapabilities { get; set; }

        /// <summary>
        /// * fragmentation: This is the ability for a peer to support fragmented
        /// payload in received frames. This is an asymmectical
        /// capability, it only concerns the peer that announces
        /// it. This is the responsibility to the other peer to use it
        /// or not.
        /// </summary>
        public class Fragmentation
        {
            public bool CanRead { get; set; }
            public bool CanWrite { get; set; }
        }
    }
}
