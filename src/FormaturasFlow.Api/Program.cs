using System.Text;
using System.Threading.RateLimiting;
using FormaturasFlow.Api.Auth;
using FormaturasFlow.Api.Data;
using FormaturasFlow.Api.Efi;
using FormaturasFlow.Api.Endpoints;
using FormaturasFlow.Api.Payments;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<EfiOptions>(builder.Configuration.GetSection(EfiOptions.SectionName));
builder.Services.Configure<AsaasOptions>(builder.Configuration.GetSection(AsaasOptions.SectionName));
builder.Services.Configure<CoraOptions>(builder.Configuration.GetSection(CoraOptions.SectionName));

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default ausente.");

builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));

builder.Services
    .AddIdentityCore<ApplicationUser>(o =>
    {
        o.Password.RequiredLength = 8;
        o.Password.RequireNonAlphanumeric = false;
        o.User.RequireUniqueEmail = true;
        o.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddRoles<ApplicationRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager();

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Seção Jwt ausente.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key))
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddScoped<JwtTokenService>();

builder.Services.AddTransient<EfiHttpHandler>();
builder.Services.AddHttpClient<EfiClient>()
    .ConfigurePrimaryHttpMessageHandler<EfiHttpHandler>();

/*  Composition root do roteamento de pagamentos: este é o ÚNICO ponto
    do sistema que conhece Asaas e Cora.  Endpoints e use cases veem
    apenas IPaymentRouter, e a matriz de domínio × método é injetada
    como dado (PaymentRoutingPolicy.Padrao).  */
builder.Services.AddHttpClient<AsaasPaymentGateway>();

/*  Singletons: o certificado mTLS é caro de carregar e o handler é
    transiente — sem isso, cada requisição reimportaria o PKCS#12.  O cache
    de token precisa do mesmo tratamento, porque o typed client é transiente
    e um cache dentro dele nasceria vazio a cada emissão.  */
builder.Services.AddSingleton<CoraCredentials>();
builder.Services.AddSingleton<CoraTokenProvider>();
builder.Services.AddTransient<CoraHttpHandler>();

builder.Services.AddHttpClient(CoraTokenProvider.HttpClientName)
    .ConfigurePrimaryHttpMessageHandler<CoraHttpHandler>();

builder.Services.AddHttpClient<CoraPaymentGateway>()
    .ConfigurePrimaryHttpMessageHandler<CoraHttpHandler>();

builder.Services.AddTransient<IPaymentGateway>(sp => sp.GetRequiredService<AsaasPaymentGateway>());
builder.Services.AddTransient<IPaymentGateway>(sp => sp.GetRequiredService<CoraPaymentGateway>());
builder.Services.AddTransient<IConsultaCobranca>(sp => sp.GetRequiredService<CoraPaymentGateway>());

builder.Services.AddSingleton(PaymentRoutingPolicy.Padrao);
builder.Services.AddScoped<IPaymentRouter, PaymentGatewayFactory>();

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"])
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

/*  O webhook da Cora é anônimo e cada POST aceito custa uma ida à Cora
    (token + consulta mTLS), então precisa de teto.

    A cota é SEPARADA por quem apresenta o segredo.  Uma janela única para
    todo mundo teria o efeito perverso de deixar um atacante encher o balde
    e a notificação legítima da Cora levar 429 — ou seja, o pagamento
    entraria e a parcela nunca seria baixada.  Quem não tem o segredo não
    alcança o balde de quem tem.

    Particionar por IP não serviria aqui: a API roda atrás de proxy, então
    todo request chega com o IP do proxy.  */
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    o.AddPolicy(CoraWebhookEndpoints.RateLimitPolicy, ctx =>
    {
        var segredo = ctx.RequestServices
            .GetRequiredService<IOptions<CoraOptions>>().Value.WebhookSecret;

        var autorizado = !string.IsNullOrWhiteSpace(segredo)
            && CoraWebhookEndpoints.SegredoConfere(ctx, segredo);

        return RateLimitPartition.GetFixedWindowLimiter(
            autorizado ? "cora-autorizado" : "cora-anonimo",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = autorizado ? 600 : 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    });
});

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddExceptionHandler<PaymentGatewayExceptionHandler>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await SeedRolesAsync(scope.ServiceProvider);
    await SeedAdminAsync(scope.ServiceProvider, app.Configuration);
}

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();
app.MapScalarApiReference();

app.MapGet("/", () => Results.Ok(new
{
    name = "FormaturasFlow.Api",
    version = "0.1.0",
    environment = app.Environment.EnvironmentName,
    docs = "/scalar"
}));

app.MapHealthChecks("/health");

app.MapAuthEndpoints();

var v1 = app.MapGroup("/api/v1");
v1.MapTurmaEndpoints();
v1.MapAlunoEndpoints();
v1.MapContratoEndpoints();
v1.MapParcelaEndpoints();
v1.MapEfiEndpoints();
v1.MapPaymentEndpoints();
v1.MapCoraWebhookEndpoints();

app.Run();

static async Task SeedRolesAsync(IServiceProvider sp)
{
    var roleMgr = sp.GetRequiredService<RoleManager<ApplicationRole>>();
    foreach (var r in new[] { Roles.SuperAdmin, Roles.Funcionario, Roles.Aluno })
        if (!await roleMgr.RoleExistsAsync(r))
            await roleMgr.CreateAsync(new ApplicationRole(r));
}

/*  Administrador declarado por configuração.

    Sem isto não existe caminho para criar um admin: o /register só promove
    o PRIMEIRO usuário do sistema e, depois dele, todo cadastro vira Aluno —
    sem nenhum endpoint de promoção.  Na prática, perder o primeiro usuário
    deixava a base sem dono, com mexer no banco à mão como única saída.

    É idempotente: no arranque seguinte apenas confirma o papel.  A senha só
    é usada na CRIAÇÃO, para um redeploy não sobrescrever silenciosamente a
    senha que o dono já trocou.  */
static async Task SeedAdminAsync(IServiceProvider sp, IConfiguration cfg)
{
    var email = cfg["Admin:Email"];
    var senha = cfg["Admin:Password"];

    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(senha))
        return;

    var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
    var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Seed.Admin");

    var user = await users.FindByEmailAsync(email);

    if (user is null)
    {
        user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            NomeCompleto = cfg["Admin:NomeCompleto"] ?? email
        };

        var criado = await users.CreateAsync(user, senha);
        if (!criado.Succeeded)
        {
            log.LogError("Admin {Email} não pôde ser criado: {Erros}",
                email, string.Join("; ", criado.Errors.Select(e => e.Description)));
            return;
        }

        log.LogInformation("Admin {Email} criado pelo seed.", email);
    }

    if (!await users.IsInRoleAsync(user, Roles.SuperAdmin))
    {
        await users.AddToRoleAsync(user, Roles.SuperAdmin);
        log.LogInformation("Admin {Email} promovido a {Papel}.", email, Roles.SuperAdmin);
    }
}

public partial class Program;
