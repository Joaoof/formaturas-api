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

        group.MapPost("/register", RegisterAsync);
        group.MapPost("/login", LoginAsync);
        group.MapGet("/me", MeAsync).RequireAuthorization();

        return app;
    }

    public record RegisterRequest(
        [Required, EmailAddress] string Email,
        [Required, MinLength(8)] string Password,
        [Required, MinLength(3)] string NomeCompleto);

    public record LoginRequest(
        [Required, EmailAddress] string Email,
        [Required] string Password);

    public record TokenResponse(string AccessToken, DateTimeOffset ExpiresAt, string Email, string NomeCompleto, IEnumerable<string> Roles);

    private static async Task<IResult> RegisterAsync(
        [FromBody] RegisterRequest req,
        UserManager<ApplicationUser> users,
        JwtTokenService tokens)
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

        var (token, expires) = await tokens.CreateAccessTokenAsync(user);
        var userRoles = await users.GetRolesAsync(user);
        return Results.Ok(new TokenResponse(token, expires, user.Email!, user.NomeCompleto, userRoles));
    }

    private static async Task<IResult> LoginAsync(
        [FromBody] LoginRequest req,
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn,
        JwtTokenService tokens)
    {
        var user = await users.FindByEmailAsync(req.Email);
        if (user is null) return Results.Unauthorized();

        var check = await signIn.CheckPasswordSignInAsync(user, req.Password, lockoutOnFailure: true);
        if (!check.Succeeded) return Results.Unauthorized();

        var (token, expires) = await tokens.CreateAccessTokenAsync(user);
        var userRoles = await users.GetRolesAsync(user);
        return Results.Ok(new TokenResponse(token, expires, user.Email!, user.NomeCompleto, userRoles));
    }

    private static async Task<IResult> MeAsync(
        HttpContext ctx,
        UserManager<ApplicationUser> users)
    {
        var user = await users.GetUserAsync(ctx.User);
        if (user is null) return Results.Unauthorized();

        var roles = await users.GetRolesAsync(user);
        return Results.Ok(new { user.Id, user.Email, user.NomeCompleto, Roles = roles });
    }
}
