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
            _app = PublicClientApplicationBuilder
                .Create(ClientId)
                .WithAuthority(new Uri($"https://login.microsoftonline.com/{TenantId}"))
                .WithRedirectUri(RedirectUri)
                .Build();
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
                    Console.WriteLine("Attempting to use cached token...");
                    try
                    {
                        var result = await _app
                            .AcquireTokenSilent(new[] { LuminaScope }, firstAccount)
                            .ExecuteAsync();

                        Console.WriteLine($"✅ Successfully used cached token (User: {result.Account?.Username})");
                        return result.AccessToken;
                    }
                    catch (MsalUiRequiredException)
                    {
                        Console.WriteLine("Cached token expired, re-login required");
                    }
                }

                // Interactive login
                Console.WriteLine("Opening browser for login...");
                var interactiveResult = await _app
                    .AcquireTokenInteractive(new[] { LuminaScope })
                    .WithPrompt(Prompt.SelectAccount)
                    .ExecuteAsync();

                Console.WriteLine($"✅ Login successful (User: {interactiveResult.Account?.Username})");
                return interactiveResult.AccessToken;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Authentication failed: {ex.Message}");
                throw;
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
            Console.WriteLine("✅ All cached tokens cleared");
        }
    }
}