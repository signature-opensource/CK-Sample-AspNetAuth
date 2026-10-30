using CK.Sample.Backend;

var app = await BackendHost.BuildAsync( args );
await app.RunAsync();
