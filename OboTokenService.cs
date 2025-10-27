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
        #region Configuration Constants
        
        // Azure AD Configuration
        // These values are specific to the Lumina API test environment
        private static readonly string TenantId = "72f988bf-86f1-41af-91ab-2d7cd011db47";
        private static readonly string ClientId = "63696678-8070-4259-91d9-292979db05c4";
        private static readonly string RedirectUri = "http://localhost:8400";
        
        // Lumina API Scope
        // This scope grants access to Lumina Search and Open APIs
        private static readonly string LuminaScope = "67f912ef-f692-43d3-9b97-3702aa2fd840/.default";
        
        #endregion

        private readonly IPublicClientApplication _app;

        public OboTokenService()
        {
            var tokenCacheHelper = new TokenCacheHelper();
            
            // Build MSAL public client application
            _app = PublicClientApplicationBuilder
                .Create(ClientId)
                .WithAuthority(new Uri($"https://login.microsoftonline.com/{TenantId}"))
                .WithRedirectUri(RedirectUri)
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
                            .AcquireTokenSilent(new[] { LuminaScope }, firstAccount)
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
                    .AcquireTokenInteractive(new[] { LuminaScope })
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
                var cacheFilePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "LuminaSearchConsole",
                    "msalcache.bin");
                    
                if (File.Exists(cacheFilePath))
                {
                    File.Delete(cacheFilePath);
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
    /// Cache Location: %LocalAppData%\LuminaSearchConsole\msalcache.bin
    /// </summary>
    public class TokenCacheHelper
    {
        private static readonly string CacheFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LuminaSearchConsole",
            "msalcache.bin");

        public void EnableSerialization(ITokenCache tokenCache)
        {
            // Hook into MSAL cache events
            tokenCache.SetBeforeAccess(BeforeAccessNotification);
            tokenCache.SetAfterAccess(AfterAccessNotification);
        }

        /// <summary>
        /// Load cached tokens from disk before MSAL accesses the cache
        /// </summary>
        /// <summary>
        /// Load cached tokens from disk before MSAL accesses the cache
        /// </summary>
        private void BeforeAccessNotification(TokenCacheNotificationArgs args)
        {
            if (File.Exists(CacheFilePath))
            {
                try
                {
                    var cacheData = File.ReadAllBytes(CacheFilePath);
                    args.TokenCache.DeserializeMsalV3(cacheData);
                }
                catch
                {
                    // If cache is corrupted, delete it and start fresh
                    File.Delete(CacheFilePath);
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
                    Directory.CreateDirectory(Path.GetDirectoryName(CacheFilePath));
                    
                    var cacheData = args.TokenCache.SerializeMsalV3();
                    File.WriteAllBytes(CacheFilePath, cacheData);
                }
                catch
                {
                    // Ignore cache write errors - authentication will still work
                }
            }
        }
    }
}