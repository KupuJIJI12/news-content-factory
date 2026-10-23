using System.Reflection;
using Microsoft.EntityFrameworkCore;
using NewsFactory.Infrastructure;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;

namespace NewsFactory.TestSupport;

/// <summary>
/// Real PostgreSQL for integration tests. Uses the server from env NEWSFACTORY_TEST_PG when set
/// (one database per test assembly), otherwise starts a postgres:17 Testcontainer.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string EnvVar = "NEWSFACTORY_TEST_PG";
    private PostgreSqlContainer? _container;
    private Respawner? _respawner;

    public string ConnectionString { get; private set; } = "";

    public async ValueTask InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable(EnvVar);
        if (!string.IsNullOrWhiteSpace(external))
        {
            var dbName = "nf_test_" + (Assembly.GetEntryAssembly()?.GetName().Name ?? "tests")
                .Replace('.', '_').ToLowerInvariant();
            var builder = new NpgsqlConnectionStringBuilder(external) { Database = dbName };
            await RecreateDatabaseAsync(external, dbName);
            ConnectionString = builder.ConnectionString;
        }
        else
        {
            _container = new PostgreSqlBuilder("postgres:17").Build();
            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
        }

        await using (var ctx = CreateContext())
            await ctx.Database.MigrateAsync();

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
            TablesToIgnore = ["__EFMigrationsHistory"],
        });
    }

    public NewsFactoryDbContext CreateContext(TimeProvider? time = null)
    {
        var options = new DbContextOptionsBuilder<NewsFactoryDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new NewsFactoryDbContext(options, time ?? TimeProvider.System);
    }

    public async Task ResetAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await _respawner!.ResetAsync(conn);
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        if (_container is not null) await _container.DisposeAsync();
    }

    private static async Task RecreateDatabaseAsync(string serverConnectionString, string dbName)
    {
        var admin = new NpgsqlConnectionStringBuilder(serverConnectionString) { Database = "postgres" };
        await using var conn = new NpgsqlConnection(admin.ConnectionString);
        await conn.OpenAsync();
        await using (var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{dbName}\" WITH (FORCE)", conn))
            await drop.ExecuteNonQueryAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", conn);
        await create.ExecuteNonQueryAsync();
    }
}

public static class PostgresFixtureExtensions
{
    public static Microsoft.EntityFrameworkCore.IDbContextFactory<NewsFactory.Infrastructure.NewsFactoryDbContext> CreateFactory(
        this PostgresFixture db, TimeProvider? time = null) =>
        new NewsFactory.Infrastructure.NewsFactoryDbContextFactory(
            new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<NewsFactory.Infrastructure.NewsFactoryDbContext>()
                .UseNpgsql(db.ConnectionString).Options,
            time ?? TimeProvider.System);
}
