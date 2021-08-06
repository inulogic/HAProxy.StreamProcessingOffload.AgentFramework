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
                    logging.ClearProviders();
                    logging.AddSimpleConsole(options =>
                    {
                        options.IncludeScopes = true;
                        options.SingleLine = true;
                        options.TimestampFormat = "hh:mm:ss ";
                    });
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    // using https://github.com/davidfowl/BedrockFramework would remove dependency on webhost
                    webBuilder
                        .ConfigureServices(services =>
                        {
                            services.Configure<SpoaFrameworkOptions>(options =>
                            {
                                options.EndPoint = new IPEndPoint(IPAddress.Any, 12345);
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
}
