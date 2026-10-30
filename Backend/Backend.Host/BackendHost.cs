using CK.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading.Tasks;

namespace CK.Sample.Backend;

/// <summary>
/// The Authentication Center of the sample: it holds the database, the login providers and the sessions. It is not a web
/// application: the fronts reach it over the AppIdentity channel ("CK-AppIdentity" configuration section).
/// </summary>
public static class BackendHost
{
    /// <summary>
    /// Builds the application and ensures the demo data.
    /// </summary>
    /// <param name="args">The command line arguments.</param>
    /// <param name="configure">Optional configuration overrides (the tests use them).</param>
    /// <param name="contentRoot">Optional content root (where the appsettings.json is). Defaults to the current directory.</param>
    /// <returns>The application, not started.</returns>
    public static async Task<IHost> BuildAsync( string[] args, Action<IConfigurationBuilder>? configure = null, string? contentRoot = null )
    {
        var builder = Host.CreateApplicationBuilder( new HostApplicationBuilderSettings { Args = args, ContentRootPath = contentRoot } );
        configure?.Invoke( builder.Configuration );
        builder.UseCKMonitoring();
        builder.AddApplicationIdentityServiceConfiguration();
        // The identity keys of the application are protected at rest.
        builder.Services.AddDataProtection();
        // The generic host registers no monitor (the web hosts have the one of their ScopedHttpContext).
        builder.Services.AddScoped<IActivityMonitor, ActivityMonitor>();
        builder.Services.AddScoped( sp => sp.GetRequiredService<IActivityMonitor>().ParallelLogger );
        var monitor = builder.GetBuilderMonitor();
        var map = StObjContextRoot.Load( typeof( BackendHost ).Assembly, monitor )
                  ?? throw new InvalidOperationException( "Unable to load the generated code: build the Backend.Builder." );
        builder.Services.AddStObjMap( monitor, map );
        var app = builder.CKBuild();
        await DemoData.EnsureAsync( monitor, app.Services, builder.Configuration );
        return app;
    }
}
