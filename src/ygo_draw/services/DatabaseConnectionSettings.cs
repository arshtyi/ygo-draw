using Npgsql;

namespace ygo_draw.services;

public sealed class DatabaseConnectionSettings
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5432;
    public string User { get; set; } = "ygo_draw";
    public string Password { get; set; } = string.Empty;
    public string Database { get; set; } = "ygo_draw_db";
    public string SslMode { get; set; } = "prefer";

    public string BuildConnectionString()
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Host,
            Port = Port,
            Username = User,
            Password = Password,
            Database = Database
        };

        if (Enum.TryParse<SslMode>(SslMode, ignoreCase: true, out var sslMode))
        {
            builder.SslMode = sslMode;
        }

        return builder.ConnectionString;
    }
}
