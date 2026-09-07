using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FormaturasFlow.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FormaturasFlow.Api.Auth;

public class JwtTokenService(
    IOptions<JwtOptions> options,
    UserManager<ApplicationUser> userManager,
    AppDbContext db)
{
    private readonly JwtOptions _opt = options.Value;

    public async Task<(string Token, DateTimeOffset ExpiresAt)> CreateAccessTokenAsync(ApplicationUser user)
    {
        var roles = await userManager.GetRolesAsync(user);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new("nome", user.NomeCompleto),
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTimeOffset.UtcNow.AddMinutes(_opt.AccessTokenMinutes);

        var token = new JwtSecurityToken(
            issuer: _opt.Issuer,
            audience: _opt.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expires.UtcDateTime,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public async Task<(string Raw, DateTimeOffset ExpiresAt)> CreateRefreshTokenAsync(
        Guid userId, string? ip, string? userAgent, CancellationToken ct)
    {
        var raw = GenerateSecureToken();
        var hash = HashToken(raw);
        var expires = DateTimeOffset.UtcNow.AddDays(_opt.RefreshTokenDays);

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = userId,
            TokenHash = hash,
            ExpiresAt = expires,
            IpCriacao = ip,
            UserAgent = userAgent
        });
        await db.SaveChangesAsync(ct);
        return (raw, expires);
    }

    public async Task<RefreshToken?> ValidateAndRotateRefreshTokenAsync(string rawToken, string? ip, string? userAgent, CancellationToken ct)
    {
        var hash = HashToken(rawToken);
        var existing = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .FirstOrDefaultAsync(db.RefreshTokens.Where(x => x.TokenHash == hash), ct);
        if (existing is null || !existing.Ativo) return null;

        existing.RevogadoEm = DateTimeOffset.UtcNow;
        existing.RevogadoMotivo = "rotated";

        var newRaw = GenerateSecureToken();
        var newHash = HashToken(newRaw);
        var newExpires = DateTimeOffset.UtcNow.AddDays(_opt.RefreshTokenDays);
        var novo = new RefreshToken
        {
            UserId = existing.UserId,
            TokenHash = newHash,
            ExpiresAt = newExpires,
            IpCriacao = ip,
            UserAgent = userAgent
        };
        db.RefreshTokens.Add(novo);
        existing.SubstituidoPorId = novo.Id;
        await db.SaveChangesAsync(ct);

        return new RefreshToken
        {
            Id = novo.Id,
            UserId = novo.UserId,
            TokenHash = newRaw,
            ExpiresAt = newExpires,
            CriadoEm = novo.CriadoEm
        };
    }

    public async Task RevokeRefreshTokenAsync(string rawToken, string motivo, CancellationToken ct)
    {
        var hash = HashToken(rawToken);
        var existing = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .FirstOrDefaultAsync(db.RefreshTokens.Where(x => x.TokenHash == hash), ct);
        if (existing is null || existing.RevogadoEm is not null) return;
        existing.RevogadoEm = DateTimeOffset.UtcNow;
        existing.RevogadoMotivo = motivo;
        await db.SaveChangesAsync(ct);
    }

    public async Task<(string Raw, ServiceToken Entity)> CreateServiceTokenAsync(
        string nome, Guid criadoPor, int? diasCustom, string? escopo, CancellationToken ct)
    {
        var dias = Math.Min(diasCustom ?? _opt.ServiceTokenDaysDefault, _opt.ServiceTokenDaysMax);
        var user = await userManager.FindByIdAsync(criadoPor.ToString())
            ?? throw new InvalidOperationException("Usuario criador nao existe.");
        var roles = await userManager.GetRolesAsync(user);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new("nome", user.NomeCompleto),
            new("service_token", nome),
        };
        if (!string.IsNullOrEmpty(escopo)) claims.Add(new("scope", escopo));
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTimeOffset.UtcNow.AddDays(dias);

        var jwt = new JwtSecurityToken(
            issuer: _opt.Issuer,
            audience: _opt.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expires.UtcDateTime,
            signingCredentials: creds);
        var raw = new JwtSecurityTokenHandler().WriteToken(jwt);

        var entity = new ServiceToken
        {
            Nome = nome,
            TokenHash = HashToken(raw),
            CriadoPorUserId = criadoPor,
            ExpiresAt = expires,
            Escopo = escopo
        };
        db.ServiceTokens.Add(entity);
        await db.SaveChangesAsync(ct);
        return (raw, entity);
    }

    private static string GenerateSecureToken()
    {
        var buffer = new byte[64];
        RandomNumberGenerator.Fill(buffer);
        return Convert.ToBase64String(buffer).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static string HashToken(string raw)
    {
        var bytes = Encoding.UTF8.GetBytes(raw);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
