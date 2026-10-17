using Npgsql;

namespace ResolveIQ.Api.Data;

public static class ConnectionStringHelper
{
    public static string? Normalize(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        NpgsqlConnectionStringBuilder builder;

        if (connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(connectionString);
            var userInfo = uri.UserInfo.Split(':', 2);
            var username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : "";
            var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
            var database = uri.AbsolutePath.Trim('/');
            var port = uri.Port > 0 ? uri.Port : 5432;

            builder = new NpgsqlConnectionStringBuilder
            {
                Host = uri.Host,
                Port = port,
                Database = database,
                Username = username,
                Password = password
            };

            // Parse any query parameters from URI (e.g. ?sslmode=require)
            if (!string.IsNullOrEmpty(uri.Query))
            {
                var queryParams = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
                foreach (var param in queryParams)
                {
                    var kv = param.Split('=', 2);
                    if (kv.Length == 2)
                    {
                        var key = Uri.UnescapeDataString(kv[0]).ToLowerInvariant();
                        var val = Uri.UnescapeDataString(kv[1]);
                        if (key is "sslmode" or "ssl_mode")
                        {
                            if (Enum.TryParse<SslMode>(val, true, out var sslMode))
                            {
                                builder.SslMode = sslMode;
                            }
                        }
                    }
                }
            }
        }
        else
        {
            try
            {
                builder = new NpgsqlConnectionStringBuilder(connectionString);
            }
            catch
            {
                return connectionString;
            }
        }

        // Disable GSSAPI / Kerberos encryption mode to prevent loading libgssapi_krb5.so.2
        // and eliminate negotiation timeouts in containerized environments (such as Render Linux)
        builder.GssEncryptionMode = GssEncryptionMode.Disable;

        // For Render / Cloud remote PostgreSQL hosts:
        // Render internal PostgreSQL instances use self-signed TLS certificates.
        // TrustServerCertificate=true allows encrypted TLS communication without failing CA chain validation.
        // Security trade-off: Skips public CA verification; acceptable on Render's isolated internal network.
        var isLocalhost = string.Equals(builder.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(builder.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(builder.Host, "db", StringComparison.OrdinalIgnoreCase);

        if (!isLocalhost)
        {
            if (builder.SslMode == SslMode.Disable)
            {
                builder.SslMode = SslMode.Prefer;
            }

#pragma warning disable CS0618 // TrustServerCertificate is obsolete in Npgsql 8+ but maintained for compatibility
            builder.TrustServerCertificate = true;
#pragma warning restore CS0618
        }

        return builder.ConnectionString;
    }
}
