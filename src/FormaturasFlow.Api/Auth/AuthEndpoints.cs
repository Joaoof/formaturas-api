using System.ComponentModel.DataAnnotations;
using FormaturasFlow.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FormaturasFlow.Api.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/register", RegisterAsync)
            .WithSummary("Cria uma nova conta e emite access + refresh token")
            .WithDescription("O primeiro cadastro do sistema recebe o papel `super_admin` automaticamente. Os demais recebem `aluno`. Senha deve ter no mínimo 8 caracteres.")
            .Produces<TokenResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem();

        group.MapPost("/login", LoginAsync)
            .WithSummary("Autentica um usuário existente e retorna access + refresh token")
            .WithDescription("Retorna um access token de `Jwt:AccessTokenMinutes` minutos e um refresh token de `Jwt:RefreshTokenDays` dias. Depois de 5 tentativas falhas, o usuário é bloqueado temporariamente.")
            .Produces<TokenResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/refresh", RefreshAsync)
            .WithSummary("Renova access token usando refresh token")
            .WithDescription("Body: `{ \"refreshToken\": \"...\" }`. O refresh token antigo é revogado e um novo é emitido (rotação).")
            .Produces<TokenResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout", LogoutAsync)
            .RequireAuthorization()
            .WithSummary("Revoga o refresh token atual")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/me", MeAsync)
            .RequireAuthorization()
            .WithSummary("Retorna dados do usuário logado")
            .Produces<MeResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/service-tokens", CreateServiceTokenAsync)
            .RequireAuthorization(p => p.RequireRole(Roles.SuperAdmin))
            .WithSummary("Cria um service token JWT de longa duração (padrão 90 dias)")
            .WithDescription("Requer role `super_admin`. Body: `{ \"nome\": \"vercel-serverfns\", \"dias\": 90, \"escopo\": \"cobrancas.write\" }`. Use pra service accounts server-to-server (Vercel serverfns, cron, webhook custom).")
            .Produces<ServiceTokenResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status403Forbidden);

        return app;
    }

    public record MeResponse(Guid Id, string Email, string NomeCompleto, IEnumerable<string> Roles);

    public record RegisterRequest(
        [Required, EmailAddress] string Email,
        [Required, MinLength(8)] string Password,
        [Required, MinLength(3)] string NomeCompleto);

    public record LoginRequest(
        [Required, EmailAddress] string Email,
        [Required] string Password);

    public record RefreshRequest([Required] string RefreshToken);

    public record TokenResponse(
        string AccessToken,
        DateTimeOffset ExpiresAt,
        string RefreshToken,
        DateTimeOffset RefreshTokenExpiresAt,
        string Email,
        string NomeCompleto,
        IEnumerable<string> Roles);

    public record ServiceTokenRequest([Required] string Nome, int? Dias, string? Escopo);
    public record ServiceTokenResponse(Guid Id, string Nome, string Token, DateTimeOffset ExpiresAt, string? Escopo);

    private static async Task<IResult> RegisterAsync(
        [FromBody] RegisterRequest req,
        HttpContext ctx,
        UserManager<ApplicationUser> users,
        JwtTokenService tokens,
        CancellationToken ct)
    {
        var user = new ApplicationUser
        {
            UserName = req.Email,
            Email = req.Email,
            NomeCompleto = req.NomeCompleto
        };

        var result = await users.CreateAsync(user, req.Password);
        if (!result.Succeeded)
            return Results.ValidationProblem(result.Errors.ToDictionary(
                e => e.Code, e => new[] { e.Description }));

        var isFirst = users.Users.Count() == 1;
        await users.AddToRoleAsync(user, isFirst ? Roles.SuperAdmin : Roles.Aluno);

        return Results.Ok(await BuildTokensAsync(user, users, tokens, ctx, ct));
    }

    private static async Task<IResult> LoginAsync(
        [FromBody] LoginRequest req,
        HttpContext ctx,
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn,
        JwtTokenService tokens,
        CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(req.Email);
        if (user is null) return Results.Unauthorized();

        var check = await signIn.CheckPasswordSignInAsync(user, req.Password, lockoutOnFailure: true);
        if (!check.Succeeded) return Results.Unauthorized();

        return Results.Ok(await BuildTokensAsync(user, users, tokens, ctx, ct));
    }

    private static async Task<IResult> RefreshAsync(
        [FromBody] RefreshRequest req,
        HttpContext ctx,
        UserManager<ApplicationUser> users,
        JwtTokenService tokens,
        CancellationToken ct)
    {
        var ip = ctx.Connection.RemoteIpAddress?.ToString();
        var ua = ctx.Request.Headers.UserAgent.ToString();
        var rotated = await tokens.ValidateAndRotateRefreshTokenAsync(req.RefreshToken, ip, ua, ct);
        if (rotated is null) return Results.Unauthorized();

        var user = await users.FindByIdAsync(rotated.UserId.ToString());
        if (user is null) return Results.Unauthorized();

        var (access, accessExp) = await tokens.CreateAccessTokenAsync(user);
        var roles = await users.GetRolesAsync(user);

        return Results.Ok(new TokenResponse(
            access, accessExp,
            rotated.TokenHash, rotated.ExpiresAt,
            user.Email!, user.NomeCompleto, roles));
    }

    private static async Task<IResult> LogoutAsync(
        [FromBody] RefreshRequest req,
        JwtTokenService tokens,
        CancellationToken ct)
    {
        await tokens.RevokeRefreshTokenAsync(req.RefreshToken, "logout", ct);
        return Results.NoContent();
    }

    private static async Task<IResult> MeAsync(
        HttpContext ctx,
        UserManager<ApplicationUser> users)
    {
        var user = await users.GetUserAsync(ctx.User);
        if (user is null) return Results.Unauthorized();

        var roles = await users.GetRolesAsync(user);
        return Results.Ok(new MeResponse(user.Id, user.Email!, user.NomeCompleto, roles));
    }

    private static async Task<IResult> CreateServiceTokenAsync(
        [FromBody] ServiceTokenRequest req,
        HttpContext ctx,
        UserManager<ApplicationUser> users,
        JwtTokenService tokens,
        CancellationToken ct)
    {
        var caller = await users.GetUserAsync(ctx.User);
        if (caller is null) return Results.Unauthorized();

        var (raw, entity) = await tokens.CreateServiceTokenAsync(req.Nome, caller.Id, req.Dias, req.Escopo, ct);
        return Results.Created($"/auth/service-tokens/{entity.Id}",
            new ServiceTokenResponse(entity.Id, entity.Nome, raw, entity.ExpiresAt, entity.Escopo));
    }

    private static async Task<TokenResponse> BuildTokensAsync(
        ApplicationUser user,
        UserManager<ApplicationUser> users,
        JwtTokenService tokens,
        HttpContext ctx,
        CancellationToken ct)
    {
        var (access, accessExp) = await tokens.CreateAccessTokenAsync(user);
        var (refresh, refreshExp) = await tokens.CreateRefreshTokenAsync(
            user.Id,
            ctx.Connection.RemoteIpAddress?.ToString(),
            ctx.Request.Headers.UserAgent.ToString(),
            ct);
        var roles = await users.GetRolesAsync(user);
        return new TokenResponse(access, accessExp, refresh, refreshExp, user.Email!, user.NomeCompleto, roles);
    }
}
