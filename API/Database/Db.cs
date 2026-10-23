using System.Data;
using Dapper;
using MySqlConnector;

namespace API.Database;

// Thrown when DB_APP_CONNECTION isn't set. Shown to the client as a friendly
// "offline" message, like any other database failure.
public class DatabaseUnavailableException(string message) : Exception(message);

// Opens MySQL connections as druid_app. The connection string comes from
// DB_APP_CONNECTION in the environment (DotEnv loads it from the repo-root .env).
// Never log or return the connection string.
public class Db
{
    // Every row belongs to the one hotel loaded by seed.sql.
    public const int HotelId = 1;

    static Db()
    {
        SqlMapper.AddTypeHandler(new DateOnlyHandler());
    }

    public MySqlConnection Open()
    {
        var connectionString = Environment.GetEnvironmentVariable("DB_APP_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new DatabaseUnavailableException("DB_APP_CONNECTION is not set.");
        var connection = new MySqlConnection(connectionString);
        connection.Open();
        return connection;
    }

    // Stay nights are DATE columns; the models use DateOnly.
    private class DateOnlyHandler : SqlMapper.TypeHandler<DateOnly>
    {
        public override void SetValue(IDbDataParameter parameter, DateOnly value)
        {
            parameter.DbType = DbType.Date;
            parameter.Value = value.ToDateTime(TimeOnly.MinValue);
        }

        public override DateOnly Parse(object value) => value switch
        {
            DateOnly d => d,
            DateTime dt => DateOnly.FromDateTime(dt),
            _ => throw new DataException("Can't read " + value.GetType().Name + " as a date.")
        };
    }
}
