using Microsoft.Identity.Client;

namespace LuminaSearchConsole
{
    /// <summary>
    /// OBO Token 获取服务 - 专门处理身份验证和token管理
    /// </summary>
    public class OboTokenService
    {
        // 配置信息
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
        /// 获取用户token (在浏览器中登录)
        /// </summary>
        public async Task<string> GetUserTokenAsync()
        {
            try
            {
                // 首先尝试静默获取token
                var accounts = await _app.GetAccountsAsync();
                var firstAccount = accounts.FirstOrDefault();

                if (firstAccount != null)
                {
                    Console.WriteLine("尝试使用缓存的token...");
                    try
                    {
                        var result = await _app
                            .AcquireTokenSilent(new[] { LuminaScope }, firstAccount)
                            .ExecuteAsync();

                        Console.WriteLine($"✅ 使用缓存token成功 (用户: {result.Account?.Username})");
                        return result.AccessToken;
                    }
                    catch (MsalUiRequiredException)
                    {
                        Console.WriteLine("缓存token已过期，需要重新登录");
                    }
                }

                // 交互式登录
                Console.WriteLine("正在打开浏览器进行登录...");
                var interactiveResult = await _app
                    .AcquireTokenInteractive(new[] { LuminaScope })
                    .WithPrompt(Prompt.SelectAccount)
                    .ExecuteAsync();

                Console.WriteLine($"✅ 登录成功 (用户: {interactiveResult.Account?.Username})");
                return interactiveResult.AccessToken;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 身份验证失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 清除所有缓存的token
        /// </summary>
        public async Task SignOutAsync()
        {
            var accounts = await _app.GetAccountsAsync();
            foreach (var account in accounts)
            {
                await _app.RemoveAsync(account);
            }
            Console.WriteLine("✅ 已清除所有缓存token");
        }
    }
}