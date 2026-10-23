using MySqlConnector;

namespace API.Database;

// Opens MySQL connections as druid_llm_ro, the read-only login for the concierge's
// model-written knowledge queries. That login can only SELECT from
// v_knowledge_documents (see create-llm-ro-user.sql), which is the real protection;
// the validation in KnowledgeQueryValidator is a backup layer.
//
// The connection string comes from DB_LLM_RO_CONNECTION. Never log or return it.
public class ReadOnlyDb
{
    public const int CommandTimeoutSeconds = 3;

    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DB_LLM_RO_CONNECTION"));

    public async Task<MySqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connectionString = Environment.GetEnvironmentVariable("DB_LLM_RO_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new DatabaseUnavailableException("DB_LLM_RO_CONNECTION is not set.");

        MySqlConnectionStringBuilder builder;
        try
        {
            builder = new MySqlConnectionStringBuilder(connectionString)
            {
                ConnectionTimeout = 5,
                DefaultCommandTimeout = CommandTimeoutSeconds,
                AllowUserVariables = false
            };
        }
        catch (ArgumentException)
        {
            throw new DatabaseUnavailableException("DB_LLM_RO_CONNECTION is not a valid connection string.");
        }

        var connection = new MySqlConnection(builder.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            // Server-side cap on how long any SELECT on this connection may run.
            await using var cap = new MySqlCommand("SET SESSION max_execution_time = 2000", connection);
            await cap.ExecuteNonQueryAsync(cancellationToken);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
        return connection;
    }
}
