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
/// <para>
/// FEASIBILITY CHECK (.wip/cris-auth/52-http-endpoint.md): the host is a Generic Host, its web application is created and
/// started by the "HttpEndpoint" feature of its party (<see cref="HttpEndpointFeatureDriver"/>), configured by the
/// "CK-AppIdentity:Local:HttpEndpoint" section.
/// </para>
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
    public static IHost Build( string[] args, Action<IConfigurationBuilder>? configure = null, string? contentRoot = null )
    {
        var builder = Host.CreateApplicationBuilder( new HostApplicationBuilderSettings { Args = args, ContentRootPath = contentRoot } );
        configure?.Invoke( builder.Configuration );
        builder.UseCKMonitoring();
        builder.AddApplicationIdentityServiceConfiguration();
        // The monitor of the work outside the requests (the requests have the one of their ScopedHttpContext).
        builder.Services.AddScoped<IActivityMonitor, ActivityMonitor>();
        builder.Services.AddScoped( sp => sp.GetRequiredService<IActivityMonitor>().ParallelLogger );
        // The web application: the authentication of the "/.cris" requests, the Cris endpoints and the two client
        // applications (Front.Builder/CKSetup.xml) on the same origin: the MiniApp under "/mini-app/", the App everywhere
        // else. In Development, MapClients starts their dev server ("ng serve") and forwards to it.
        var pipeline = new HttpEndpointPipeline( services => services.AddCrisAuthentication(),
                                                 app =>
                                                 {
                                                     app.UseCrisAuthentication();
                                                     app.UseCris();
                                                     app.MapClients();
                                                 } );
        builder.Services.AddSingleton( pipeline );
        var monitor = builder.GetBuilderMonitor();
        var map = StObjContextRoot.Load( typeof( FrontHost ).Assembly, monitor )
                  ?? throw new InvalidOperationException( "Unable to load the generated code: build the Front.Builder." );
        builder.Services.AddStObjMap( monitor, map );
        var host = builder.CKBuild();
        pipeline.HostServices = builder.Services;
        return host;
    }
}
