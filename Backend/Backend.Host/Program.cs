using CK.Sample.Backend;
using Microsoft.Extensions.Hosting;

var app = await BackendHost.BuildAsync( args );
await app.RunAsync();
