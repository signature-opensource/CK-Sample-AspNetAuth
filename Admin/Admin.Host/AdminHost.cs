using CK.Core;
using CK.TypeScript;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;

namespace CK.Sample.Admin;

/// <summary>
/// The back-office of the sample: it serves the client application of the user management and its "/.cris" endpoints on
/// the same origin. Like the front, it has no database and no login provider: its AuthCenter is the remote "$Backend"
/// party ("CK-AppIdentity" configuration section), to which it relays the session commands and dispatches the
/// administration commands.
/// <para>
/// The host is a Generic Host: its web server is the "HttpEndpoint" feature of its party (CK.AppIdentity.HttpEndpoint),
/// the "Main" endpoint of the "CK-AppIdentity:Local:HttpEndpoint" section.
/// </para>
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
    public static IHost Build( string[] args, Action<IConfigurationBuilder>? configure = null, string? contentRoot = null )
    {
        var builder = Host.CreateApplicationBuilder( new HostApplicationBuilderSettings { Args = args, ContentRootPath = contentRoot } );
        configure?.Invoke( builder.Configuration );
        builder.UseCKMonitoring();
        builder.AddApplicationIdentityServiceConfiguration();
        // The monitor of the work outside the requests (the requests have the one of their ScopedHttpContext).
        builder.Services.AddScoped<IActivityMonitor, ActivityMonitor>();
        builder.Services.AddScoped( sp => sp.GetRequiredService<IActivityMonitor>().ParallelLogger );
        // The web application of the "Main" endpoint: the authentication of the "/.cris" requests, the Cris endpoints and
        // the client application (Admin.Builder). In Development, MapClients starts its dev server ("ng serve") and forwards
        // to it.
        builder.AddHttpEndpoint( "Main", web =>
        {
            web.Services.AddCrisAuthentication();
            web.AppendApplicationBuilder( app =>
            {
                app.UseCrisAuthentication();
                app.UseCris();
                // CKBuild gives the WebApplication.
                ((WebApplication)app).MapClients();
            } );
        } );
        var monitor = builder.GetBuilderMonitor();
        var map = StObjContextRoot.Load( typeof( AdminHost ).Assembly, monitor )
                  ?? throw new InvalidOperationException( "Unable to load the generated code: build the Admin.Builder." );
        builder.Services.AddStObjMap( monitor, map );
        return builder.CKBuild();
    }
}
