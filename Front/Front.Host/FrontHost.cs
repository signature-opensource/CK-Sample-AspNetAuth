using CK.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using System;
using System.IO;

namespace CK.Sample.Front;

/// <summary>
/// The front of the sample: it serves the Angular application and its "/.cris" endpoints on the same origin. It has no
/// database and no login provider: its AuthCenter is the remote "$Backend" party ("CK-AppIdentity" configuration
/// section), to which it relays the session commands.
/// </summary>
public static class FrontHost
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
        builder.Services.AddCrisAuthentication();
        builder.Services.AddHttpForwarder();
        var monitor = builder.GetBuilderMonitor();
        var map = StObjContextRoot.Load( typeof( FrontHost ).Assembly, monitor )
                  ?? throw new InvalidOperationException( "Unable to load the generated code: build the Front.Builder." );
        var app = builder.CKBuild( map );
        app.UseCrisAuthentication();
        app.UseCris();
        // The two Angular applications (Front.Builder/CKSetup.xml) share this origin: the MiniApp under "/mini/" (its
        // base href), the App everywhere else.
        MapFront( app, monitor, "MiniApp", "/mini" );
        MapFront( app, monitor, "App", "" );
        return app;
    }

    // Feasibility check: serves the Angular application "Clients/{name}" under the requestPath ("" for the root).
    // In Development, when "Spa:{name}:DevServer" is configured, its dev server ("yarn start") serves it through this
    // host, on its origin (the authentication requires the same origin as in production).
    static void MapFront( WebApplication app, IActivityMonitor monitor, string name, string requestPath )
    {
        var devServer = app.Environment.IsDevelopment() ? app.Configuration[$"Spa:{name}:DevServer"] : null;
        var spa = Path.Combine( app.Environment.ContentRootPath, $"Clients/{name}/dist/{name}/browser" );
        if( !string.IsNullOrEmpty( devServer ) )
        {
            monitor.Info( $"The Angular application '{name}' is served by its dev server '{devServer}'." );
            app.MapForwarder( $"{requestPath}/{{**catch-all}}", devServer ).Add( b => ((RouteEndpointBuilder)b).Order = int.MaxValue );
        }
        else if( Directory.Exists( spa ) )
        {
            var files = new PhysicalFileProvider( spa );
            app.UseDefaultFiles( new DefaultFilesOptions { FileProvider = files, RequestPath = requestPath } );
            app.UseStaticFiles( new StaticFileOptions { FileProvider = files, RequestPath = requestPath } );
            // The fallback rewrites the request path to the file path: it must be under the RequestPath.
            app.MapFallbackToFile( $"{requestPath}/{{*path:nonfile}}",
                                   $"{requestPath.TrimStart( '/' )}/index.html".TrimStart( '/' ),
                                   new StaticFileOptions { FileProvider = files, RequestPath = requestPath } );
        }
        else
        {
            monitor.Warn( $"The Angular application '{name}' is not built ('{spa}' not found)." );
        }
    }
}
