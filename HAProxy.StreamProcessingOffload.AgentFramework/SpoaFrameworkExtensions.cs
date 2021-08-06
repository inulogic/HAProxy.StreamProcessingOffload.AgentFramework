using HAProxy.StreamProcessingOffload.AgentFramework.Spoa;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Threading.Tasks;

namespace HAProxy.StreamProcessingOffload.AgentFramework
{
    public static class SpoaFrameworkExtensions
    {
        public static IServiceCollection AddSpoaFramework(this IServiceCollection services)
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<KestrelServerOptions>, SpoaFrameworkOptionsSetup>());


            return services;
        }
    }

    public class SpoaFrameworkOptionsSetup : IConfigureOptions<KestrelServerOptions>
    {
        private readonly SpoaFrameworkOptions _options;

        public SpoaFrameworkOptionsSetup(IOptions<SpoaFrameworkOptions> options)
        {
            _options = options.Value;
        }


        public void Configure(KestrelServerOptions options)
        {
            options.Listen(_options.EndPoint, builder =>
            {
                builder.UseConnectionHandler<SpoaFrameworkConnectionHandler>();
            });
        }
    }

    public class SpoaFrameworkConnectionHandler : ConnectionHandler
    {
        private readonly ILogger _logger;
        private readonly ISpoaApplication _spoaApplication;

        public SpoaFrameworkConnectionHandler(ILogger<SpoaFrameworkConnectionHandler> logger, ISpoaApplication spoaApplication)
        {
            _logger = logger;
            _spoaApplication = spoaApplication;
        }

        public override async Task OnConnectedAsync(ConnectionContext connection)
        {
            var stoppingToken = connection.Features.Get<IConnectionLifetimeNotificationFeature>().ConnectionClosedRequested;
            
            var spopConnection = new SpoaConnection(_logger, connection.Transport);
            await spopConnection.ProcessConnectionAsync(_spoaApplication, stoppingToken);
        }
    }

    public class SpoaFrameworkOptions
    {
        public IPEndPoint EndPoint { get; set; }
    }
}
