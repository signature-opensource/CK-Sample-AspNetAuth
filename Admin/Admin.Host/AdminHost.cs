using CK.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using System;
using System.IO;

namespace CK.Sample.Admin;

/// <summary>
/// The back-office of the sample: it serves the Angular application of the user management and its "/.cris" endpoints on
/// the same origin. Like the front, it has no database and no login provider: its AuthCenter is the remote "$Backend"
/// party ("CK-AppIdentity" configuration section), to which it relays the session commands and dispatches the
/// administration commands.
/// </summary>
public static class AdminHost
{
    /// <summary>
    /// Builds the application.
    /// </summary>
    /// <param name="args">The command line arguments.</param>
    /// <param name="configure">Optional configuration overrides (the tests use them).</param>
    /// <param name="contentRoot">Optional content root (where the appsettings.json and the Clients are). Defaults to the current directory.</param>
    /// <returns>The application, not started.</returns>
    public static WebApplication Build( string[] args, Action<IConfigurationBuilder>? configure = null, string? contentRoot = null )
    {
        var builder = WebApplication.CreateSlimBuilder( new WebApplicationOptions { Args = args, ContentRootPath = contentRoot } );
        configure?.Invoke( builder.Configuration );
        builder.UseCKMonitoring();
        builder.AddApplicationIdentityServiceConfiguration();
        // The identity keys of the application are protected at rest.
        builder.Services.AddDataProtection();
        builder.Services.AddCrisAuthentication();
        var monitor = builder.GetBuilderMonitor();
        var map = StObjContextRoot.Load( typeof( AdminHost ).Assembly, monitor )
                  ?? throw new InvalidOperationException( "Unable to load the generated code: build the Admin.Builder." );
        var app = builder.CKBuild( map );
        app.UseCrisAuthentication();
        app.UseCris();
        // The Angular application, when it has been built (see the README).
        var spa = Path.Combine( app.Environment.ContentRootPath, app.Configuration["Spa:Path"] ?? "Clients/App/dist/App/browser" );
        if( Directory.Exists( spa ) )
        {
            var files = new PhysicalFileProvider( spa );
            app.UseDefaultFiles( new DefaultFilesOptions { FileProvider = files } );
            app.UseStaticFiles( new StaticFileOptions { FileProvider = files } );
            app.MapFallbackToFile( "index.html", new StaticFileOptions { FileProvider = files } );
        }
        else
        {
            monitor.Warn( $"The Angular application is not built ('{spa}' not found): only the '/.cris' endpoints are served." );
        }
        return app;
    }
}
