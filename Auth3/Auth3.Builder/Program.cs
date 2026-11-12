using CK.Setup;

// Generates the code of Auth3.Host and sets up its database.
return await CKomposableAppBuilder.RunAsync( ( monitor, builder ) =>
{
    builder.EngineConfiguration.EnsureAspect<SetupableAspectConfiguration>();
    var sql = builder.EngineConfiguration.EnsureAspect<SqlSetupAspectConfiguration>();
    sql.DefaultDatabaseConnectionString = "Server=.;Database=CKSampleAuth3;Integrated Security=True;TrustServerCertificate=true";
} );
