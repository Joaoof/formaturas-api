using FormaturasFlow.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Respawn;
using Testcontainers.PostgreSql;
using Xunit;

namespace FormaturasFlow.Api.IntegrationTests.Infra;

public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("formaturas_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private Respawner? _respawner;

    public string ConnectionString => _pg.GetConnectionString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = ConnectionString,
                ["Jwt:Issuer"] = "https://api.test",
                ["Jwt:Audience"] = "test-audience",
                ["Jwt:Key"] = "chave-hmac-forte-com-mais-de-32-bytes-obrigatorio-aqui!!",
                ["Jwt:AccessTokenMinutes"] = "60",
                ["Efi:Sandbox"] = "true",
                ["Efi:WebhookSecret"] = "test-webhook-secret",
                ["Efi:PixKey"] = "test-pix-key"
            });
        });
    }

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        _respawner = await Respawner.CreateAsync(ConnectionString, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
            TablesToIgnore = [new Respawn.Graph.Table("__EFMigrationsHistory")]
        });
    }

    public async Task ResetDatabaseAsync()
    {
        if (_respawner is not null)
            await _respawner.ResetAsync(ConnectionString);
    }

    public new async Task DisposeAsync()
    {
        await _pg.DisposeAsync();
        await base.DisposeAsync();
    }
}

[CollectionDefinition("api")]
public class ApiCollection : ICollectionFixture<ApiFactory> { }
