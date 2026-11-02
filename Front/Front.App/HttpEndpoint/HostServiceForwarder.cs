using CK.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.Sample.Front;

/// <summary>
/// Adds the services of the host to the services of a web application, with the rules of the CK engine's endpoint
/// containers: a singleton is forwarded to the host (one instance), a singleton instance and an open generic are copied,
/// a scoped or transient service is copied (created in the request scope).
/// <para>
/// FEASIBILITY CHECK (.wip/cris-auth/52-http-endpoint.md).
/// </para>
/// </summary>
static class HostServiceForwarder
{
    // The services that the web application must not get from the host: an IHostedService would start twice; the monitor
    // of a request is the one of its ScopedHttpContext (registered by the web application).
    static readonly HashSet<Type> _excluded = [typeof( IHostedService ), typeof( IActivityMonitor ), typeof( IParallelLogger )];

    // The services that the web application takes from the host even if it has its own: one logging pipeline (the host's,
    // that CK.Monitoring configured).
    static readonly HashSet<Type> _fromTheHost = [typeof( ILoggerFactory )];

    /// <summary>
    /// Adds the <paramref name="hostServices"/> to the <paramref name="services"/> of the web application.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="hostServices">The services of the host.</param>
    /// <param name="host">The service provider of the host.</param>
    /// <param name="services">The services of the web application.</param>
    public static void Forward( IActivityMonitor monitor, IServiceCollection hostServices, IServiceProvider host, IServiceCollection services )
    {
        foreach( var t in _fromTheHost ) services.RemoveAll( t );
        // What the web application already has (its infrastructure: configuration, environment, lifetime, options,
        // routing...) is kept.
        var own = new HashSet<Type>( services.Select( d => d.ServiceType ) );
        int forwarded = 0, copied = 0;
        var duplicated = new List<Type>();
        foreach( var g in hostServices.GroupBy( d => (d.ServiceType, d.ServiceKey) ) )
        {
            var t = g.Key.ServiceType;
            if( _excluded.Contains( t ) || own.Contains( t ) ) continue;
            var descriptors = g.ToList();
            if( t.IsGenericTypeDefinition || descriptors.Any( d => d.Lifetime != ServiceLifetime.Singleton ) )
            {
                // Copied: an open generic is closed by each container; scoped and transient services are created in the
                // scopes of the web application. A singleton among them would be a second instance.
                foreach( var d in descriptors )
                {
                    services.Add( d );
                    ++copied;
                    if( d.Lifetime == ServiceLifetime.Singleton && !t.IsGenericTypeDefinition && !HasInstance( d ) ) duplicated.Add( t );
                }
                continue;
            }
            if( descriptors.Count == 1 && HasInstance( descriptors[0] ) )
            {
                services.Add( descriptors[0] );
                ++copied;
                continue;
            }
            var key = g.Key.ServiceKey;
            if( key == null )
            {
                services.Add( new ServiceDescriptor( t, _ => host.GetRequiredService( t ), ServiceLifetime.Singleton ) );
                if( descriptors.Count > 1 )
                {
                    // The container of the web application would give the last one for each item: the enumeration
                    // is forwarded as a whole.
                    var tEnum = typeof( IEnumerable<> ).MakeGenericType( t );
                    services.Add( new ServiceDescriptor( tEnum, _ => host.GetRequiredService( tEnum ), ServiceLifetime.Singleton ) );
                }
            }
            else
            {
                if( descriptors.Count > 1 ) monitor.Warn( $"Keyed singleton '{t:C}' ('{key}') is registered {descriptors.Count} times: only the last one is forwarded." );
                services.Add( new ServiceDescriptor( t, key, ( _, k ) => host.GetRequiredKeyedService( t, k ), ServiceLifetime.Singleton ) );
            }
            ++forwarded;
        }
        foreach( var t in _fromTheHost )
        {
            services.Add( new ServiceDescriptor( t, _ => host.GetRequiredService( t ), ServiceLifetime.Singleton ) );
        }
        monitor.Info( $"Host services: {forwarded} singletons forwarded, {copied} descriptors copied, {own.Count} service types of the web application kept." );
        if( duplicated.Count > 0 )
        {
            monitor.Warn( $"Singletons registered with scoped or transient services of the same type are copied (a second instance in the web application): {duplicated.Distinct().Select( t => t.ToCSharpName() ).Concatenate()}." );
        }

        static bool HasInstance( ServiceDescriptor d ) => d.IsKeyedService ? d.KeyedImplementationInstance != null : d.ImplementationInstance != null;
    }
}
