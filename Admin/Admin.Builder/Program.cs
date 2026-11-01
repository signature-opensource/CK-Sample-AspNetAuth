using CK.Setup;

// Generates the code of Admin.Host and its client application (Admin.Host/Clients/App, served at "/").
return await CKomposableAppBuilder.RunAsync( ( monitor, builder ) =>
{
    var ts = builder.EnsureDefaultTypeScriptAspectConfiguration();
    ts.Name = "App";
    ts.Kind = ClientKind.DefaultApp;
    // The Front's client applications use 4200 and 4201.
    ts.DevServerPort = 4300;
} );
