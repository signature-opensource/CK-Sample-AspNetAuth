using CK.Sample.Admin;
using Microsoft.Extensions.Hosting;

var app = AdminHost.Build( args );
await app.RunAsync();
