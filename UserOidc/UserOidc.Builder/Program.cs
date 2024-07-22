
using CK.Setup;

return CKomposableAppBuilder.Run( ( monitor, builder ) =>
{
    builder.EngineConfiguration.EnsureAspect<SetupableAspectConfiguration>();
    var sql = builder.EngineConfiguration.EnsureAspect<SqlSetupAspectConfiguration>();
    sql.DefaultDatabaseConnectionString = "Server=.;Database=SampleUserOidc;Integrated Security=True;TrustServerCertificate=true";
    //var binPathTSConf = builder.EnsureDefaultTypeScriptAspectConfiguration();
    //binPathTSConf.TargetProjectPath = builder.GetHostFolderPath().Combine( "Clients/AspNetAuthTester" );
} );
