using Microsoft.Extensions.Configuration;
using Microsoft.Identity.Client;

namespace Launcher.Internal;

/// <summary>
/// Unified token service for acquiring Lumina API access tokens via OBO flow.
/// This service runs in the Launcher and provides tokens to all child projects.
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
        _redirectUri = config["AzureAd:RedirectUri"] ?? "http://localhost"; // No port - MSAL will choose random port
        var apiScopes = config["LuminaConfiguration:ApiScopes"] ?? throw new ArgumentException("LuminaConfiguration:ApiScopes required");
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
            .WithUseEmbeddedWebView(false)
            .WithSystemWebViewOptions(new SystemWebViewOptions
            {
                HtmlMessageSuccess = @"
                    <html>
                    <head><title>Authentication Complete</title></head>
                    <body style='font-family: system-ui; display: flex; align-items: center; justify-content: center; height: 100vh; margin: 0; background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white;'>
                        <div style='text-align: center; background: rgba(255,255,255,0.1); padding: 40px; border-radius: 12px;'>
                            <div style='font-size: 60px; margin-bottom: 20px;'>✓</div>
                            <h1 style='margin: 0 0 10px; font-size: 28px;'>Authentication Complete!</h1>
                            <p style='margin: 0; opacity: 0.9;'>You can close this window and return to Lumina Lab.</p>
                        </div>
                        <script>setTimeout(() => window.close(), 2000);</script>
                    </body>
                    </html>
                "
            })
            .ExecuteAsync();

        _cachedToken = result.AccessToken;
        _tokenExpiry = result.ExpiresOn;
        
        return _cachedToken;
    }

    /// <summary>
    /// Checks if there is a valid cached token without triggering interactive login
    /// </summary>
    public bool HasValidToken()
    {
        return !string.IsNullOrEmpty(_cachedToken) && DateTimeOffset.UtcNow < _tokenExpiry.AddMinutes(-5);
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
