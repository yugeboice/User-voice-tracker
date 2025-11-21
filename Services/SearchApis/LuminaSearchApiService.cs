using LuminaSearchConsole.Models;

namespace LuminaSearchConsole.Services.SearchApis
{
    /// <summary>
    /// Lumina Search API - Demonstrates POST /api/sonicberry/search
    /// 
    /// API Endpoint: POST /api/sonicberry/search
    /// Purpose: Web search with batch processing capabilities
    /// 
    /// UI Integration:
    /// - Main search box at top of page
    /// - Results display in "📊 Stock Results" and "📰 Latest News" sections
    /// 
    /// How to try:
    /// 1. Enter company name (e.g., "Microsoft") in search box
    /// 2. Click "🔍 Search" button
    /// 3. View stock prices and news in the results section below
    /// 
    /// Frontend code: wwwroot/js/search.js → handleSearchSubmit()
    /// </summary>
    public class LuminaSearchApiService
    {
        private readonly LuminaSearchService _luminaSearchService;
        private readonly ApiLogService _apiLogService;
        private readonly ILogger<LuminaSearchApiService> _logger;

        public LuminaSearchApiService(
            LuminaSearchService luminaSearchService,
            ApiLogService apiLogService,
            ILogger<LuminaSearchApiService> logger)
        {
            _luminaSearchService = luminaSearchService;
            _apiLogService = apiLogService;
            _logger = logger;
        }

        /// <summary>
        /// Execute Batch Search for company information
        /// 
        /// Demonstrates: Batch search pattern (multiple queries in one request)
        /// Example: Searching "Microsoft" creates 2 queries:
        ///   - "Microsoft stock price"
        ///   - "Microsoft latest news"
        /// 
        /// Frontend trigger: Form submit in main search box
        /// Results display: Stock section + News section
        /// </summary>
        public async Task<BatchSearchResult> ExecuteBatchSearchAsync(string companyName, int topResults)
        {
            _apiLogService.AddLog("Lumina Search API", "POST /api/sonicberry/search", 
                $"Parameters\n" +
                $"{{\n" +
                $"  \"requests\": [\n" +
                $"    {{ \"q\": \"{companyName} stock price\", \"topN\": {topResults}, \"source\": \"WebWithBing\", \"recency\": 7 }},\n" +
                $"    {{ \"q\": \"{companyName} latest news\", \"topN\": {topResults}, \"source\": \"WebWithBing\", \"recency\": 7 }}\n" +
                $"  ]\n" +
                $"}}");
            
            var startTime = DateTime.Now;
            var batchResult = await _luminaSearchService.ExecuteBatchCompanySearchAsync(companyName, topResults);
            var duration = (DateTime.Now - startTime).TotalMilliseconds;
            
            // Build result preview
            var stockPreview = batchResult.StockResults.Count > 0 && batchResult.StockResults[0].Title != null ? 
                $"{batchResult.StockResults[0].Title.Substring(0, Math.Min(50, batchResult.StockResults[0].Title.Length))}..." : "(no results)";
            var newsPreview = batchResult.NewsResults.Count > 0 && batchResult.NewsResults[0].Title != null ? 
                $"{batchResult.NewsResults[0].Title.Substring(0, Math.Min(50, batchResult.NewsResults[0].Title.Length))}..." : "(no results)";
            
            _apiLogService.AddLog("Lumina Search API", "POST /api/sonicberry/search", 
                $"Result\n" +
                $"{{\n" +
                $"  \"results_count\": {batchResult.StockResults.Count + batchResult.NewsResults.Count},\n" +
                $"  \"stock_results\": {batchResult.StockResults.Count},\n" +
                $"  \"news_results\": {batchResult.NewsResults.Count},\n" +
                $"  \"stock_preview\": \"{stockPreview}\",\n" +
                $"  \"news_preview\": \"{newsPreview}\",\n" +
                $"  \"response_time_ms\": {duration:F0}\n" +
                $"}}");
            
            return batchResult;
        }

        /// <summary>
        /// Execute simple web search (single query)
        /// 
        /// Demonstrates: Basic search with single query
        /// Frontend trigger: Search form (fallback mode)
        /// </summary>
        public async Task<List<SearchResult>> ExecuteSimpleSearchAsync(string query, int topResults)
        {
            _apiLogService.AddLog("Lumina Search API", "POST /api/sonicberry/search", 
                $"Parameters\n" +
                $"{{\n" +
                $"  \"q\": \"{query}\",\n" +
                $"  \"topN\": {topResults},\n" +
                $"  \"source\": \"WebWithBing\",\n" +
                $"  \"market\": \"en-US\"\n" +
                $"}}");
            
            var startTime = DateTime.Now;
            var results = await _luminaSearchService.ExecuteWebSearchAsync(query, topResults);
            var duration = (DateTime.Now - startTime).TotalMilliseconds;
            
            var preview = results.Count > 0 && results[0].Title != null ? 
                results[0].Title.Substring(0, Math.Min(50, results[0].Title.Length)) + "..." : "(no results)";
            
            _apiLogService.AddLog("Lumina Search API", "POST /api/sonicberry/search", 
                $"Result\n" +
                $"{{\n" +
                $"  \"results_count\": {results.Count},\n" +
                $"  \"first_result\": \"{preview}\",\n" +
                $"  \"response_time_ms\": {duration:F0}\n" +
                $"}}");
            
            return results;
        }
    }
}
