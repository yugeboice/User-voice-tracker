namespace LuminaSearchConsole
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("🚀 Lumina API Search Console Demo");
            Console.WriteLine("==================================");
            Console.WriteLine();

            try
            {
                // Step 1: User authentication to get token
                Console.WriteLine("📋 Step 1: User Authentication");
                Console.WriteLine("Opening browser for login...");
                Console.WriteLine();

                var oboTokenService = new OboTokenService();
                var token = await oboTokenService.GetUserTokenAsync();
                Console.WriteLine($"✅ Token acquired successfully!");
                Console.WriteLine($"🔑 Token preview: {token[..Math.Min(50, token.Length)]}...");
                Console.WriteLine();

                // Step 2 & 3: Initialize search service and execute search
                Console.WriteLine("📋 Step 2 & 3: Initialize search service and execute search");
                var searchService = new LuminaSearchService(token);
                Console.WriteLine("✅ LuminaSearchService initialized successfully");
                Console.WriteLine();

                // Execute basic search (following the Step 3 example from documentation)
                await searchService.ExecuteBasicSearchAsync();

                Console.WriteLine();
                Console.WriteLine("🎉 Demo completed!");

            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ An error occurred: {ex.Message}");
                Console.WriteLine($"Details: {ex}");
            }

            Console.WriteLine();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }
}