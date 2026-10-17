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
        Assert.Contains("USER app", dockerfileContent);
        Assert.Contains("EXPOSE 8080", dockerfileContent);
        Assert.Contains("ENTRYPOINT", dockerfileContent);
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
}
