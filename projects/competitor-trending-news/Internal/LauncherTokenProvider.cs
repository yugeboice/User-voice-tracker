using Microsoft.Extensions.Configuration;

namespace MinimalApiCall;

/// <summary>
/// Token provider that gets tokens from the centralized Launcher auth service.
/// This allows unified authentication across all projects.
/// </summary>
public class LauncherTokenProvider
{
    private readonly string _launcherUrl;
    private readonly HttpClient _httpClient;
    private string? _cachedToken;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;

    public LauncherTokenProvider(IConfiguration config)
    {
        _launcherUrl = config["LauncherUrl"] ?? "http://localhost:8400";
        _httpClient = new HttpClient();
    }

    /// <summary>
    /// Gets an access token from Launcher. Uses cached token if valid, otherwise fetches from Launcher.
    /// </summary>
    public async Task<string> GetTokenAsync()
    {
        // Return cached token if still valid
        if (!string.IsNullOrEmpty(_cachedToken) && DateTimeOffset.UtcNow < _tokenExpiry.AddMinutes(-5))
        {
            return _cachedToken;
        }

        try
        {
            var response = await _httpClient.GetAsync($"{_launcherUrl}/api/auth/token");
            
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to get token from Launcher: {response.StatusCode}. {error}");
            }

            var result = await response.Content.ReadFromJsonAsync<TokenResponse>();
            if (result == null || string.IsNullOrEmpty(result.Token))
            {
                throw new Exception("Invalid token response from Launcher");
            }

            _cachedToken = result.Token;
            _tokenExpiry = DateTimeOffset.UtcNow.AddHours(1); // Assume 1 hour validity
            
            return _cachedToken;
        }
        catch (HttpRequestException ex)
        {
            throw new Exception($"Cannot connect to Launcher at {_launcherUrl}. Please ensure Launcher is running and you are logged in.", ex);
        }
    }

    private record TokenResponse(string Token);
}
