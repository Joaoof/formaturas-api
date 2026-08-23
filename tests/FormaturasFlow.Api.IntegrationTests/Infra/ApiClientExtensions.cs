using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace FormaturasFlow.Api.IntegrationTests.Infra;

public static class ApiClientExtensions
{
    public record TokenResponse(string AccessToken, DateTimeOffset ExpiresAt, string Email, string NomeCompleto, IEnumerable<string> Roles);

    public static async Task<TokenResponse> RegisterAsync(this HttpClient http, string email, string password, string nome)
    {
        var resp = await http.PostAsJsonAsync("/auth/register", new { email, password, nomeCompleto = nome });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<TokenResponse>())!;
    }

    public static async Task<TokenResponse> LoginAsync(this HttpClient http, string email, string password)
    {
        var resp = await http.PostAsJsonAsync("/auth/login", new { email, password });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<TokenResponse>())!;
    }

    public static HttpClient WithToken(this HttpClient http, string token)
    {
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }
}
