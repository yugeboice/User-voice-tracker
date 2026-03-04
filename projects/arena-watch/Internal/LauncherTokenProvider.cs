namespace MinimalApiCall.Internal;

public class LauncherTokenProvider
{
    private readonly string _launcherUrl;
    private readonly HttpClient _httpClient;
    private string? _cachedToken;
    private DateTimeOffset _tokenExpiry;

    public LauncherTokenProvider(IConfiguration config)
    {
        _launcherUrl = config["LauncherUrl"] ?? "http://localhost:8400";
        _httpClient = new HttpClient();
    }

    public async Task<string> GetTokenAsync()
    {
        if (!string.IsNullOrEmpty(_cachedToken) && DateTimeOffset.UtcNow < _tokenExpiry.AddMinutes(-5))
            return _cachedToken;

        var response = await _httpClient.GetAsync($"{_launcherUrl}/api/auth/token");
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<TokenResponse>();
        
        _cachedToken = result!.Token;
        _tokenExpiry = result.ExpiresOn;
        return _cachedToken;
    }

    private record TokenResponse(string Token, DateTimeOffset ExpiresOn);
}
