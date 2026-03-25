using Microsoft.Extensions.Configuration;
using Microsoft.Identity.Client;

namespace MinimalApiCall;

/// <summary>
/// Minimal token service for acquiring Lumina API access tokens via OBO flow.
/// </summary>
public class TokenService
{
    private readonly string _tenantId;
    private readonly string _clientId;
    private readonly string _redirectUri;
    private readonly string[] _scopes;
    private readonly string _cacheFilePath;
    
    private IPublicClientApplication? _app;
    private string? _cachedToken;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;

    public TokenService(IConfiguration config)
    {
        _tenantId = config["AzureAd:TenantId"] ?? throw new ArgumentException("AzureAd:TenantId required");
        _clientId = config["AzureAd:ClientId"] ?? throw new ArgumentException("AzureAd:ClientId required");
        _redirectUri = config["AzureAd:RedirectUri"] ?? "http://localhost";
        var apiScopes = config["LuminaConfiguration:ApiScopes"] ?? throw new ArgumentException("LuminaConfiguration:ApiScopes required");
        _scopes = new[] { apiScopes };
        _cacheFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MinimalApiCall", "msal_cache.bin");
    }

    public TokenService(string tenantId, string clientId, string redirectUri, string apiScopes)
    {
        _tenantId = tenantId;
        _clientId = clientId;
        _redirectUri = redirectUri;
        _scopes = new[] { apiScopes };
        _cacheFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MinimalApiCall", "msal_cache.bin");
    }

    /// <summary>
    /// Gets an access token for Lumina API. Uses cached token if valid, otherwise acquires new one.
    /// </summary>
    public async Task<string> GetTokenAsync()
    {
        // Return cached token if still valid
        if (!string.IsNullOrEmpty(_cachedToken) && DateTimeOffset.UtcNow < _tokenExpiry.AddMinutes(-5))
        {
            return _cachedToken;
        }

        _app ??= CreateApp();
        
        AuthenticationResult? result = null;
        var accounts = await _app.GetAccountsAsync();
        
        // Try silent authentication first (from cache)
        if (accounts.Any())
        {
            try
            {
                result = await _app.AcquireTokenSilent(_scopes, accounts.FirstOrDefault()).ExecuteAsync();
            }
            catch (MsalUiRequiredException) { }
        }

        // Fall back to interactive login
        result ??= await _app.AcquireTokenInteractive(_scopes)
            .WithPrompt(Prompt.SelectAccount)
            .ExecuteAsync();

        _cachedToken = result.AccessToken;
        _tokenExpiry = result.ExpiresOn;
        
        return _cachedToken;
    }

    private IPublicClientApplication CreateApp()
    {
        var app = PublicClientApplicationBuilder
            .Create(_clientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, _tenantId)
            .WithRedirectUri(_redirectUri)
            .Build();

        // Enable token cache persistence
        Directory.CreateDirectory(Path.GetDirectoryName(_cacheFilePath)!);
        app.UserTokenCache.SetBeforeAccess(args =>
        {
            if (File.Exists(_cacheFilePath))
                args.TokenCache.DeserializeMsalV3(File.ReadAllBytes(_cacheFilePath));
        });
        app.UserTokenCache.SetAfterAccess(args =>
        {
            if (args.HasStateChanged)
                File.WriteAllBytes(_cacheFilePath, args.TokenCache.SerializeMsalV3());
        });

        return app;
    }
}
