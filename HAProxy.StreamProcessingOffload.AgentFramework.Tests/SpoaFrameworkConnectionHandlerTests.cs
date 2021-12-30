namespace HAProxy.StreamProcessingOffload.AgentFramework.Tests;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using HAProxy.StreamProcessingOffload.AgentFramework.Spop;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class SpoaFrameworkConnectionHandlerTests
{
    [Fact]
    public async Task EngineCanConnectAndClose()
    {
        // https://github.com/dotnet/aspnetcore/blob/52eff90fbcfca39b7eb58baad597df6a99a542b0/src/SignalR/server/SignalR/test/HubConnectionHandlerTestUtils/Utils.cs
        var handler = new SpoaFrameworkConnectionHandler(NullLogger<SpoaFrameworkConnectionHandler>.Instance, new TestSpoaApplication());

        using (var engine = new TestEngine())
        {
            var connectionHandlerTask = handler.OnConnectedAsync(engine.Connection);

            // kill the connection
            engine.Dispose();

            await connectionHandlerTask;
        }
    }

    [Fact]
    public async Task EngineCanHandshake()
    {
        var handler = new SpoaFrameworkConnectionHandler(NullLogger<SpoaFrameworkConnectionHandler>.Instance, new TestSpoaApplication());

        using (var engine = new TestEngine())
        {
            var connectionHandlerTask = handler.OnConnectedAsync(engine.Connection);

            await engine.SendHello();
            var (frame, _) = await engine.ReadOneFrameAsync();

            Assert.Equal(FrameType.AgentHello, frame.Type);

            // kill the connection
            engine.Dispose();

            await connectionHandlerTask;
        }
    }

    [Fact]
    public async Task EngineCanDisconnect()
    {
        var handler = new SpoaFrameworkConnectionHandler(NullLogger<SpoaFrameworkConnectionHandler>.Instance, new TestSpoaApplication());

        using (var engine = new TestEngine())
        {
            var connectionHandlerTask = handler.OnConnectedAsync(engine.Connection);

            await engine.SendHello();
            await engine.ReadOneFrameAsync();

            await engine.SendDisconnect();
            var (frame, _) = await engine.ReadOneFrameAsync();

            Assert.Equal(FrameType.AgentDisconnect, frame.Type);

            await connectionHandlerTask;

            Assert.False(engine.Connection.Application.Input.TryRead(out var _));
        }
    }

    [Fact]
    public async Task AgentCanDisconnect()
    {
        var handler = new SpoaFrameworkConnectionHandler(NullLogger<SpoaFrameworkConnectionHandler>.Instance, new TestSpoaApplication());

        using (var engine = new TestEngine())
        {
            var connectionHandlerTask = handler.OnConnectedAsync(engine.Connection);

            await engine.SendHello();
            await engine.ReadOneFrameAsync();

            engine.RequestClose();
            var (frame, _) = await engine.ReadOneFrameAsync();

            Assert.Equal(FrameType.AgentDisconnect, frame.Type);

            await connectionHandlerTask;

            Assert.False(engine.Connection.Application.Input.TryRead(out var _));
        }
    }

    [Fact]
    public async Task WhenPipeFaultingAgentShouldDisconnect()
    {
        var handler = new SpoaFrameworkConnectionHandler(NullLogger<SpoaFrameworkConnectionHandler>.Instance, new TestSpoaApplication());

        using (var engine = new TestEngine())
        {
            var connectionHandlerTask = handler.OnConnectedAsync(engine.Connection);

            await engine.Connection.Application.Output.CompleteAsync(new IOException("test"));

            var status = new Dictionary<string, object>();
            var (frame, payload) = await engine.ReadOneFrameAsync();

            FrameReader.DecodeKeyValueListPayload(new ReadOnlySequence<byte>(payload), status);

            Assert.Equal(FrameType.AgentDisconnect, frame.Type);
            Assert.Equal(1, status["status-code"]);
            Assert.Equal("test", status["message"]);

            await connectionHandlerTask;
        }
    }

    [Fact]
    public async Task WhenUnsupportedVersionAgentShouldDisconnect()
    {
        var handler = new SpoaFrameworkConnectionHandler(NullLogger<SpoaFrameworkConnectionHandler>.Instance, new TestSpoaApplication());

        using (var engine = new TestEngine()
        {
            PeerSettings =
                {
                    SupportedSpopVersion = "1.0"
                }
        })
        {
            var connectionHandlerTask = handler.OnConnectedAsync(engine.Connection);

            await engine.Connection.Application.Output.CompleteAsync(new Exception("test"));

            var (frame, _) = await engine.ReadOneFrameAsync();
            Assert.Equal(FrameType.AgentDisconnect, frame.Type);

            await connectionHandlerTask;
        }
    }

    private static readonly SpopMessage Message = new("test")
    {
        Args =
                    {
                        { "null", null },
                        { "bool", true },
                        { "int", 2290 },
                        { "long", (long)2290 },
                        { "uint", (uint)2290 },
                        { "ulong", (ulong)2290 },
                        { "ipv4", IPAddress.Loopback },
                        { "ipv6", IPAddress.IPv6Loopback },
                        { "string", "string" },
                        { "binary", new byte[] { 0x1, 0x2 } }
                    }
    };
    private static readonly IEnumerable<SpopAction> ListOfActions = new List<SpopAction>
        {
            new SetVarAction(VarScope.Process, "null", null),
            new SetVarAction(VarScope.Process, "bool", true),
            new SetVarAction(VarScope.Process, "int", int.MinValue),
            new SetVarAction(VarScope.Process, "long", long.MinValue),
            new SetVarAction(VarScope.Process, "uint", uint.MinValue),
            new SetVarAction(VarScope.Process, "ulong", ulong.MinValue),
            new SetVarAction(VarScope.Process, "ipv4", IPAddress.Loopback),
            new SetVarAction(VarScope.Process, "ipv6", IPAddress.IPv6Loopback),
            new SetVarAction(VarScope.Process, "string", "string"),
            new SetVarAction(VarScope.Process, "binary", new byte[] { 0x1, 0x2 })
        };
    private readonly TestSpoaApplication spoaApplication = new()
    {
        AppDelegate = (streamId, messages) =>
        {
            Assert.Equal(42, streamId);
            Assert.NotNull(messages);
            Assert.NotEmpty(messages);
            //Assert.Same(_message, messages.Single());

            return Task.FromResult(ListOfActions);
        }
    };


    [Fact]
    public async Task WhenEngineSendsNotifyAgentShouldRunSpoaApplicationAndWriteAgentAck()
    {
        var handler = new SpoaFrameworkConnectionHandler(NullLogger<SpoaFrameworkConnectionHandler>.Instance, this.spoaApplication);

        using (var engine = new TestEngine())
        {
            var connectionHandlerTask = handler.OnConnectedAsync(engine.Connection);

            await engine.SendHello();
            await engine.SendNotify(42, 1, new List<SpopMessage> { Message });

            await engine.ReadOneFrameAsync();

            var (frame, payload) = await engine.ReadOneFrameAsync();
            FrameReader.DecodeListOfActionsPayload(new ReadOnlySequence<byte>(payload), out var actions);

            Assert.Equal(FrameType.AgentAck, frame.Type);
            Assert.Equal(Frame.Fin, frame.Flags);
            Assert.Equal(42, frame.StreamId);
            Assert.Equal(1, frame.FrameId);

            Assert.NotEmpty(actions);

            // kill the connection
            engine.Dispose();

            await connectionHandlerTask;
        }
    }

    [Fact]
    public async Task WhenEngineSendsLastUnsetFrameAgentShouldRunSpoaApplicationAndWriteAgentAck()
    {
        var handler = new SpoaFrameworkConnectionHandler(NullLogger<SpoaFrameworkConnectionHandler>.Instance, this.spoaApplication);

        using (var engine = new TestEngine()
        {
            PeerSettings =
                {
                    FrameSize = 100, // test payload is 112 byte long
                    FragmentationCapabilities = {
                        CanWrite = true
                    }
                }
        })
        {
            var connectionHandlerTask = handler.OnConnectedAsync(engine.Connection);

            await engine.SendHello();
            await engine.SendNotify(42, 1, new List<SpopMessage> { Message });

            await engine.ReadOneFrameAsync();

            var (frame, payload) = await engine.ReadOneFrameAsync();
            FrameReader.DecodeListOfActionsPayload(new ReadOnlySequence<byte>(payload), out var actions);

            Assert.Equal(FrameType.AgentAck, frame.Type);
            Assert.Equal(Frame.Fin, frame.Flags);
            Assert.Equal(42, frame.StreamId);
            Assert.Equal(1, frame.FrameId);
            Assert.NotEmpty(actions);

            // kill the connection
            engine.Dispose();

            await connectionHandlerTask;
        }
    }

    [Fact]
    public async Task WhenEngineSupportsFragmentationAgentShouldFragmentAgentAck()
    {
        var handler = new SpoaFrameworkConnectionHandler(NullLogger<SpoaFrameworkConnectionHandler>.Instance, this.spoaApplication);

        using (var engine = new TestEngine()
        {
            PeerSettings =
                {
                    FrameSize = 100, // test payload is 112 byte long
                    FragmentationCapabilities = {
                        CanRead = true
                    }
                }
        })
        {
            var connectionHandlerTask = handler.OnConnectedAsync(engine.Connection);

            await engine.SendHello();
            await engine.SendNotify(42, 1, new List<SpopMessage> { Message });

            await engine.ReadOneFrameAsync();

            var (frame, fragment1) = await engine.ReadOneFrameAsync();
            var (frameFin, fragment2) = await engine.ReadOneFrameAsync();

            FrameReader.DecodeListOfActionsPayload(new ReadOnlySequence<byte>(Combine(fragment1, fragment2)), out var actions);

            Assert.Equal(FrameType.AgentAck, frame.Type);
            Assert.Equal(Frame.None, frame.Flags);
            Assert.Equal(42, frame.StreamId);
            Assert.Equal(1, frame.FrameId);
            Assert.Equal(FrameType.Unset, frameFin.Type);
            Assert.Equal(Frame.Fin, frameFin.Flags);
            Assert.Equal(42, frameFin.StreamId);
            Assert.Equal(1, frameFin.FrameId);
            Assert.NotEmpty(actions);

            // kill the connection
            engine.Dispose();

            await connectionHandlerTask;
        }
    }

    private static byte[] Combine(byte[] fragment1, byte[] fragment2)
    {
        var payload = new byte[fragment1.Length + fragment2.Length];
        fragment1.CopyTo(payload, 0);
        fragment2.CopyTo(payload, fragment1.Length);

        return payload;
    }

    public class TestSpoaApplication : ISpoaApplication
    {
        public Func<long, IEnumerable<SpopMessage>, Task<IEnumerable<SpopAction>>> AppDelegate { get; set; } = (streamId, messages)
            => Task.FromResult(Enumerable.Empty<SpopAction>());

        public Task<IEnumerable<SpopAction>> ProcessMessagesAsync(long streamId, IEnumerable<SpopMessage> messages) => this.AppDelegate(streamId, messages);
    }
}
