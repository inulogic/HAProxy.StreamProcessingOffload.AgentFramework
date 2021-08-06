namespace HAProxy.StreamProcessingOffload.AgentFramework.Spop
{
    internal class Constants
    {
        public class Disconnect
        {
            public class ItemKeyNames
            {
                public const string StatusCode = "status-code";
                public const string Message = "message";
            }
        }

        public class Handshake
        {
            public class ItemKeyNames
            {
                public const string SupportedVersions = "supported-versions";
                public const string Version = "version";
                public const string MaxFrameSize = "max-frame-size";
                public const string FrameCapabilities = "capabilities";
                public const string Healthcheck = "healthcheck";
                public const string EngineId = "engine-id";
            }

            public class FrameCapabilities
            {
                public const string Fragmentation = "fragmentation";
                public const string Pipelining = "pipelining";
            }
        }

        public class StatusCode
        {
            /// <summary>
            /// normal (no error occurred)
            /// </summary>
            public const int None = 0;
            /// <summary>
            /// I/O error
            /// </summary>
            public const int IO = 1;
            /// <summary>
            /// A timeout occurred
            /// </summary>
            public const int Timeout = 2;
            /// <summary>
            /// frame is too big
            /// </summary>
            public const int TooBig = 3;
            /// <summary>
            /// invalid frame received
            /// </summary>
            public const int InvalidFrame = 4;
            /// <summary>
            /// version value not found
            /// </summary>
            public const int VersionValueNotFound = 5;
            /// <summary>
            /// max-frame-size value not found
            /// </summary>
            public const int MaxFrameSizeNotFound = 6;
            /// <summary>
            /// capabilities value not found
            /// </summary>
            public const int CapabilitiesNotFound = 7;
            /// <summary>
            /// unsupported version
            /// </summary>
            public const int UnsupportedVersion = 8;
            /// <summary>
            /// max-frame-size too big or too small
            /// </summary>
            public const int InvalidMaxFrameSize = 9;
            /// <summary>
            /// payload fragmentation is not supported
            /// </summary>
            public const int FragmentationNotSupported = 10;
            /// <summary>
            /// invalid interlaced frames
            /// </summary>
            public const int InvalidInterlacedFrames = 11;
            /// <summary>
            /// frame-id not found (it does not match any referenced frame)
            /// </summary>
            public const int FrameIdNotFound = 12;
            /// <summary>
            /// resource allocation error
            /// </summary>
            public const int ResourceAllocationError = 13;
            /// <summary>
            /// an unknown error occurred
            /// </summary>
            public const int UnknownError = 99;

        }
    }
}
