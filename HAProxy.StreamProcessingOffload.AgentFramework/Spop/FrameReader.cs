using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace HAProxy.StreamProcessingOffload.AgentFramework.Spop
{
    public static class FrameReader
    {
        /// <summary>
        /// Try read a spop frame from the given buffer. When an entire frame is readable from the buffer,
        /// frameMetadata and framePayload are valid.
        /// 
        /// Frame format:
        /// <FRAME-LENGTH:4 bytes> <FRAME-TYPE:1 byte> <FLAGS:4 bytes> <STREAM-ID:varint> <FRAME-ID:varint> <FRAME-PAYLOAD:*>
        /// </summary>
        /// <param name="readableBuffer"></param>
        /// <param name="frameMetadata">metadata of the frame</param>
        /// <param name="framePayload">Slice of the frame payload</param>
        /// <returns>true if it reads an entire frame</returns>
        public static bool TryReadFrame(ref ReadOnlySequence<byte> readableBuffer, SpopFrameMetadata frameMetadata, out ReadOnlySequence<byte> framePayload)
        {
            var reader = new SequenceReader<byte>(readableBuffer);
            bool result = reader.TryReadFrame(frameMetadata, out framePayload);

            readableBuffer = readableBuffer.Slice(reader.Position);

            return result;
        }

        /// <summary>
        /// Try read a spop frame from the given buffer. When an entire frame is readable from the buffer,
        /// frameMetadata and framePayload are valid.
        /// 
        /// Frame format:
        /// <FRAME-LENGTH:4 bytes> <FRAME-TYPE:1 byte> <FLAGS:4 bytes> <STREAM-ID:varint> <FRAME-ID:varint> <FRAME-PAYLOAD:*>
        /// </summary>
        /// <param name="readableBuffer"></param>
        /// <param name="frameMetadata">metadata of the frame</param>
        /// <param name="framePayload">Slice of the frame payload</param>
        /// <returns>true if it reads an entire frame</returns>
        public static bool TryReadFrame(ref this SequenceReader<byte> input, SpopFrameMetadata frameMetadata, out ReadOnlySequence<byte> framePayload)
        {
            framePayload = ReadOnlySequence<byte>.Empty;

            // <FRAME-LENGTH:4 bytes>
            if (!input.TryReadInteger(out int length)) return false;

            // Make sure the whole frame is buffered
            if (input.Length < length)
            {
                return false;
            }

            // adjust length of the sequence
            var frameInput = input.UnreadSequence.Slice(0, length);

            var frameReader = new SequenceReader<byte>(frameInput);

            // <FRAME-TYPE:1 byte>
            if (!frameReader.TryReadByte(out var type)) return false;

            // <FLAGS:4 bytes>
            if (!frameReader.TryReadInteger(out int flags)) return false;

            // <STREAM-ID:varint>
            if (!frameReader.TryReadVariableInteger(out long streamId)) return false;

            // <FRAME-ID:varint>
            if (!frameReader.TryReadVariableInteger(out long frameId)) return false;

            // <FRAME-PAYLOAD:*>

            frameMetadata.Type = (FrameType)type;
            frameMetadata.Flags = (FrameFlags)flags;
            frameMetadata.StreamId = streamId;
            frameMetadata.FrameId = frameId;

            // remaining currentBuffer
            framePayload = frameReader.UnreadSequence;

            // remaining buffer, not examined
            input.Advance(length);

            return true;
        }

        public static bool TryReadInteger(ref this SequenceReader<byte> reader, out int value)
        {
            return reader.TryReadBigEndian(out value);
        }

        public static bool TryReadByte(ref this SequenceReader<byte> reader, out byte value)
        {
            return reader.TryRead(out value);
        }

        public static bool TryReadVariableInteger(ref this SequenceReader<byte> reader, out long value)
        {
            byte nextValue;
            if (reader.TryRead(out nextValue))
            {
                value = nextValue;
                if (value < 240)
                {
                    return true;
                }
                else
                {
                    int shift = 4;
                    do
                    {
                        if (reader.TryRead(out nextValue))
                        {
                            value += nextValue << shift;
                            shift += 7;
                        }
                        else
                        {
                            return false;
                        }
                    } while (nextValue >= 128);

                    return true;
                }
            }
            else
            {
                value = default;
                return false;
            }
        }

        // <LENGTH:varint><ASCII CHAR:*>
        public static bool TryReadString(ref this SequenceReader<byte> reader, out string value)
        {
            value = default;
            if (!TryReadVariableInteger(ref reader, out var length)) return false;
            if (reader.Remaining < length) return false;

            value = Encoding.ASCII.GetString(reader.UnreadSequence.Slice(0, length));

            reader.Advance(length);

            return true;
        }

        // LIST-OF-MESSAGES : [ <MESSAGE-NAME> <NB-ARGS:1 byte> <KV-LIST> ... ]
        // MESSAGE-NAME     : <STRING>
        public static void DecodeListOfMessagesPayload(in ReadOnlySequence<byte> sequence, out IEnumerable<SpopMessage> value)
        {
            var reader = new SequenceReader<byte>(sequence);
            value = default;
            var result = new List<SpopMessage>();
            while (!reader.End)
            {
                if (!reader.TryReadString(out string messageName)) return;
                var message = new SpopMessage(messageName);

                if (!TryReadByte(ref reader, out var nbArgs)) return;

                for (byte i = 0; i < nbArgs; i++)
                {
                    if (!reader.TryReadString(out var key)) return;
                    if (!reader.TryReadTypeData(out object keyvalue)) return;

                    message.Args.Add(key, keyvalue);
                }

                result.Add(message);
            }

            value = result;
        }

        // LIST-OF-ACTIONS  : [ <ACTION-TYPE:1 byte> <NB-ARGS:1 byte> <ACTION-ARGS> ... ]
        // ACTION-ARGS      : [ <TYPED-DATA>... ] see each action type
        public static void DecodeListOfActionsPayload(in ReadOnlySequence<byte> sequence, out IEnumerable<SpopAction> value)
        {
            value = default;
            var reader = new SequenceReader<byte>(sequence);
            var result = new List<SpopAction>();
            while (!reader.End)
            {
                object actionValue = default;
                if (!reader.TryReadByte(out byte actionType)) return;
                if (!reader.TryReadByte(out byte nbArgs)) return;
                if (!reader.TryReadByte(out byte scope)) return;
                if (!reader.TryReadString(out var actionName)) return;
                if (nbArgs == 3)
                    if (!reader.TryReadTypeData(out actionValue)) return;

                SpopAction action = actionType == 0x1 ? new SetVarAction((VarScope)scope, actionName, actionValue) : new UnsetVarAction((VarScope)scope, actionName);

                result.Add(action);
            }

            value = result;
        }

        // KV-LIST          : [ <KV-NAME> <KV-VALUE> ... ]
        // KV-NAME          : <STRING>
        // KV-VALUE         : <TYPED-DATA>
        public static void DecodeKeyValueListPayload(in ReadOnlySequence<byte> sequence, IDictionary<string, object> value)
        {
            var reader = new SequenceReader<byte>(sequence);

            while (!reader.End)
            {
                if (!reader.TryReadString(out var key)) break;
                if (!reader.TryReadTypeData(out object keyvalue)) break;

                value.Add(key, keyvalue);
            }
        }

        // TYPED-DATA    : <TYPE:4 bits><FLAGS:4 bits><DATA>
        public static bool TryReadTypeData(ref this SequenceReader<byte> reader, out object value)
        {
            value = default;
            if (!reader.TryRead(out var dataTypeByte)) return false;
            var dataType = dataTypeByte & 0x0F;
            var flags = dataTypeByte >> 4;

            if ((DataType)dataType == DataType.Boolean)
            {
                value = (flags & 0x1) == 0x1;
                return true;
            }
            else
            {
                switch ((DataType)dataType)
                {
                    case DataType.Null: { return true; }
                    case DataType.Int32: if (reader.TryReadInt32(out int int32)) { value = int32; return true; } break;
                    case DataType.Int64: if (reader.TryReadInt64(out long int64)) { value = int64; return true; } break;
                    case DataType.Uint32: if (reader.TryReadUint32(out uint uint32)) { value = uint32; return true; } break;
                    case DataType.Uint64: if (reader.TryReadUint64(out ulong uint64)) { value = uint64; return true; } break;
                    case DataType.Ipv4: if (reader.TryReadIpv4(out IPAddress address)) { value = address; return true; } break;
                    case DataType.Ipv6: if (reader.TryReadIpv6(out IPAddress address6)) { value = address6; return true; } break;
                    case DataType.String: if (reader.TryReadString(out string @string)) { value = @string; return true; } break;
                    case DataType.Binary: if (reader.TryReadBinary(out byte[] binary)) { value = binary; return true; } break;
                    default:
                        throw new ApplicationException(string.Format("Unable to parse data type: {0}", dataType));
                }

                return false;
            }
        }

        public static bool TryReadIpv4(ref this SequenceReader<byte> reader, out IPAddress value)
        {
            value = default;
            Span<byte> stackBuffer = stackalloc byte[4];
            if (!reader.TryCopyTo(stackBuffer)) return false;
            reader.Advance(stackBuffer.Length);

            value = new IPAddress(stackBuffer);

            return true;
        }
        public static bool TryReadIpv6(ref this SequenceReader<byte> reader, out IPAddress value)
        {
            value = default;
            Span<byte> stackBuffer = stackalloc byte[16];
            if (!reader.TryCopyTo(stackBuffer)) return false;
            reader.Advance(stackBuffer.Length);

            value = new IPAddress(stackBuffer);

            return true;
        }

        public static bool TryReadInt64(ref this SequenceReader<byte> reader, out long value)
        {
            return reader.TryReadVariableInteger(out value);
        }

        public static bool TryReadInt32(ref this SequenceReader<byte> reader, out int value)
        {
            if (reader.TryReadVariableInteger(out long longValue))
            {
                value = (int)longValue;
                return true;
            }

            value = default;
            return false;
        }
        public static bool TryReadUint32(ref this SequenceReader<byte> reader, out uint value)
        {
            if (reader.TryReadVariableInteger(out long longValue))
            {
                value = (uint)longValue;
                return true;
            }

            value = default;
            return false;
        }
        public static bool TryReadUint64(ref this SequenceReader<byte> reader, out ulong value)
        {
            if (reader.TryReadVariableInteger(out long longValue))
            {
                value = (ulong)longValue;
                return true;
            }

            value = default;
            return false;
        }

        public static bool TryReadBinary(ref this SequenceReader<byte> reader, out byte[] value)
        {
            value = default;
            if (!reader.TryReadVariableInteger(out var length)) return false;

            return reader.TryReadBytes(length, out value);
        }


        private static bool TryReadBytes(ref this SequenceReader<byte> reader, long length, out byte[] value)
        {
            value = default;
            if (reader.Remaining < length) return false;

            // TODO : Should rent from a pool
            var result = new byte[length];
            Span<byte> span = new Span<byte>(result);

            if (!reader.TryCopyTo(span)) return false;

            reader.Advance(result.Length);

            return true;
        }
    }
}
