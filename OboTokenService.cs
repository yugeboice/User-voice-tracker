using Microsoft.Identity.Client;

namespace LuminaSearchConsole
{
    /// <summary>
    /// OBO Token Service - Handles authentication and token management
    /// </summary>
    public class OboTokenService
    {
        // Configuration
        private static readonly string TenantId = "72f988bf-86f1-41af-91ab-2d7cd011db47";
        private static readonly string ClientId = "63696678-8070-4259-91d9-292979db05c4";
        private static readonly string RedirectUri = "http://localhost:8400";
        private static readonly string LuminaScope = "67f912ef-f692-43d3-9b97-3702aa2fd840/.default";

        private readonly IPublicClientApplication _app;

        public OboTokenService()
        {
            var tokenCacheHelper = new TokenCacheHelper();
            
            _app = PublicClientApplicationBuilder
                .Create(ClientId)
                .WithAuthority(new Uri($"https://login.microsoftonline.com/{TenantId}"))
                .WithRedirectUri(RedirectUri)
                .Build();
                
            // Enable token cache persistence
            tokenCacheHelper.EnableSerialization(_app.UserTokenCache);
        }

        /// <summary>
        /// Get user token (login through browser)
        /// </summary>
        public async Task<string> GetUserTokenAsync()
        {
            try
            {
                // First try to get token silently
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
                        // Token expired, need interactive login
                    }
                }

                // Interactive login - use SelectAccount to show account picker
                var interactiveResult = await _app
                    .AcquireTokenInteractive(new[] { LuminaScope })
                    .WithPrompt(Prompt.SelectAccount) // Show account selection instead of forcing login
                    .ExecuteAsync();

                return interactiveResult.AccessToken;
            }
            catch (Exception ex)
            {
                throw new Exception($"Authentication failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Clear all cached tokens
        /// </summary>
        public async Task SignOutAsync()
        {
            var accounts = await _app.GetAccountsAsync();
            foreach (var account in accounts)
            {
                await _app.RemoveAsync(account);
            }
            
            // Also clear the persistent cache file
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
    /// Helper class for token cache persistence
    /// </summary>
    public class TokenCacheHelper
    {
        private static readonly string CacheFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LuminaSearchConsole",
            "msalcache.bin");

        public void EnableSerialization(ITokenCache tokenCache)
        {
            tokenCache.SetBeforeAccess(BeforeAccessNotification);
            tokenCache.SetAfterAccess(AfterAccessNotification);
        }

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
                    // If cache is corrupted, delete it
                    File.Delete(CacheFilePath);
                }
            }
        }

        private void AfterAccessNotification(TokenCacheNotificationArgs args)
        {
            if (args.HasStateChanged)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(CacheFilePath));
                    var cacheData = args.TokenCache.SerializeMsalV3();
                    File.WriteAllBytes(CacheFilePath, cacheData);
                }
                catch
                {
                    // Ignore cache write errors
                }
            }
        }
    }
}