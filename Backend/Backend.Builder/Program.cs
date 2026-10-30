using CK.Setup;

// Generates the code of Backend.Host and sets up its database.
return await CKomposableAppBuilder.RunAsync( ( monitor, builder ) =>
{
    builder.EngineConfiguration.EnsureAspect<SetupableAspectConfiguration>();
    var sql = builder.EngineConfiguration.EnsureAspect<SqlSetupAspectConfiguration>();
    sql.DefaultDatabaseConnectionString = "Server=.;Database=CKSampleBackend;Integrated Security=True;TrustServerCertificate=true";
} );
