using System.Data.SqlClient;
using EventStore.Database;

namespace EventStore.Tests
{
    internal class ServiceConfig
    {
        public static readonly ServiceConfig Instance = new ServiceConfig();

        private ServiceConfig()
        {
            ConnectionStringBuilder = new SqlConnectionStringBuilder(Config.DatabaseConnectionString);
            ConnectionProvider = new ConnectionProvider(ConnectionStringBuilder);
            DbStore = new DbStore(ConnectionProvider);
        }

        public SqlConnectionStringBuilder ConnectionStringBuilder { get; }
        public ConnectionProvider ConnectionProvider { get; }
        public DbStore DbStore { get; }
    }
}
