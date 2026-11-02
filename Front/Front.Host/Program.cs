using CK.Sample.Front;
using Microsoft.Extensions.Hosting;

var app = FrontHost.Build( args );
await app.RunAsync();
