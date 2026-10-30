using CK.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading.Tasks;

namespace CK.Sample.Backend;

/// <summary>
/// The Authentication Center of the sample: it holds the database, the login providers and the sessions. It has no HTTP
/// endpoint: the fronts reach it over the AppIdentity channel ("CK-AppIdentity" configuration section).
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
    public static async Task<WebApplication> BuildAsync( string[] args, Action<IConfigurationBuilder>? configure = null, string? contentRoot = null )
    {
        var builder = WebApplication.CreateSlimBuilder( new WebApplicationOptions { Args = args, ContentRootPath = contentRoot } );
        configure?.Invoke( builder.Configuration );
        builder.UseCKMonitoring();
        builder.AddApplicationIdentityServiceConfiguration();
        // The identity keys of the application are protected at rest.
        builder.Services.AddDataProtection();
        var monitor = builder.GetBuilderMonitor();
        var map = StObjContextRoot.Load( typeof( BackendHost ).Assembly, monitor )
                  ?? throw new InvalidOperationException( "Unable to load the generated code: build the Backend.Builder." );
        var app = builder.CKBuild( map );
        await DemoData.EnsureAsync( monitor, app.Services, app.Configuration );
        return app;
    }
}
