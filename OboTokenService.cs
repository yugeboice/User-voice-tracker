using Microsoft.Identity.Client;

namespace LuminaSearchConsole
{
    /// <summary>
    /// OAuth 2.0 Authentication Service for Lumina API
    /// 
    /// Handles user authentication using MSAL (Microsoft Authentication Library).
    /// Implements token caching for better performance and user experience.
    /// 
    /// Authentication Flow:
    /// 1. Try silent token acquisition from cache
    /// 2. If cache miss or expired, prompt interactive browser login
    /// 3. Store token in persistent cache for future use
    /// </summary>
    public class OboTokenService
    {
        private readonly IPublicClientApplication _app;
        private readonly AzureAdConfiguration _azureAdConfig;
        private readonly LuminaConfiguration _luminaConfig;
        private readonly string _cacheFilePath;

        public OboTokenService(
            AzureAdConfiguration azureAdConfig,
            LuminaConfiguration luminaConfig)
        {
            _azureAdConfig = azureAdConfig;
            _luminaConfig = luminaConfig;
            
            // Store cache file path for SignOutAsync
            _cacheFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LuminaSearchConsole",
                _azureAdConfig.CacheFileName);
            
            var tokenCacheHelper = new TokenCacheHelper(_azureAdConfig.CacheFileName);
            
            // Build MSAL public client application
            _app = PublicClientApplicationBuilder
                .Create(_azureAdConfig.ClientId)
                .WithAuthority(new Uri(_azureAdConfig.Authority))
                .WithRedirectUri(_azureAdConfig.RedirectUri)
                .Build();
                
            // Enable persistent token caching to disk
            tokenCacheHelper.EnableSerialization(_app.UserTokenCache);
        }

        /// <summary>
        /// Acquire access token for Lumina API
        /// 
        /// Token Acquisition Strategy:
        /// 1. Silent acquisition: Try to get token from cache (fast, no user interaction)
        /// 2. Interactive acquisition: Show browser login if cache miss (requires user)
        /// </summary>
        /// <returns>Access token for calling Lumina APIs</returns>
        public async Task<string> GetUserTokenAsync()
        {
            try
            {
                // Step 1: Attempt silent token acquisition from cache
                var accounts = await _app.GetAccountsAsync();
                var firstAccount = accounts.FirstOrDefault();

                if (firstAccount != null)
                {
                    try
                    {
                        var result = await _app
                            .AcquireTokenSilent(new[] { _luminaConfig.ApiScopes }, firstAccount)
                            .ExecuteAsync();

                        return result.AccessToken;
                    }
                    catch (MsalUiRequiredException)
                    {
                        // Token expired or not in cache - need interactive login
                    }
                }

                // Step 2: Interactive login via browser
                // User will see account picker or login prompt
                var interactiveResult = await _app
                    .AcquireTokenInteractive(new[] { _luminaConfig.ApiScopes })
                    .WithPrompt(Prompt.SelectAccount)
                    .ExecuteAsync();

                return interactiveResult.AccessToken;
            }
            catch (Exception ex)
            {
                throw new Exception($"Authentication failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Sign out user and clear all cached tokens
        /// 
        /// This removes:
        /// 1. All accounts from MSAL cache
        /// 2. Persistent cache file from disk
        /// </summary>
        public async Task SignOutAsync()
        {
            // Remove all accounts from MSAL cache
            var accounts = await _app.GetAccountsAsync();
            foreach (var account in accounts)
            {
                await _app.RemoveAsync(account);
            }
            
            // Delete persistent cache file
            try
            {
                if (File.Exists(_cacheFilePath))
                {
                    File.Delete(_cacheFilePath);
                }
            }
            catch
            {
                // Ignore cache deletion errors
            }
        }
    }
    
    /// <summary>
    /// Token Cache Helper
    /// 
    /// Implements persistent token caching to local disk.
    /// This improves performance by avoiding repeated authentication.
    /// 
    /// Cache Location: %LocalAppData%\LuminaSearchConsole\{cacheFileName}
    /// </summary>
    public class TokenCacheHelper
    {
        private readonly string _cacheFilePath;

        public TokenCacheHelper(string cacheFileName)
        {
            _cacheFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LuminaSearchConsole",
                cacheFileName);
        }

        public void EnableSerialization(ITokenCache tokenCache)
        {
            // Hook into MSAL cache events
            tokenCache.SetBeforeAccess(BeforeAccessNotification);
            tokenCache.SetAfterAccess(AfterAccessNotification);
        }

        /// <summary>
        /// Load cached tokens from disk before MSAL accesses the cache
        /// </summary>
        private void BeforeAccessNotification(TokenCacheNotificationArgs args)
        {
            if (File.Exists(_cacheFilePath))
            {
                try
                {
                    var cacheData = File.ReadAllBytes(_cacheFilePath);
                    args.TokenCache.DeserializeMsalV3(cacheData);
                }
                catch
                {
                    // If cache is corrupted, delete it and start fresh
                    File.Delete(_cacheFilePath);
                }
            }
        }

        /// <summary>
        /// Save updated tokens to disk after MSAL modifies the cache
        /// </summary>
        private void AfterAccessNotification(TokenCacheNotificationArgs args)
        {
            // Only write to disk if cache was modified
            if (args.HasStateChanged)
            {
                try
                {
                    // Ensure directory exists
                    var directory = Path.GetDirectoryName(_cacheFilePath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    
                    var cacheData = args.TokenCache.SerializeMsalV3();
                    File.WriteAllBytes(_cacheFilePath, cacheData);
                }
                catch
                {
                    // Ignore cache write errors - authentication will still work
                }
            }
        }
    }
}