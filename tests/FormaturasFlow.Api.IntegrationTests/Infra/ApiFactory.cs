using FormaturasFlow.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
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

    public const string CoraWebhookSecret = "segredo-webhook-cora-de-teste";

    public string ConnectionString => _pg.GetConnectionString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
    }

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();

        Environment.SetEnvironmentVariable("ConnectionStrings__Default", ConnectionString);
        Environment.SetEnvironmentVariable("Jwt__Issuer", "https://api.test");
        Environment.SetEnvironmentVariable("Jwt__Audience", "test-audience");
        Environment.SetEnvironmentVariable("Jwt__Key", "chave-hmac-forte-com-mais-de-32-bytes-obrigatorio-aqui!!");
        Environment.SetEnvironmentVariable("Jwt__AccessTokenMinutes", "60");
        Environment.SetEnvironmentVariable("Asaas__Sandbox", "true");
        Environment.SetEnvironmentVariable("Asaas__ApiKey", "test-asaas-api-key");
        Environment.SetEnvironmentVariable("Asaas__WebhookToken", "test-asaas-webhook-token");
        Environment.SetEnvironmentVariable("Asaas__WebhookSecret", "test-asaas-webhook-secret");
        Environment.SetEnvironmentVariable("Cora__Sandbox", "true");
        Environment.SetEnvironmentVariable("Cora__ClientId", "test-cora-client");
        Environment.SetEnvironmentVariable("Cora__WebhookToken", "test-cora-webhook-token");
        Environment.SetEnvironmentVariable("Cora__WebhookSecret", CoraWebhookSecret);

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        /*  A sobrecarga que recebe string só atende SQL Server; para Postgres
            é obrigatório passar a conexão, senão o Respawn lança
            ArgumentException e TODO teste de integração falha no arranque.  */
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],

            /*  AspNetRoles é dado de referência, semeado uma vez no arranque
                da aplicação.  Limpá-lo entre testes deixaria o processo sem
                os papéis, e toda requisição autorizada morreria com
                "Role SUPER_ADMIN does not exist".  */
            TablesToIgnore =
            [
                new Respawn.Graph.Table("__EFMigrationsHistory"),
                new Respawn.Graph.Table("AspNetRoles")
            ]
        });
    }

    public async Task ResetDatabaseAsync()
    {
        if (_respawner is null) return;

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await _respawner.ResetAsync(conn);
    }

    public new async Task DisposeAsync()
    {
        await _pg.DisposeAsync();
        await base.DisposeAsync();
    }
}

[CollectionDefinition("api")]
public class ApiCollection : ICollectionFixture<ApiFactory> { }
