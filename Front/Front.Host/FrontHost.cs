using CK.Core;
using CK.TypeScript;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;

namespace CK.Sample.Front;

/// <summary>
/// The front of the sample: it serves its client applications and its "/.cris" endpoints on the same origin. It has no
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
        var monitor = builder.GetBuilderMonitor();
        var map = StObjContextRoot.Load( typeof( FrontHost ).Assembly, monitor )
                  ?? throw new InvalidOperationException( "Unable to load the generated code: build the Front.Builder." );
        var app = builder.CKBuild( map );
        app.UseCrisAuthentication();
        app.UseCris();
        // The two client applications (Front.Builder/CKSetup.xml) share this origin: the MiniApp under "/mini-app/", the
        // App everywhere else. In Development, the host starts their dev server ("ng serve") and forwards to it.
        app.MapClients();
        return app;
    }
}
