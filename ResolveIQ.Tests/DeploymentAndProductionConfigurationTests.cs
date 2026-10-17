using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ResolveIQ.Api.Data;
using ResolveIQ.Api.Health;
using Xunit;

namespace ResolveIQ.Tests;

public class DeploymentAndProductionConfigurationTests
{
    [Fact]
    public async Task ForwardedHeaders_PopulatesClientIpAndHttpsScheme_WhenReverseProxyHeadersPresent()
    {
        var services = new ServiceCollection();
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

        var serviceProvider = services.BuildServiceProvider();
        var optionsAccessor = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ForwardedHeadersOptions>>();

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.195";
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1"); // Private proxy IP

        var middleware = new ForwardedHeadersMiddleware(
            next: (innerContext) =>
            {
                Assert.Equal("203.0.113.195", innerContext.Connection.RemoteIpAddress?.ToString());
                Assert.Equal("https", innerContext.Request.Scheme);
                return Task.CompletedTask;
            },
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
            optionsAccessor);

        await middleware.Invoke(context);
    }

    [Fact]
    public void MigrationBundleSql_ExistsAndContainsIdempotencyGuards()
    {
        var baseDir = AppContext.BaseDirectory;
        // Search for migration_bundle.sql in project tree
        var currentDir = new DirectoryInfo(baseDir);
        string? migrationSqlPath = null;

        while (currentDir != null)
        {
            var candidate = Path.Combine(currentDir.FullName, "ResolveIQ.Api", "Migrations", "migration_bundle.sql");
            if (File.Exists(candidate))
            {
                migrationSqlPath = candidate;
                break;
            }
            currentDir = currentDir.Parent;
        }

        Assert.NotNull(migrationSqlPath);
        Assert.True(File.Exists(migrationSqlPath));

        var sqlContent = File.ReadAllText(migrationSqlPath);
        Assert.Contains("__EFMigrationsHistory", sqlContent);
        Assert.Contains("IF NOT EXISTS", sqlContent);
        Assert.Contains("COMMIT;", sqlContent);
    }

    [Fact]
    public void Dockerfile_ExistsAndEnforcesNonRootAppUser()
    {
        var baseDir = AppContext.BaseDirectory;
        var currentDir = new DirectoryInfo(baseDir);
        string? dockerfilePath = null;

        while (currentDir != null)
        {
            var candidate = Path.Combine(currentDir.FullName, "Dockerfile");
            if (File.Exists(candidate))
            {
                dockerfilePath = candidate;
                break;
            }
            currentDir = currentDir.Parent;
        }

        Assert.NotNull(dockerfilePath);
        var dockerfileContent = File.ReadAllText(dockerfilePath);

        Assert.Contains("FROM mcr.microsoft.com/dotnet/sdk:10.0", dockerfileContent);
        Assert.Contains("FROM mcr.microsoft.com/dotnet/aspnet:10.0", dockerfileContent);
        Assert.Contains("libgssapi-krb5-2", dockerfileContent);
        Assert.Contains("USER app", dockerfileContent);
        Assert.Contains("EXPOSE 8080", dockerfileContent);
        Assert.Contains("ENTRYPOINT", dockerfileContent);
    }

    [Fact]
    public void ConnectionStringHelper_NormalizesRenderUri_DisablesGssAndEnablesSsl()
    {
        var renderUri = "postgres://resolveiq_user:super_secret_pw@dpg-c12345678-a:5432/resolveiq_db";
        var normalized = ConnectionStringHelper.Normalize(renderUri);

        Assert.NotNull(normalized);
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(normalized);

        Assert.Equal("dpg-c12345678-a", builder.Host);
        Assert.Equal(5432, builder.Port);
        Assert.Equal("resolveiq_db", builder.Database);
        Assert.Equal("resolveiq_user", builder.Username);
        Assert.Equal("super_secret_pw", builder.Password);
        Assert.Equal(Npgsql.GssEncryptionMode.Disable, builder.GssEncryptionMode);
        Assert.Equal(Npgsql.SslMode.Prefer, builder.SslMode);
#pragma warning disable CS0618
        Assert.True(builder.TrustServerCertificate);
#pragma warning restore CS0618
    }

    [Fact]
    public void ConnectionStringHelper_NormalizesRenderUriWithSslQueryParam()
    {
        var renderUri = "postgresql://resolveiq_user:super_secret_pw@dpg-c12345678-a.oregon-postgres.render.com:5432/resolveiq_db?sslmode=require";
        var normalized = ConnectionStringHelper.Normalize(renderUri);

        Assert.NotNull(normalized);
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(normalized);

        Assert.Equal("dpg-c12345678-a.oregon-postgres.render.com", builder.Host);
        Assert.Equal(Npgsql.SslMode.Require, builder.SslMode);
        Assert.Equal(Npgsql.GssEncryptionMode.Disable, builder.GssEncryptionMode);
#pragma warning disable CS0618
        Assert.True(builder.TrustServerCertificate);
#pragma warning restore CS0618
    }

    [Fact]
    public void ConnectionStringHelper_NormalizesKeyValueString_DisablesGssAndSetsSslForRemote()
    {
        var kvString = "Host=dpg-c12345678-a;Database=resolveiq_db;Username=postgres;Password=test;";
        var normalized = ConnectionStringHelper.Normalize(kvString);

        Assert.NotNull(normalized);
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(normalized);

        Assert.Equal("dpg-c12345678-a", builder.Host);
        Assert.Equal(Npgsql.GssEncryptionMode.Disable, builder.GssEncryptionMode);
        Assert.Equal(Npgsql.SslMode.Prefer, builder.SslMode);
#pragma warning disable CS0618
        Assert.True(builder.TrustServerCertificate);
#pragma warning restore CS0618
    }

    [Fact]
    public void ConnectionStringHelper_NormalizesUriWithTrailingSlash_TrimsDatabaseAndEnablesTrustServerCertificate()
    {
        var renderUri = "postgres://resolveiq_user:super_secret_pw@dpg-c12345678-a/resolveiq_db/";
        var normalized = ConnectionStringHelper.Normalize(renderUri);

        Assert.NotNull(normalized);
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(normalized);

        Assert.Equal("dpg-c12345678-a", builder.Host);
        Assert.Equal(5432, builder.Port);
        Assert.Equal("resolveiq_db", builder.Database);
        Assert.Equal(Npgsql.GssEncryptionMode.Disable, builder.GssEncryptionMode);
        Assert.Equal(Npgsql.SslMode.Prefer, builder.SslMode);
#pragma warning disable CS0618
        Assert.True(builder.TrustServerCertificate);
#pragma warning restore CS0618
    }

    [Fact]
    public void ConnectionStringHelper_PreservesLocalhostWithoutForcingSsl()
    {
        var localString = "Host=localhost;Port=5432;Database=resolveiq_db;Username=postgres;Password=test;";
        var normalized = ConnectionStringHelper.Normalize(localString);

        Assert.NotNull(normalized);
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(normalized);

        Assert.Equal("localhost", builder.Host);
        Assert.Equal(Npgsql.GssEncryptionMode.Disable, builder.GssEncryptionMode);
#pragma warning disable CS0618
        Assert.False(builder.TrustServerCertificate);
#pragma warning restore CS0618
    }

    [Fact]
    public void ConnectionStringHelper_PreservesLocalhostAliasesWithoutTrustServerCertificate()
    {
        var ipLoopback = "Host=127.0.0.1;Port=5432;Database=resolveiq_db;Username=postgres;Password=test;";
        var normalizedIp = ConnectionStringHelper.Normalize(ipLoopback);
        var builderIp = new Npgsql.NpgsqlConnectionStringBuilder(normalizedIp);
#pragma warning disable CS0618
        Assert.False(builderIp.TrustServerCertificate);
#pragma warning restore CS0618

        var dockerDb = "Host=db;Port=5432;Database=resolveiq_db;Username=postgres;Password=test;";
        var normalizedDb = ConnectionStringHelper.Normalize(dockerDb);
        var builderDb = new Npgsql.NpgsqlConnectionStringBuilder(normalizedDb);
#pragma warning disable CS0618
        Assert.False(builderDb.TrustServerCertificate);
#pragma warning restore CS0618
    }

    [Fact]
    public void DockerCompose_ExistsAndConfiguresPersistentDatabaseVolume()
    {
        var baseDir = AppContext.BaseDirectory;
        var currentDir = new DirectoryInfo(baseDir);
        string? composePath = null;

        while (currentDir != null)
        {
            var candidate = Path.Combine(currentDir.FullName, "docker-compose.yml");
            if (File.Exists(candidate))
            {
                composePath = candidate;
                break;
            }
            currentDir = currentDir.Parent;
        }

        Assert.NotNull(composePath);
        var composeContent = File.ReadAllText(composePath);

        Assert.Contains("postgres_data:", composeContent);
        Assert.Contains("condition: service_healthy", composeContent);
        Assert.Contains("resolveiq_network", composeContent);
    }

    [Fact]
    public void RenderYaml_ConfiguresBackendUrlAndPreservesAudience()
    {
        var baseDir = AppContext.BaseDirectory;
        var currentDir = new DirectoryInfo(baseDir);
        string? renderYamlPath = null;

        while (currentDir != null)
        {
            var candidate = Path.Combine(currentDir.FullName, "render.yaml");
            if (File.Exists(candidate))
            {
                renderYamlPath = candidate;
                break;
            }
            currentDir = currentDir.Parent;
        }

        Assert.NotNull(renderYamlPath);
        var content = File.ReadAllText(renderYamlPath);

        Assert.Contains("value: https://resolveiq-x1fk.onrender.com", content);
        Assert.Contains("key: VITE_API_BASE_URL", content);
        Assert.Contains("key: Jwt__Issuer", content);
        Assert.Contains("key: Jwt__Audience", content);
        Assert.Contains("value: https://resolveiq-client.onrender.com", content);
    }
}

