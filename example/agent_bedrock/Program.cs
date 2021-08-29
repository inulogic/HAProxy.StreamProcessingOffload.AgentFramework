using Bedrock.Framework;
using HAProxy.StreamProcessingOffload.AgentFramework;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;

namespace Agent
{
    class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureLogging(logging =>
                {
                    logging.SetMinimumLevel(LogLevel.Debug);
                    logging.ClearProviders();
                    logging.AddSimpleConsole(options =>
                    {
                        options.IncludeScopes = true;
                        options.SingleLine = true;
                        options.TimestampFormat = "hh:mm:ss ";
                    });
                })
                .ConfigureServices(services => {
                    services.AddSingleton<ISpoaApplication, SpoaApplication>();
                })
                .ConfigureServer(builder => 
                {
                    builder.UseSockets(sockets => 
                    {
                        sockets.Listen(IPAddress.Any, 12345, builder => builder.UseConnectionLogging().UseConnectionHandler<SpoaFrameworkConnectionHandler>());
                    });
                });
    }
}
