
using CK.Setup;

return await CKomposableAppBuilder.RunAsync( ( monitor, builder ) =>
{
    builder.EngineConfiguration.EnsureAspect<SetupableAspectConfiguration>();
    var sql = builder.EngineConfiguration.EnsureAspect<SqlSetupAspectConfiguration>();
    sql.DefaultDatabaseConnectionString = "Server=.;Database=SampleUserOidc;Integrated Security=True;TrustServerCertificate=true";

    var tester = builder.EnsureDefaultTypeScriptAspectConfiguration();
    tester.TargetProjectPath = builder.GetHostFolderPath().Combine( "Clients/AspNetAuthTester" );
    tester.TypeFilterName = "None";

    var angularDemo = new TypeScriptBinPathAspectConfiguration();
    tester.AddOtherConfiguration( angularDemo );
    angularDemo.AutoInstallYarn = true;
    angularDemo.GitIgnoreCKGenFolder = true;
    angularDemo.TargetProjectPath = builder.GetHostFolderPath().Combine( "Clients/AngularDemo" );
    angularDemo.TypeFilterName = "TypeScriptAngularDemo";
} );
