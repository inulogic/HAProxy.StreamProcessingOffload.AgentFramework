namespace HAProxy.StreamProcessingOffload.AgentFramework.Tests;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using HAProxy.StreamProcessingOffload.AgentFramework.Spop;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.Extensions.Logging.Abstractions;

// https://github.com/dotnet/aspnetcore/blob/10beca5a9847902daad02198167d27964ac94cb2/src/SignalR/common/testassets/Tests.Utils/TestClient.cs
public class TestEngine : IConnectionLifetimeNotificationFeature, IDisposable
{
    private readonly CancellationTokenSource cts;
    public DefaultConnectionContext Connection { get; }
    public CancellationToken ConnectionClosedRequested { get; set; }

    private readonly SpopFrameProducer frameProducer;

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
        this.Connection = new DefaultConnectionContext(Guid.NewGuid().ToString(), pair.Transport, pair.Application);

        this.Connection.Features.Set<IConnectionLifetimeNotificationFeature>(this);

        this.frameProducer = new SpopFrameProducer(NullLogger.Instance, new SpopFrameWriter(NullLogger.Instance, pair.Application.Output));

        this.cts = new CancellationTokenSource();
        this.ConnectionClosedRequested = this.cts.Token;
    }

    public void Dispose() => this.Connection.Application.Output.Complete();

    public Task SendHello(bool isHealthCheck = false) => this.frameProducer.WriteEngineHelloAsync(this.PeerSettings, isHealthCheck).AsTask();

    internal Task SendNotify(long streamId, long frameId, IEnumerable<SpopMessage> messages) => this.frameProducer.WriteEngineNotifyAsync(streamId, frameId, messages, this.PeerSettings).AsTask();

    internal async Task SendDisconnect() => await this.frameProducer.WriteEngineDisconnectAsync();

    public void RequestClose() => this.cts.Cancel();

    internal async Task<(SpopFrameMetadata, byte[])> ReadOneFrameAsync()
    {
        var result = await this.Connection.Application.Input.ReadAsync();

        if (result.IsCanceled)
        {
            throw new Exception();
        }

        if (result.Buffer.IsEmpty || result.IsCompleted)
        {
            return (null, default);
        }

        var buffer = result.Buffer;

        var metadata = new SpopFrameMetadata();

        FrameReader.TryReadFrame(ref buffer, metadata, out var framePayload);
        var payload = framePayload.ToArray();

        this.Connection.Application.Input.AdvanceTo(buffer.Start);

        return (metadata, payload);
    }
}
