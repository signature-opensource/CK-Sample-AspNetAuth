using CK.Setup;

// Generates the code of Front.Host and its Angular application (Front.Host/Clients/App).
return await CKomposableAppBuilder.RunAsync( ( monitor, builder ) =>
{
    var ts = builder.EnsureDefaultTypeScriptAspectConfiguration();
    ts.TargetProjectPath = builder.GetHostFolderPath().Combine( "Clients/App" );
} );
