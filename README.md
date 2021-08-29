# HAProxy Stream Processing Offload Agent Framework

Provides a [Stream Processing Offload Protocol](https://github.com/haproxy/wiki/wiki/SPOE:-Stream-Processing-Offloading-Engine) framework for ASP.NET Core or [BedrockFramework](HAProxy.StreamProcessingOffload.AgentFramework).

It is fully asynchronuous and make heavy use of [I/O Pipeline](https://docs.microsoft.com/en-us/dotnet/standard/io/pipelines) and [Buffer](https://docs.microsoft.com/en-us/dotnet/standard/io/buffers) to do high-performance I/O and protocol parsing.

Supports :
- spop version 2.0
- fragmentation in both ways. Meaning it can read fragmented frame from HAProxy and is ready to write them when HAProxy will support it
- pipelining

While it works fine, some part still need improvement. Contributions are welcome to improve :
- end-user experience and configuration
- add spop frame async capability (use an arbitrary connection to send agent ack frames)

## Run example

```sh
cd example
docker-compose up -d
curl -v http://localhost:8000
```

should show Ip_score response header

```http
HTTP/1.1 GET /

Host: localhost:8000
Ip_score: 10
User-Agent: curl/7.68.0
Accept: */*
```

You may want to test bedrock flavor with `docker compose -f docker-compose.yml -f docker-compose.bedrock.yml up -d` or compare performance with the c sample with `docker compose -f docker-compose.yml -f docker-compose.spoa-example.yml up -d`.

## Usage

1. Add a SpoaApplication class to handle messages sent by HAProxy and return actions

    ```C#
    public class SpoaApplication : ISpoaApplication
    {
        public Task<IEnumerable<SpopAction>> ProcessMessagesAsync(long streamId, IEnumerable<SpopMessage> messages)
        {
            var responseActions = new List<SpopAction>();

            foreach (var myMessage in messages)
            {
                if (myMessage.Name == "my-message-name")
                {
                    int ip_score = 10;

                    if (IPAddress.IsLoopback((IPAddress)myMessage.Args["ip"])) ip_score = 20;

                    SpopAction setVar = new SetVarAction(VarScope.Request, "ip_score", ip_score);
                    responseActions.Add(setVar);
                }
            }

            return Task.FromResult((IEnumerable<SpopAction>)responseActions);
        }
    }
    ```

2. In `Program.cs`, Add Spoa Framework to a IWebHostBuilder.

    ```C#
    class Program
    {
        public static async Task Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder
                        .ConfigureServices(services =>
                        {
                            services.Configure<SpoaFrameworkOptions>(options =>
                            {
                                options.EndPoint = new IPEndPoint(IPAddress.Loopback, 12345);
                            });
                            services.AddSingleton<ISpoaApplication, SpoaApplication>();
                            services.AddSpoaFramework();
                        })
                        .UseStartup<Dummy>();
                });
    }

    internal class Dummy
    {
        public void Configure(IApplicationBuilder app)
        {
        }
    }
    ```

    Use your own Startup class if the Host is also serving Web content.

3. See examples/haproxy to configure haproxy