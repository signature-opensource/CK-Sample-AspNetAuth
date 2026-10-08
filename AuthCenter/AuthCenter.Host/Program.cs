using CK.Sample.AuthCenter;

var app = await AuthCenterHost.BuildAsync( args );
await app.RunAsync();
