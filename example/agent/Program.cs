using System.Net;
using Agent;
using HAProxy.StreamProcessingOffload.AgentFramework;

var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions()
{
    Args = args
});

builder.WebHost
    .UseKestrelCore()
    .ConfigureLogging(logging =>
    {
        logging.AddConsole();
    })
    .ConfigureServices(services =>
    {
        services.Configure<SpoaFrameworkOptions>(options =>
        {
            options.EndPoint = new IPEndPoint(IPAddress.Any, 12345);
        });
        services.AddSingleton<ISpoaApplication, SpoaApplication>();
        services.AddSpoaFramework();
    });

var app = builder.Build();

app.Run();

