using CK.Setup;

// Generates the code of Admin.Host and its Angular application (Admin.Host/Clients/App).
return await CKomposableAppBuilder.RunAsync( ( monitor, builder ) =>
{
    var ts = builder.EnsureDefaultTypeScriptAspectConfiguration();
    ts.TargetProjectPath = builder.GetHostFolderPath().Combine( "Clients/App" );
} );
