namespace HAProxy.StreamProcessingOffload.AgentFramework;
using System.Net;
using System.Threading.Tasks;
using HAProxy.StreamProcessingOffload.AgentFramework.Spoa;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public static class SpoaFrameworkExtensions
{
    public static IServiceCollection AddSpoaFramework(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<KestrelServerOptions>, SpoaFrameworkOptionsSetup>());

        return services;
    }
}

public class SpoaFrameworkOptionsSetup(IOptions<SpoaFrameworkOptions> options) : IConfigureOptions<KestrelServerOptions>
{
    internal readonly SpoaFrameworkOptions options = options.Value;

    public void Configure(KestrelServerOptions options) => options.Listen(this.options.EndPoint, builder => builder.UseConnectionHandler<SpoaFrameworkConnectionHandler>());
}

public class SpoaFrameworkConnectionHandler(ILogger<SpoaFrameworkConnectionHandler> logger, ISpoaApplication spoaApplication) : ConnectionHandler
{
    internal readonly ILogger logger = logger;
    internal readonly ISpoaApplication spoaApplication = spoaApplication;

    public override async Task OnConnectedAsync(ConnectionContext connection)
    {
        var stoppingToken = connection.Features.Get<IConnectionLifetimeNotificationFeature>().ConnectionClosedRequested;

        var spopConnection = new SpoaConnection(this.logger, connection.Transport);
        await spopConnection.ProcessConnectionAsync(this.spoaApplication, stoppingToken);
    }
}

public class SpoaFrameworkOptions
{
    public IPEndPoint EndPoint { get; set; }
}
