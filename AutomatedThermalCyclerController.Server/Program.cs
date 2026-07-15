using Microsoft.AspNetCore.Server.Kestrel.Core;
using AutomatedThermalCyclerController.Features.Services;
using Serilog;
using SiLA2.AspNetCore;
using SiLA2.Commands;
using SiLA2.Network.Discovery;
using SiLA2.Network.Discovery.mDNS;
using SiLA2.Server;
using SiLA2.Server.Services;
using SiLA2.Utils.Config;
using SiLA2.Utils.gRPC;
using SiLA2.Utils.Network;
using SiLA2.Utils.Security;
using SiLA2.Utils;
using Microsoft.Extensions.Options;

public partial class Program
{
    private static void Main(string[] args)
    {
        try
        {
            var builder = WebApplication.CreateBuilder(args);

            ConfigureLogSettings(builder.Host, builder.Configuration);
            ConfigureServices(builder.Services, builder.Configuration, builder.WebHost);

            var app = builder.Build();
            var silaServer = app.Services.GetRequiredService<ISiLA2Server>();
            app.InitializeSiLA2Features(silaServer);

            app.MapGrpcService<AutomatedThermalCyclerControllerService>();
            app.MapGrpcService<SiLAService>();

            silaServer.Start();
            app.Run();
        }
        catch (Exception ex)
        {
            Log.Error($"Unexpected error occurred: {ex.GetType().Name} {ex.Message}\n{ex.StackTrace}");
            throw;
        }
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration, ConfigureWebHostBuilder webHost)
    {
        services.AddGrpc(options =>
        {
            options.Interceptors.Add<SiLA2.Server.Interceptors.LoggingInterceptor>();
            options.Interceptors.Add<SiLA2.Server.Interceptors.MetadataValidationInterceptor>();
            options.Interceptors.Add<SiLA2.Server.Interceptors.ParameterValidationInterceptor>();
        });

        // add basic services for SiLA2
        services.AddSingleton<MetadataManager>();
        services.AddSingleton<IGrpcChannelProvider, GrpcChannelProvider>();
        services.AddSingleton(typeof(IObservableCommandManager<,>), typeof(ObservableCommandManager<,>));
        services.AddTransient<INetworkService, NetworkService>();
        services.AddSingleton<ServerInformation>();
        services.AddTransient<IServiceAnnouncer, ServiceAnnouncer>();
        services.AddSingleton<ISiLA2Server, SiLA2Server>();
        services.AddSingleton<ICertificateProvider, CertificateProvider>();
        services.AddSingleton<ICertificateContext, CertificateContext>();
        services.AddSingleton<ICertificateRepository, CertificateRepository>();
        services.AddSingleton((IConfigurationRoot)configuration);
        services.AddSingleton<IWritableOptions<ServerConfig>>(sp =>
        {
            return new WritableOptions<ServerConfig>(
                sp.GetRequiredService<IOptionsMonitor<ServerConfig>>(),
                sp.GetRequiredService<IConfigurationRoot>(),
                "ServerConfig",
                Path.Combine(AppContext.BaseDirectory, "appsettings.json")
                );
        });

        // add server information
        var sconf = configuration.GetSection("ServerConfig");
        var port = int.Parse(sconf["Port"] ?? string.Empty);
        ServerConfig serverConfig = new ServerConfig(sconf["Name"],
                                                     Guid.Parse(sconf["UUID"] ?? string.Empty),
                                                     sconf["FQHN"],
                                                     port,
                                                     sconf["NetworkInterface"],
                                                     sconf["DiscoveryServiceName"]);
        services.AddSingleton<IServerConfig>(serverConfig);
        var serverInfo = new ServerInformation(sconf["Type"],
                                               sconf["Description"],
                                               sconf["VendorURL"],
                                               sconf["Version"]);
        services.AddSingleton(serverInfo);
        var discoveryInfo = new ServiceDiscoveryInfo(serverConfig);
        services.AddSingleton(discoveryInfo);

        var enableTLS = bool.Parse(configuration["EnableTLS"] ?? string.Empty);
        webHost.ConfigureKestrel(options =>
        {
            if (enableTLS)
            {
                options.ConfigureEndpointDefaults(endpoints => { endpoints.Protocols = HttpProtocols.Http2; });
                options.ListenAnyIP(port, listenOptions =>
                {
                    var certProvider = services.BuildServiceProvider().GetRequiredService<ICertificateProvider>();
                    var cert = certProvider.GetServerCertificate(false);
                    listenOptions.UseHttps(cert);
                });
            }
            else
            {
                // allow communication without TLS only on localhost
                options.ListenLocalhost(port, loption =>
                {
                    loption.Protocols = HttpProtocols.Http2;
                });
            }
        });

        // add defined SiLA Feature
        services.AddSingleton<AutomatedThermalCyclerControllerService>();
    }

    private static void ConfigureLogSettings(ConfigureHostBuilder host, IConfiguration configuration)
    {
        Log.Logger = new LoggerConfiguration()
                    .ReadFrom
                    .Configuration(configuration)
                    .CreateLogger();

        // bridge Features logging to Serilog
        host.UseSerilog((context, services, configuration) =>
            configuration.ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services))
            .ConfigureServices((context, services) =>
            {
                services.AddGrpc();
                services.AddTransient<AutomatedThermalCyclerControllerService>();
            });
    }
}