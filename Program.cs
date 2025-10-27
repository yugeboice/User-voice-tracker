namespace LuminaSearchConsole
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("🚀 Lumina API 搜索控制台演示");
            Console.WriteLine("=============================");
            Console.WriteLine();

            try
            {
                // Step 1: 用户身份验证获取token
                Console.WriteLine("📋 Step 1: 用户身份验证");
                Console.WriteLine("即将打开浏览器进行登录...");
                Console.WriteLine();

                var oboTokenService = new OboTokenService();
                var token = await oboTokenService.GetUserTokenAsync();
                Console.WriteLine($"✅ Token获取成功!");
                Console.WriteLine($"🔑 Token预览: {token[..Math.Min(50, token.Length)]}...");
                Console.WriteLine();

                // Step 2 & 3: 初始化搜索服务并执行搜索
                Console.WriteLine("📋 Step 2 & 3: 初始化搜索服务并执行搜索");
                var searchService = new LuminaSearchService(token);
                Console.WriteLine("✅ LuminaSearchService 初始化完成");
                Console.WriteLine();

                // 执行基础搜索 (按照文档Step 3示例)
                await searchService.ExecuteBasicSearchAsync();

                Console.WriteLine();
                Console.WriteLine("🎉 演示完成!");

            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 发生错误: {ex.Message}");
                Console.WriteLine($"详细信息: {ex}");
            }

            Console.WriteLine();
            Console.WriteLine("按任意键退出...");
            Console.ReadKey();
        }
    }
}