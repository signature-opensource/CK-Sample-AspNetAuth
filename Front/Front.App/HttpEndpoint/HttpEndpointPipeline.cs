using CK.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace CK.Sample.Front;

/// <summary>
/// What the host gives to the <see cref="HttpEndpointFeatureDriver"/>: the services and the pipeline of its web
/// application, and the services of the host (forwarded to the web application).
/// <para>
/// FEASIBILITY CHECK (.wip/cris-auth/52-http-endpoint.md): this is local to the sample, the HttpEndpoint feature is not
/// a package yet.
/// </para>
/// </summary>
[SingletonService]
public sealed class HttpEndpointPipeline
{
    /// <summary>
    /// Initializes a new pipeline.
    /// </summary>
    /// <param name="configureServices">Registers the services of the pipeline (in the web application only).</param>
    /// <param name="configure">Configures the pipeline of the built web application.</param>
    public HttpEndpointPipeline( Action<IServiceCollection> configureServices, Action<WebApplication> configure )
    {
        ConfigureServices = configureServices;
        Configure = configure;
    }

    /// <summary>
    /// Gets the registration of the services of the pipeline.
    /// </summary>
    public Action<IServiceCollection> ConfigureServices { get; }

    /// <summary>
    /// Gets the configuration of the pipeline.
    /// </summary>
    public Action<WebApplication> Configure { get; }

    /// <summary>
    /// Gets or sets the services of the host: set by the host once it is built (its collection is complete and read-only).
    /// </summary>
    public IServiceCollection? HostServices { get; set; }
}
