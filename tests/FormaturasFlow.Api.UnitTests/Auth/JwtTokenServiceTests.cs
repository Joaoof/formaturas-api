using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using FormaturasFlow.Api.Auth;
using FormaturasFlow.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace FormaturasFlow.Api.UnitTests.Auth;

public class JwtTokenServiceTests
{
    private static (JwtTokenService Sut, IServiceProvider Sp) Build(JwtOptions opt)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services
            .AddIdentityCore<ApplicationUser>()
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<AppDbContext>();

        var sp = services.BuildServiceProvider();
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        return (new JwtTokenService(Options.Create(opt), users), sp);
    }

    private static JwtOptions ValidOpts() => new()
    {
        Issuer = "https://api.test",
        Audience = "test-audience",
        Key = "chave-hmac-forte-com-mais-de-32-bytes-obrigatorio-aqui!!",
        AccessTokenMinutes = 30
    };

    [Fact]
    public async Task Gera_Token_Assinado_Com_Claims_Do_Usuario()
    {
        var opt = ValidOpts();
        var (sut, sp) = Build(opt);
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "alice@ex.com",
            Email = "alice@ex.com",
            NomeCompleto = "Alice"
        };
        (await users.CreateAsync(user, "SenhaForte1!")).Succeeded.Should().BeTrue();

        var (raw, expires) = await sut.CreateAccessTokenAsync(user);

        raw.Should().NotBeNullOrWhiteSpace();
        expires.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(29));
        expires.Should().BeBefore(DateTimeOffset.UtcNow.AddMinutes(31));

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(raw);

        jwt.Issuer.Should().Be(opt.Issuer);
        jwt.Audiences.Should().ContainSingle(a => a == opt.Audience);
        jwt.Claims.Should().ContainSingle(c => c.Type == JwtRegisteredClaimNames.Sub && c.Value == user.Id.ToString());
        jwt.Claims.Should().ContainSingle(c => c.Type == JwtRegisteredClaimNames.Email && c.Value == user.Email);
        jwt.Claims.Should().ContainSingle(c => c.Type == "nome" && c.Value == user.NomeCompleto);
        jwt.Claims.Should().ContainSingle(c => c.Type == JwtRegisteredClaimNames.Jti);
    }

    [Fact]
    public async Task Inclui_Todas_As_Roles_Como_Claim_Role()
    {
        var (sut, sp) = Build(ValidOpts());
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var roleMgr = sp.GetRequiredService<RoleManager<ApplicationRole>>();

        foreach (var r in new[] { Roles.SuperAdmin, Roles.Funcionario })
            await roleMgr.CreateAsync(new ApplicationRole(r));

        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "bob@ex.com", Email = "bob@ex.com", NomeCompleto = "Bob" };
        (await users.CreateAsync(user, "SenhaForte1!")).Succeeded.Should().BeTrue();
        await users.AddToRolesAsync(user, [Roles.SuperAdmin, Roles.Funcionario]);

        var (raw, _) = await sut.CreateAccessTokenAsync(user);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(raw);
        var roleClaims = jwt.Claims
            .Where(c => c.Type == ClaimTypes.Role)
            .Select(c => c.Value)
            .ToArray();

        roleClaims.Should().BeEquivalentTo(new[] { Roles.SuperAdmin, Roles.Funcionario });
    }
}
