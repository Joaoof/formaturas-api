using System.Text;
using FormaturasFlow.Api.Asaas;
using FormaturasFlow.Api.Auth;
using FormaturasFlow.Api.Cora;
using FormaturasFlow.Api.Data;
using FormaturasFlow.Api.Endpoints;
using FormaturasFlow.Api.Pagamentos;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
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

builder.Services.AddHttpClient<AsaasClient>();

builder.Services.AddTransient<CoraHttpHandler>();
builder.Services.AddHttpClient<CoraClient>()
    .ConfigurePrimaryHttpMessageHandler<CoraHttpHandler>();

builder.Services.AddScoped<PagamentoService>();

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

builder.Services.AddProblemDetails();

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
v1.MapPagamentoEndpoints();
v1.MapCobrancaEndpoints();
v1.MapDespesaEndpoints();
v1.MapAgendaEndpoints();
v1.MapPublicEndpoints();

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
