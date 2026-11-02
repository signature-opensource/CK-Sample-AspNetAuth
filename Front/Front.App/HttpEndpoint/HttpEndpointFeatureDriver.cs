using CK.AppIdentity;
using CK.AspNet;
using CK.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading.Tasks;

namespace CK.Sample.Front;

/// <summary>
/// The "HttpEndpoint" feature of the local party: it creates, starts and stops the web application of the host (the host
/// is a Generic Host, the web server is a feature of its party). Its configuration is the "HttpEndpoint" section of the
/// "Local" one: "Urls" (required).
/// <para>
/// FEASIBILITY CHECK (.wip/cris-auth/52-http-endpoint.md): local to the sample, the root local party only (no tenant
/// domain, no named endpoints).
/// </para>
/// </summary>
public sealed class HttpEndpointFeatureDriver : ApplicationIdentityFeatureDriver
{
    /// <summary>
    /// The configuration key of the feature, in the "Local" section.
    /// </summary>
    public const string ConfigurationKey = "HttpEndpoint";

    readonly HttpEndpointPipeline _pipeline;
    readonly IServiceProvider _services;
    WebApplication? _app;

    /// <summary>
    /// Initializes a new <see cref="HttpEndpointFeatureDriver"/>.
    /// </summary>
    /// <param name="s">The application identity service.</param>
    /// <param name="pipeline">The pipeline given by the host.</param>
    /// <param name="services">The services of the host.</param>
    public HttpEndpointFeatureDriver( ApplicationIdentityService s, HttpEndpointPipeline pipeline, IServiceProvider services )
        : base( s, isAllowedByDefault: false )
    {
        _pipeline = pipeline;
        _services = services;
    }

    /// <summary>
    /// Gets the web application, once started.
    /// </summary>
    public WebApplication? Application => _app;

    /// <inheritdoc />
    protected override async Task<bool> SetupAsync( FeatureLifetimeContext context )
    {
        if( !IsRootAllowed ) return true;
        var monitor = context.Monitor;
        var section = ApplicationIdentityService.LocalConfiguration.Configuration.TryGetSection( ConfigurationKey );
        var urls = section?["Urls"];
        if( string.IsNullOrWhiteSpace( urls ) )
        {
            monitor.Error( $"The \"Local:{ConfigurationKey}:Urls\" configuration is required." );
            return false;
        }
        var hostServices = _pipeline.HostServices;
        if( hostServices == null )
        {
            monitor.Error( $"The host didn't set the HostServices of its HttpEndpointPipeline." );
            return false;
        }
        var env = _services.GetRequiredService<IHostEnvironment>();
        var builder = WebApplication.CreateSlimBuilder( new WebApplicationOptions
        {
            ApplicationName = env.ApplicationName,
            EnvironmentName = env.EnvironmentName,
            ContentRootPath = env.ContentRootPath
        } );
        // The configuration is the host's one (with its overrides): the web application reads nothing by itself.
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddConfiguration( _services.GetRequiredService<IConfiguration>() );
        builder.WebHost.UseUrls( urls.Split( ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) );
        HostServiceForwarder.Forward( monitor, hostServices, _services, builder.Services );
        // What CKBuild (CK.AspNet) registers: the request's monitor is the one of its ScopedHttpContext.
        builder.Services.AddScoped<ScopedHttpContext>();
        builder.Services.AddScoped( sp => sp.GetRequiredService<ScopedHttpContext>().Monitor );
        builder.Services.AddScoped( sp => sp.GetRequiredService<IActivityMonitor>().ParallelLogger );
        _pipeline.ConfigureServices( builder.Services );
        var app = builder.Build();
        app.UseMiddleware<CKMiddleware>();
        _pipeline.Configure( app );
        await app.StartAsync().ConfigureAwait( false );
        _app = app;
        monitor.Info( $"The web application listens on {string.Join( ", ", app.Urls )}." );
        return true;
    }

    /// <inheritdoc />
    protected override Task<bool> SetupDynamicRemoteAsync( FeatureLifetimeContext context, IOwnedParty party ) => Task.FromResult( true );

    /// <inheritdoc />
    protected override Task TeardownDynamicRemoteAsync( FeatureLifetimeContext context, IOwnedParty party ) => Task.CompletedTask;

    /// <inheritdoc />
    protected override async Task TeardownAsync( FeatureLifetimeContext context )
    {
        if( _app != null )
        {
            // Stopped, not disposed: its container would dispose the singletons it forwards to the host (a container
            // disposes what its factories return).
            await _app.StopAsync().ConfigureAwait( false );
            _app = null;
        }
    }
}
