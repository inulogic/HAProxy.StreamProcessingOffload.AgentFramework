using HAProxy.StreamProcessingOffload.AgentFramework.Spop;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace HAProxy.StreamProcessingOffload.AgentFramework.Tests
{
    // https://github.com/dotnet/aspnetcore/blob/10beca5a9847902daad02198167d27964ac94cb2/src/SignalR/common/testassets/Tests.Utils/TestClient.cs
    public class TestEngine : IConnectionLifetimeNotificationFeature, IDisposable
    {
        private readonly CancellationTokenSource _cts;
        public DefaultConnectionContext Connection { get; }
        public CancellationToken ConnectionClosedRequested { get; set; }

        private readonly SpopFrameProducer _frameProducer;

        public SpopPeerSettings PeerSettings { get; } = new SpopPeerSettings()
        {
            FrameSize = 256,
            HasPipeliningCapabilities = true,
            SupportedSpopVersion = "2.0"
        };

        public TestEngine()
        {
            var options = new PipeOptions(readerScheduler: PipeScheduler.Inline, writerScheduler: PipeScheduler.Inline, useSynchronizationContext: false);
            var pair = DuplexPipe.CreateConnectionPair(options, options);
            Connection = new DefaultConnectionContext(Guid.NewGuid().ToString(), pair.Transport, pair.Application);

            Connection.Features.Set<IConnectionLifetimeNotificationFeature>(this);

            _frameProducer = new SpopFrameProducer(NullLogger.Instance, new SpopFrameWriter(NullLogger.Instance, pair.Application.Output));

            _cts = new CancellationTokenSource();
            ConnectionClosedRequested = _cts.Token;
        }

        public void Dispose()
        {
            Connection.Application.Output.Complete();
        }

        public Task SendHello()
        {
            return _frameProducer.WriteEngineHelloAsync(PeerSettings).AsTask();
        }

        internal Task SendNotify(long streamId, long frameId, IEnumerable<SpopMessage> messages)
        {
            return _frameProducer.WriteEngineNotifyAsync(streamId, frameId, messages, PeerSettings).AsTask();
        }

        internal async Task SendDisconnect()
        {
            await _frameProducer.WriteEngineDisconnectAsync();
        }

        public void RequestClose()
        {
            _cts.Cancel();
        }

        internal async Task<(SpopFrameMetadata, byte[])> ReadOneFrameAsync()
        {
            var result = await Connection.Application.Input.ReadAsync();

            if (result.IsCanceled) throw new Exception();

            if (result.Buffer.IsEmpty || result.IsCompleted) return (null, default);

            var buffer = result.Buffer;

            SpopFrameMetadata metadata = new SpopFrameMetadata();

            FrameReader.TryReadFrame(ref buffer, metadata, out var framePayload);
            var payload = framePayload.ToArray();

            Connection.Application.Input.AdvanceTo(buffer.Start);

            return (metadata, payload);
        }
    }
}