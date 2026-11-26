using CK.Core;
using CK.Setup;
using Microsoft.Data.SqlClient;
using System;

// Generates the code of Auth3.Host and sets up its database.
return await CKomposableAppBuilder.RunAsync( ( monitor, builder ) =>
{
    builder.EngineConfiguration.EnsureAspect<SetupableAspectConfiguration>();
    var sql = builder.EngineConfiguration.EnsureAspect<SqlSetupAspectConfiguration>();
    sql.DefaultDatabaseConnectionString = "Server=.;Database=CKSampleAuth3;Integrated Security=True;TrustServerCertificate=true";
    // This is the local SQL Server with the Windows account. A machine that has no such server (Linux, a container)
    // already gives its server to the tests with this environment variable (see CK.Testing.SqlServer):
    // the builder uses the same server and keeps the database name.
    var master = Environment.GetEnvironmentVariable( "TestHelper__SqlServer__MasterConnectionString" );
    if( !String.IsNullOrEmpty( master ) )
    {
        var database = new SqlConnectionStringBuilder( sql.DefaultDatabaseConnectionString ).InitialCatalog;
        sql.DefaultDatabaseConnectionString = new SqlConnectionStringBuilder( master ) { InitialCatalog = database }.ConnectionString;
        monitor.Info( $"Database '{database}': using the SQL Server of the 'TestHelper__SqlServer__MasterConnectionString' environment variable." );
    }
} );
