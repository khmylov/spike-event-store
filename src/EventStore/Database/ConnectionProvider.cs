using System;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace EventStore.Database
{
    internal sealed class ConnectionProvider
    {
        private static readonly ILogger _log = Log.ForContext<ConnectionProvider>();

        private readonly SqlConnectionStringBuilder _connectionStringBuilder;

        public ConnectionProvider(SqlConnectionStringBuilder connectionStringBuilder)
        {
            _connectionStringBuilder = connectionStringBuilder;
        }

        public async Task<TResult> WithConnectionAsync<TResult>(
            Func<SqlConnection, Task<TResult>> action,
            CancellationToken cancellationToken)
        {
            await using var connection = new SqlConnection(_connectionStringBuilder.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            _log.Debug("Opened SQL connection {id}", connection.ClientConnectionId);
            var result = await action(connection).ConfigureAwait(false);
            return result;
        }
    }
}
