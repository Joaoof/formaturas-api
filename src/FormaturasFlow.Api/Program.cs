using System.Text;
using System.Threading.RateLimiting;
using FormaturasFlow.Api.Asaas;
using FormaturasFlow.Api.Auth;
using FormaturasFlow.Api.Cora;
using FormaturasFlow.Api.Data;
using FormaturasFlow.Api.Endpoints;
using FormaturasFlow.Api.Pagamentos;
using FormaturasFlow.Api.Payments;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));

/*  Duas versoes das options coexistem durante a transicao para a nova
    arquitetura de pagamentos. O legado (Asaas/AsaasClient, Cora/CoraClient +
    PagamentoService) atende o endpoint standalone /api/v1/cobrancas; o novo
    (Payments.*) atende /api/v1/pagamentos com roteamento por dominio. */
builder.Services.Configure<FormaturasFlow.Api.Asaas.AsaasOptions>(
    builder.Configuration.GetSection(FormaturasFlow.Api.Asaas.AsaasOptions.SectionName));
builder.Services.Configure<FormaturasFlow.Api.Cora.CoraOptions>(
    builder.Configuration.GetSection(FormaturasFlow.Api.Cora.CoraOptions.SectionName));
builder.Services.Configure<FormaturasFlow.Api.Payments.AsaasOptions>(
    builder.Configuration.GetSection(FormaturasFlow.Api.Payments.AsaasOptions.SectionName));
builder.Services.Configure<FormaturasFlow.Api.Payments.CoraOptions>(
    builder.Configuration.GetSection(FormaturasFlow.Api.Payments.CoraOptions.SectionName));

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default ausente.");

builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));

builder.Services
    .AddIdentityCore<ApplicationUser>(o =>
    {
        /*  Alunos logam com o CPF como senha inicial (11 dígitos). As
            exigências de maiúscula/minúscula/símbolo do Identity default
            invalidam essa senha e quebram silenciosamente a adesão pública.
            Mantemos comprimento mínimo de 8 (o CPF tem 11) e obrigamos ao
            menos um dígito — bloqueia senhas triviais como "aaaaaaaa". */
        o.Password.RequiredLength         = 8;
        o.Password.RequireDigit           = true;
        o.Password.RequireLowercase       = false;
        o.Password.RequireUppercase       = false;
        o.Password.RequireNonAlphanumeric = false;
        o.User.RequireUniqueEmail         = true;
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

// --- Legado: PagamentoService (usa Asaas/AsaasClient + Cora/CoraClient) ---
builder.Services.AddHttpClient<AsaasClient>();
builder.Services.AddTransient<FormaturasFlow.Api.Cora.CoraHttpHandler>();
builder.Services.AddHttpClient<CoraClient>()
    .ConfigurePrimaryHttpMessageHandler<FormaturasFlow.Api.Cora.CoraHttpHandler>();
builder.Services.AddScoped<PagamentoService>();

/*  Composition root do roteamento de pagamentos (nova arquitetura): este eh o
    UNICO ponto do sistema que conhece Asaas e Cora. Endpoints e use cases veem
    apenas IPaymentRouter, e a matriz de dominio x metodo eh injetada como dado
    (PaymentRoutingPolicy.Padrao).  */
builder.Services.AddHttpClient<AsaasPaymentGateway>();

/*  Singletons: o certificado mTLS eh caro de carregar e o handler eh
    transiente — sem isso, cada requisicao reimportaria o PKCS#12. O cache
    de token precisa do mesmo tratamento, porque o typed client eh transiente
    e um cache dentro dele nasceria vazio a cada emissao.  */
builder.Services.AddSingleton<CoraCredentials>();
builder.Services.AddSingleton<CoraTokenProvider>();
builder.Services.AddTransient<FormaturasFlow.Api.Payments.CoraHttpHandler>();

builder.Services.AddHttpClient(CoraTokenProvider.HttpClientName)
    .ConfigurePrimaryHttpMessageHandler<FormaturasFlow.Api.Payments.CoraHttpHandler>();

builder.Services.AddHttpClient<CoraPaymentGateway>()
    .ConfigurePrimaryHttpMessageHandler<FormaturasFlow.Api.Payments.CoraHttpHandler>();

/*  Cadastro dos endpoints de notificação: mesmo canal mTLS da emissão.  */
builder.Services.AddHttpClient<CoraEndpointsClient>()
    .ConfigurePrimaryHttpMessageHandler<FormaturasFlow.Api.Payments.CoraHttpHandler>();

builder.Services.AddTransient<IPaymentGateway>(sp => sp.GetRequiredService<AsaasPaymentGateway>());
builder.Services.AddTransient<IPaymentGateway>(sp => sp.GetRequiredService<CoraPaymentGateway>());
builder.Services.AddTransient<IConsultaCobranca>(sp => sp.GetRequiredService<CoraPaymentGateway>());

builder.Services.AddSingleton(PaymentRoutingPolicy.Padrao);
builder.Services.AddScoped<IPaymentRouter, PaymentGatewayFactory>();

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();

var corsExplicit = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
    ?? ["http://localhost:5173", "http://localhost:3000"];
var corsPatterns = builder.Configuration.GetSection("Cors:Patterns").Get<string[]>()
    ?? [".vercel.app", ".lovable.app"];

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .SetIsOriginAllowed(origin =>
    {
        if (corsExplicit.Contains(origin, StringComparer.OrdinalIgnoreCase)) return true;
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var u)) return false;
        var host = u.Host;
        return corsPatterns.Any(pat =>
            pat.StartsWith('.') ? host.EndsWith(pat, StringComparison.OrdinalIgnoreCase) || host.Equals(pat[1..], StringComparison.OrdinalIgnoreCase)
                                : host.Equals(pat, StringComparison.OrdinalIgnoreCase));
    })
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

/*  O webhook da Cora eh anonimo e cada POST aceito custa uma ida a Cora
    (token + consulta mTLS), entao precisa de teto.

    A cota eh SEPARADA por quem apresenta o segredo.  Uma janela unica para
    todo mundo teria o efeito perverso de deixar um atacante encher o balde e
    a notificacao legitima da Cora levar 429 — ou seja, o pagamento entraria
    e a parcela nunca seria baixada.  Quem nao tem o segredo nao alcanca o
    balde de quem tem.

    Particionar por IP nao serviria: a API roda atras de proxy, entao todo
    request chega com o IP do proxy.  */
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    o.AddPolicy(CoraWebhookEndpoints.RateLimitPolicy, ctx =>
    {
        var segredo = ctx.RequestServices
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<FormaturasFlow.Api.Payments.CoraOptions>>()
            .Value.WebhookSecret;

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

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    o.SerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
});

builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                       | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
                       | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedHost;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await SeedRolesAsync(scope.ServiceProvider);
    await BootstrapAdminAsync(scope.ServiceProvider, app.Configuration);
}

app.UseExceptionHandler();
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
app.MapPagamentoWebhookEndpoints();

var v1 = app.MapGroup("/api/v1");
v1.MapTurmaEndpoints();
v1.MapAlunoEndpoints();
v1.MapContratoEndpoints();
v1.MapParcelaEndpoints();
v1.MapPagamentoEndpoints();
v1.MapCobrancaEndpoints();
v1.MapDespesaEndpoints();
v1.MapAgendaEndpoints();
v1.MapColaboradorEndpoints();
v1.MapPublicEndpoints();
v1.MapPaymentEndpoints();
v1.MapCoraWebhookEndpoints();
v1.MapCoraAdminEndpoints();

app.Run();

static async Task SeedRolesAsync(IServiceProvider sp)
{
    var roleMgr = sp.GetRequiredService<RoleManager<ApplicationRole>>();
    foreach (var r in new[] { Roles.SuperAdmin, Roles.Funcionario, Roles.Aluno })
        if (!await roleMgr.RoleExistsAsync(r))
            await roleMgr.CreateAsync(new ApplicationRole(r));
}

static async Task BootstrapAdminAsync(IServiceProvider sp, IConfiguration cfg)
{
    var email = cfg["Admin:BootstrapEmail"] ?? Environment.GetEnvironmentVariable("ADMIN_BOOTSTRAP_EMAIL");
    if (string.IsNullOrWhiteSpace(email)) return;

    var userMgr = sp.GetRequiredService<UserManager<ApplicationUser>>();
    var user = await userMgr.FindByEmailAsync(email);
    if (user is null) return;

    if (!await userMgr.IsInRoleAsync(user, Roles.SuperAdmin))
        await userMgr.AddToRoleAsync(user, Roles.SuperAdmin);
}

public partial class Program;
