using System.Text;
using FormaturasFlow.Api.Auth;
using FormaturasFlow.Api.Data;
using FormaturasFlow.Api.Efi;
using FormaturasFlow.Api.Endpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<EfiOptions>(builder.Configuration.GetSection(EfiOptions.SectionName));

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

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"])
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

builder.Services.AddProblemDetails();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await SeedRolesAsync(scope.ServiceProvider);
}

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

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

app.Run();

static async Task SeedRolesAsync(IServiceProvider sp)
{
    var roleMgr = sp.GetRequiredService<RoleManager<ApplicationRole>>();
    foreach (var r in new[] { Roles.SuperAdmin, Roles.Funcionario, Roles.Aluno })
        if (!await roleMgr.RoleExistsAsync(r))
            await roleMgr.CreateAsync(new ApplicationRole(r));
}

public partial class Program;
