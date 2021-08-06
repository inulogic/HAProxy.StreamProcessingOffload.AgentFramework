using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace HAProxy.StreamProcessingOffload.AgentFramework.Spop
{
    /// <summary>
    /// Will not produce fragmented frame as HAProxy still do not support it
    /// Support pipelining but not async (reuse the same TCP connection used for the corresponding NOTIFY frame)
    /// </summary>
    public class SpopFrameProducer
    {
        private static readonly byte[] StatusCodeByteArray = Encoding.ASCII.GetBytes(Constants.Disconnect.ItemKeyNames.StatusCode);
        private static readonly byte[] MessageByteArray = Encoding.ASCII.GetBytes(Constants.Disconnect.ItemKeyNames.Message);
        private static readonly byte[] VersionByteArray = Encoding.ASCII.GetBytes(Constants.Handshake.ItemKeyNames.Version);
        private static readonly byte[] SupportedVersionsByteArray = Encoding.ASCII.GetBytes(Constants.Handshake.ItemKeyNames.SupportedVersions);
        private static readonly byte[] MaxFrameSizeByteArray = Encoding.ASCII.GetBytes(Constants.Handshake.ItemKeyNames.MaxFrameSize);
        private static readonly byte[] CapabilitiesByteArray = Encoding.ASCII.GetBytes(Constants.Handshake.ItemKeyNames.FrameCapabilities);
        private static readonly byte[] HealthcheckByteArray = Encoding.ASCII.GetBytes(Constants.Handshake.ItemKeyNames.Healthcheck);

        private static ReadOnlySpan<byte> StatusCodeBytes => StatusCodeByteArray;
        private static ReadOnlySpan<byte> MessageBytes => MessageByteArray;
        private static ReadOnlySpan<byte> VersionBytes => VersionByteArray;
        private static ReadOnlySpan<byte> SupportedVersionsBytes => SupportedVersionsByteArray;
        private static ReadOnlySpan<byte> MaxFrameSizeBytes => MaxFrameSizeByteArray;
        private static ReadOnlySpan<byte> CapabilitiesBytes => CapabilitiesByteArray;
        private static ReadOnlySpan<byte> HealthcheckBytes => HealthcheckByteArray;

        private readonly ILogger logger;
        private readonly SpopFrameWriter _frameWriter;
        private readonly object _writeLock = new object();
        private readonly SpopFrameMetadata _outgoingFrame = new SpopFrameMetadata();

        public SpopFrameProducer(ILogger logger, SpopFrameWriter frameWriter)
        {
            this.logger = logger;
            _frameWriter = frameWriter;
        }

        public ValueTask<FlushResult> WriteAgentDisconnectAsync(int statusCode = 0, string message = "")
        {
            lock (_writeLock)
            {
                _outgoingFrame.PrepareAgentDisconnect();

                ArrayBufferWriter<byte> buffer = new ArrayBufferWriter<byte>(SpopPeerSettings.MinAllowedMaxFrameSize);
                int bytePending = 0;
                WriteString(buffer, ref bytePending, StatusCodeBytes);
                WriteTypedData(buffer, ref bytePending, statusCode);
                WriteString(buffer, ref bytePending, MessageBytes);
                WriteTypedData(buffer, ref bytePending, message);
                buffer.Advance(bytePending);

                return WriteFrameUnfragemntedPayload(buffer.WrittenSpan);
            }
        }

        public ValueTask<FlushResult> WriteAgentHelloAsync(SpopPeerSettings peerSettings)
        {
            lock (_writeLock)
            {
                _outgoingFrame.PrepareAgentHello();

                ArrayBufferWriter<byte> buffer = new ArrayBufferWriter<byte>(SpopPeerSettings.MinAllowedMaxFrameSize);
                int bytePending = 0;
                WriteString(buffer, ref bytePending, VersionBytes);
                WriteTypedData(buffer, ref bytePending, peerSettings.SupportedSpopVersion);
                WriteString(buffer, ref bytePending, MaxFrameSizeBytes);
                WriteTypedData(buffer, ref bytePending, (uint)peerSettings.FrameSize);
                WriteString(buffer, ref bytePending, CapabilitiesBytes);
                WriteTypedData(buffer, ref bytePending, GetCapabilities(peerSettings));
                buffer.Advance(bytePending);

                return WriteFrameUnfragemntedPayload(buffer.WrittenSpan);
            }
        }

        public ValueTask<FlushResult> WriteAgentAckAsync(long streamId, long frameId, IEnumerable<SpopAction> actions, SpopPeerSettings settings)
        {
            lock (_writeLock)
            {
                _outgoingFrame.PrepareAgentAck(streamId, frameId);

                ArrayBufferWriter<byte> buffer = new ArrayBufferWriter<byte>(SpopPeerSettings.MinAllowedMaxFrameSize);
                int bytePending = 0;
                WriteListOfActions(buffer, ref bytePending, actions);
                buffer.Advance(bytePending);

                if (settings.FragmentationCapabilities.CanWrite)
                    return WriteFrameFragmentedPayload(buffer.WrittenMemory, (int)settings.FrameSize);
                else
                    return WriteFrameUnfragemntedPayload(buffer.WrittenSpan);
            }
        }

        public ValueTask<FlushResult> WriteEngineHelloAsync(SpopPeerSettings peerSettings, bool isHealthCheck = false)
        {
            lock (_writeLock)
            {
                _outgoingFrame.PrepareEngineHello();

                ArrayBufferWriter<byte> buffer = new ArrayBufferWriter<byte>(SpopPeerSettings.MinAllowedMaxFrameSize);
                int bytePending = 0;
                WriteString(buffer, ref bytePending, SupportedVersionsBytes);
                WriteTypedData(buffer, ref bytePending, peerSettings.SupportedSpopVersion);
                WriteString(buffer, ref bytePending, MaxFrameSizeBytes);
                WriteTypedData(buffer, ref bytePending, (uint)peerSettings.FrameSize);
                WriteString(buffer, ref bytePending, CapabilitiesBytes);
                WriteTypedData(buffer, ref bytePending, GetCapabilities(peerSettings));
                if (isHealthCheck)
                {
                    WriteString(buffer, ref bytePending, HealthcheckBytes);
                    WriteTypedData(buffer, ref bytePending, true);
                }
                buffer.Advance(bytePending);

                return WriteFrameUnfragemntedPayload(buffer.WrittenSpan);
            }
        }

        private static string GetCapabilities(SpopPeerSettings peerSettings)
        {
            List<string> capabilities = new List<string>(2);
            if (peerSettings.FragmentationCapabilities.CanRead) capabilities.Add(Constants.Handshake.FrameCapabilities.Fragmentation);
            if (peerSettings.HasPipeliningCapabilities) capabilities.Add(Constants.Handshake.FrameCapabilities.Pipelining);

            return string.Join(',', capabilities);
        }

        public ValueTask<FlushResult> WriteEngineNotifyAsync(long streamId, long frameId, IEnumerable<SpopMessage> messages, SpopPeerSettings settings)
        {
            lock (_writeLock)
            {
                _outgoingFrame.PrepareEngineNotify(streamId, frameId);

                ArrayBufferWriter<byte> buffer = new ArrayBufferWriter<byte>(SpopPeerSettings.MinAllowedMaxFrameSize);
                int bytePending = 0;
                WriteListOfMessages(buffer, ref bytePending, messages);
                buffer.Advance(bytePending);

                if (settings.FragmentationCapabilities.CanWrite)
                    return WriteFrameFragmentedPayload(buffer.WrittenMemory, (int)settings.FrameSize);
                else
                    return WriteFrameUnfragemntedPayload(buffer.WrittenSpan);
            }
        }

        public ValueTask<FlushResult> WriteEngineDisconnectAsync(uint statusCode = 0, string message = "")
        {
            lock (_writeLock)
            {
                _outgoingFrame.PrepareEngineDisconnect();

                ArrayBufferWriter<byte> buffer = new ArrayBufferWriter<byte>(SpopPeerSettings.MinAllowedMaxFrameSize);
                int bytePending = 0;
                WriteString(buffer, ref bytePending, StatusCodeBytes);
                WriteTypedData(buffer, ref bytePending, statusCode);
                WriteString(buffer, ref bytePending, MessageBytes);
                WriteString(buffer, ref bytePending, message);
                buffer.Advance(bytePending);

                return WriteFrameUnfragemntedPayload(buffer.WrittenSpan);
            }
        }

        const int frameTypeAndFlagsLength = 5;
        const int frameLengthLength = 4;
        const int frameIdsMaxLength = 5 * 2;
        const int frameMetadataMaxLength = frameLengthLength + frameTypeAndFlagsLength + frameIdsMaxLength;


        // <FRAME-LENGTH:4 bytes> <FRAME-TYPE:1 byte> <FLAGS:4 bytes> <STREAM-ID:varint> <FRAME-ID:varint> <FRAME-PAYLOAD:*>
        internal ValueTask<FlushResult> WriteFrameInternal(in ReadOnlySpan<byte> framePayload)
        {
            logger.LogDebug("snd frame {outgoingFrame}", _outgoingFrame);

            // small enough to be stackallocated
            Span<byte> metadataSpan = stackalloc byte[frameMetadataMaxLength];

            // first thing first, we need to know the frame length which depends on frame id and stream id
            Span<byte> idsSpan = metadataSpan.Slice(frameLengthLength + frameTypeAndFlagsLength, frameIdsMaxLength);
            Span<byte> currentSpan = idsSpan;

            // <STREAM-ID:varint>
            WriteVariableInteger(ref currentSpan, _outgoingFrame.StreamId);

            // <FRAME-ID:varint>
            WriteVariableInteger(ref currentSpan, _outgoingFrame.FrameId);

            var idsLength = idsSpan.Length - currentSpan.Length;
            idsSpan = idsSpan.Slice(0, idsLength);

            var frameLength = frameTypeAndFlagsLength + idsLength + framePayload.Length;
            var totalLength = frameLengthLength + frameLength;

            metadataSpan = metadataSpan.Slice(0, frameLengthLength + frameTypeAndFlagsLength + idsLength);

            currentSpan = metadataSpan;

            // <FRAME-LENGTH:4 bytes>
            WriteInteger(ref currentSpan, frameLength);
            // <FRAME-TYPE:1 byte>
            WriteByte(ref currentSpan, (byte)_outgoingFrame.Type);
            // <FLAGS:4 bytes>
            WriteInteger(ref currentSpan, (int)(_outgoingFrame.Flags));

            // oneshot
            return _frameWriter.WriteFrame(metadataSpan, framePayload);
        }

        internal ValueTask<FlushResult> WriteFrameUnfragemntedPayload(in ReadOnlySpan<byte> framePayload)
        {
            _outgoingFrame.Flags |= FrameFlags.Fin;
            return WriteFrameInternal(framePayload);
        }

        private async ValueTask<FlushResult> WriteFrameFragmentedPayload(ReadOnlyMemory<byte> framePayload, int maxFrameSize)
        {
            var current = framePayload;
            FlushResult result = default;

            while (!current.IsEmpty)
            {
                int remainingPayloadSize = Math.Min(current.Length, maxFrameSize - frameMetadataMaxLength);

                if (remainingPayloadSize == current.Length) _outgoingFrame.Flags |= FrameFlags.Fin;

                result = await WriteFrameInternal(current.Slice(0, remainingPayloadSize).Span);
                if (result.IsCanceled || result.IsCompleted) break;

                _outgoingFrame.Type = FrameType.Unset;

                current = current.Slice(remainingPayloadSize);
            }

            return result;
        }

        // LIST-OF-MESSAGES : [ <MESSAGE-NAME> <NB-ARGS:1 byte> <KV-LIST> ... ]
        // MESSAGE-NAME     : <STRING>
        private static void WriteListOfMessages(ArrayBufferWriter<byte> buffer, ref int bytePending, IEnumerable<SpopMessage> value)
        {
            foreach (var message in value)
            {
                WriteString(buffer, ref bytePending, message.Name);
                WriteByte(buffer, ref bytePending, (byte)message.Args.Count);
                WriteKeyValueList(buffer, ref bytePending, message.Args);
            }
        }

        // KV-LIST          : [ <KV-NAME> <KV-VALUE> ... ]
        // KV-NAME          : <STRING>
        // KV-VALUE         : <TYPED-DATA>
        public static void WriteKeyValueList(ArrayBufferWriter<byte> buffer, ref int bytePending, IDictionary<string, object> value)
        {
            foreach (var kvp in value)
            {
                WriteString(buffer, ref bytePending, kvp.Key);
                WriteTypedData(buffer, ref bytePending, kvp.Value);
            }
        }

        // LIST-OF-ACTIONS  : [ <ACTION-TYPE:1 byte> <NB-ARGS:1 byte> <ACTION-ARGS> ... ]
        // ACTION-ARGS      : [ <TYPED-DATA>... ] see each action type
        private static void WriteListOfActions(ArrayBufferWriter<byte> buffer, ref int bytePending, IEnumerable<SpopAction> actions)
        {
            foreach (var action in actions)
            {
                switch (action)
                {
                    case SetVarAction setVarAction:
                        WriteSetVariableAction(buffer, ref bytePending, setVarAction);
                        break;
                    case UnsetVarAction unsetVarAction:
                        WriteUnsetVariableAction(buffer, ref bytePending, unsetVarAction);
                        break;
                }
            }
        }

        // ACTION-SET-VAR  : <SET-VAR=1:1 byte><NB-ARGS=3:1 byte><VAR-SCOPE:1 byte><VAR-NAME><VAR-VALUE>
        private static void WriteSetVariableAction(ArrayBufferWriter<byte> buffer, ref int bytePending, SetVarAction value)
        {
            WriteByte(buffer, ref bytePending, (byte)1);
            WriteByte(buffer, ref bytePending, (byte)3);
            WriteByte(buffer, ref bytePending, (byte)value.Scope);
            WriteString(buffer, ref bytePending, value.Name);
            WriteTypedData(buffer, ref bytePending, value.Value);
        }

        // ACTION-UNSET-VAR  : <UNSET-VAR=2:1 byte><NB-ARGS=2:1 byte><VAR-SCOPE:1 byte><VAR-NAME>
        private static void WriteUnsetVariableAction(ArrayBufferWriter<byte> buffer, ref int bytePending, UnsetVarAction value)
        {
            WriteByte(buffer, ref bytePending, (byte)2);
            WriteByte(buffer, ref bytePending, (byte)2);
            WriteByte(buffer, ref bytePending, (byte)value.Scope);
            WriteString(buffer, ref bytePending, value.Name);
        }

        // TYPED-DATA    : <TYPE:4 bits><FLAGS:4 bits><DATA>
        public static void WriteTypedData(ArrayBufferWriter<byte> buffer, ref int bytePending, object value)
        {
            switch (value)
            {
                case null:
                    WriteByte(buffer, ref bytePending, 0x0);
                    break;
                case bool boolValue:
                    WriteByte(buffer, ref bytePending, (byte)(Unsafe.As<bool, byte>(ref boolValue) << 4 | (byte)DataType.Boolean));
                    break;
                case int intValue:
                    WriteByte(buffer, ref bytePending, (byte)DataType.Int32);
                    WriteInt32(buffer, ref bytePending, intValue);
                    break;
                case long longValue:
                    WriteByte(buffer, ref bytePending, (byte)DataType.Int64);
                    WriteInt64(buffer, ref bytePending, longValue);
                    break;
                case uint uintValue:
                    WriteByte(buffer, ref bytePending, (byte)DataType.Uint32);
                    WriteUint32(buffer, ref bytePending, uintValue);
                    break;
                case ulong ulongValue:
                    WriteByte(buffer, ref bytePending, (byte)DataType.Uint64);
                    WriteUint64(buffer, ref bytePending, ulongValue);
                    break;
                case IPAddress ipAddressValue when ipAddressValue.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork:
                    WriteByte(buffer, ref bytePending, (byte)DataType.Ipv4);
                    WriteIpv4(buffer, ref bytePending, ipAddressValue);
                    break;
                case IPAddress ipAddressValue when ipAddressValue.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6:
                    WriteByte(buffer, ref bytePending, (byte)DataType.Ipv6);
                    WriteIpv6(buffer, ref bytePending, ipAddressValue);
                    break;
                case string stringValue:
                    WriteByte(buffer, ref bytePending, (byte)DataType.String);
                    WriteString(buffer, ref bytePending, stringValue);
                    break;
                case byte[] binary:
                    WriteByte(buffer, ref bytePending, (byte)DataType.Binary);
                    WriteBinary(buffer, ref bytePending, binary);
                    break;
                default:
                    throw new ApplicationException(string.Format("Unable to write data type: {0}", value.GetType()));
            }
        }

        /// <summary>
        /// Write string to buffer without allocating
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="bytePending"></param>
        /// <param name="value"></param>
        private static void WriteString(ArrayBufferWriter<byte> buffer, ref int bytePending, string value)
        {
            if (value.Length > int.MaxValue - 5) throw new ArgumentException(nameof(value), "string too long");

            var requiredSize = checked(5 + value.Length);
            var memory = buffer.GetSpan(checked(bytePending + requiredSize));
            var span = memory.Slice(bytePending, requiredSize);
            WriteVariableInteger(ref span, value.Length);
            WriteString(ref span, value);

            bytePending += (requiredSize - span.Length);
        }

        private static void WriteString(ArrayBufferWriter<byte> buffer, ref int bytePending, in ReadOnlySpan<byte> value)
        {
            if (value.Length > int.MaxValue - 5) throw new ArgumentException(nameof(value), "string too long");

            var requiredSize = checked(5 + value.Length);
            var memory = buffer.GetSpan(checked(bytePending + requiredSize));
            var span = memory.Slice(bytePending, requiredSize);
            WriteVariableInteger(ref span, value.Length);
            value.CopyTo(span);
            span = span.Slice(value.Length);

            bytePending += (requiredSize - span.Length);
        }

        private static void WriteIpv4(ArrayBufferWriter<byte> buffer, ref int bytePending, IPAddress value)
        {
            var requiredSize = 4;
            var memory = buffer.GetSpan(checked(bytePending + requiredSize));
            var span = memory.Slice(bytePending, requiredSize);
            value.TryWriteBytes(span, out int _);

            bytePending += requiredSize;
        }

        private static void WriteIpv6(ArrayBufferWriter<byte> buffer, ref int bytePending, IPAddress value)
        {
            var requiredSize = 16;
            var memory = buffer.GetSpan(checked(bytePending + requiredSize));
            var span = memory.Slice(bytePending, requiredSize);
            value.TryWriteBytes(span, out int _);

            bytePending += requiredSize;
        }

        private static void WriteInt32(ArrayBufferWriter<byte> buffer, ref int bytePending, int value)
        {
            var requiredSize = 5;
            var memory = buffer.GetSpan(checked(bytePending + requiredSize));
            var span = memory.Slice(bytePending, requiredSize);
            WriteVariableInteger(ref span, value);

            bytePending += (requiredSize - span.Length);
        }

        private static void WriteUint32(ArrayBufferWriter<byte> buffer, ref int bytePending, uint value)
        {
            var requiredSize = 5;
            var memory = buffer.GetSpan(checked(bytePending + requiredSize));
            var span = memory.Slice(bytePending, requiredSize);
            WriteVariableInteger(ref span, value);

            bytePending += (requiredSize - span.Length);
        }

        private static void WriteInt64(ArrayBufferWriter<byte> buffer, ref int bytePending, long value)
        {
            var requiredSize = 10;
            var memory = buffer.GetSpan(checked(bytePending + requiredSize));
            var span = memory.Slice(bytePending, requiredSize);
            WriteVariableInteger(ref span, (long)value);

            bytePending += (requiredSize - span.Length);
        }

        private static void WriteUint64(ArrayBufferWriter<byte> buffer, ref int bytePending, ulong value)
        {
            var requiredSize = 10;
            var memory = buffer.GetSpan(checked(bytePending + requiredSize));
            var span = memory.Slice(bytePending, requiredSize);
            WriteVariableInteger(ref span, (long)value);

            bytePending += (requiredSize - span.Length);
        }

        private static void WriteBinary(ArrayBufferWriter<byte> buffer, ref int bytePending, byte[] value)
        {
            var requiredSize = checked(5 + value.Length);
            var memory = buffer.GetSpan(checked(bytePending + requiredSize));
            var span = memory.Slice(bytePending, requiredSize);
            WriteVariableInteger(ref span, value.Length);
            value.AsSpan().CopyTo(span);

            bytePending += (requiredSize - span.Length + value.Length);
        }

        private static void WriteByte(ArrayBufferWriter<byte> buffer, ref int bytePending, byte value)
        {
            var requiredSize = 1;
            var memory = buffer.GetSpan(checked(bytePending + requiredSize));
            var span = memory.Slice(bytePending, requiredSize);
            WriteByte(ref span, value);

            bytePending += requiredSize;
        }

        public static void WriteString(ref Span<byte> buffer, string value)
        {
            int written = Encoding.ASCII.GetBytes(value.AsSpan(), buffer);
            buffer = buffer.Slice(written);
        }

        public static void WriteByte(ref Span<byte> buffer, byte value)
        {
            buffer[0] = value;
            buffer = buffer.Slice(sizeof(byte));
        }

        private static void WriteInteger(ref Span<byte> buffer, int value)
        {
            BinaryPrimitives.WriteInt32BigEndian(buffer, value);
            buffer = buffer.Slice(sizeof(int));
        }

        public static void WriteVariableInteger(ref Span<byte> buffer, long value)
        {
            int index = 0;

            if (value < 240)
            {
                buffer[index++] = (byte)value;
            }
            else
            {

                buffer[index++] = (byte)(value | 240);
                value = (value - 240) >> 4;

                while (value >= 128)
                {
                    buffer[index++] = (byte)(value | 128);
                    value = (value - 128) >> 7;
                }

                buffer[index++] = (byte)value;
            }

            buffer = buffer.Slice(sizeof(byte) * index);
        }
    }
}
