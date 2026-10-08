using CK.Setup;

// Generates the code of AuthCenter.Host and sets up its database.
return await CKomposableAppBuilder.RunAsync( ( monitor, builder ) =>
{
    builder.EngineConfiguration.EnsureAspect<SetupableAspectConfiguration>();
    var sql = builder.EngineConfiguration.EnsureAspect<SqlSetupAspectConfiguration>();
    sql.DefaultDatabaseConnectionString = "Server=.;Database=CKSampleAuthCenter;Integrated Security=True;TrustServerCertificate=true";
} );
