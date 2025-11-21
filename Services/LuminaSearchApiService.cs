using LuminaSearchConsole.Models;

namespace LuminaSearchConsole.Services
{
    /// <summary>
    /// Lumina Search & Find API Service
    /// 
    /// Demonstrates how to use Lumina Search APIs:
    /// - POST /api/sonicberry/search (Batch Search)
    /// - POST /api/sonicberry/find (Extract structured info)
    /// 
    /// UI Integration:
    /// - Main search box → BatchSearch (Stock + News results)
    /// - 🏢 Company Overview card → Find API (Wikipedia extraction)
    /// 
    /// Try it:
    /// 1. Enter company name (e.g., "Microsoft") in search box
    /// 2. Click "🔍 Search" → triggers BatchSearch
    /// 3. View "🏢 Company Overview" → triggers Find API via AJAX
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
        /// Lumina API: POST /api/sonicberry/search
        /// Demonstrates: Batch search pattern (multiple queries in one request)
        /// 
        /// UI Location: Main search form
        /// Frontend Flow:
        /// 1. User enters company name in search box
        /// 2. Form submits to /Home/Search
        /// 3. Returns Stock results + News results
        /// 
        /// Example Request:
        ///   Company: "Microsoft"
        ///   Generates 2 queries: "Microsoft stock price" + "Microsoft latest news"
        /// </summary>
        public async Task<BatchSearchResult> ExecuteBatchSearchAsync(string companyName, int topResults)
        {
            _apiLogService.AddLog("Lumina Search", "BatchSearch", 
                $"📋 Batch Search Request:\n" +
                $"  Company: '{companyName}'\n" +
                $"  Queries: Stock price + Latest news\n" +
                $"  TopN: {topResults}\n" +
                $"  API: POST /api/sonicberry/search");
            
            var startTime = DateTime.Now;
            var batchResult = await _luminaSearchService.ExecuteBatchCompanySearchAsync(companyName, topResults);
            var duration = (DateTime.Now - startTime).TotalMilliseconds;
            
            // Build result preview
            var stockPreview = batchResult.StockResults.Count > 0 ? 
                $"\n  First stock result: {batchResult.StockResults[0].Title}" : "";
            var newsPreview = batchResult.NewsResults.Count > 0 ? 
                $"\n  First news result: {batchResult.NewsResults[0].Title}" : "";
            
            _apiLogService.AddLog("Lumina Search", "BatchSearch", 
                $"✅ Search completed\n" +
                $"  Stock results: {batchResult.StockResults.Count}\n" +
                $"  News results: {batchResult.NewsResults.Count}\n" +
                $"  Response time: {duration:F0}ms{stockPreview}{newsPreview}");
            
            return batchResult;
        }

        /// <summary>
        /// Execute simple web search
        /// 
        /// Lumina API: POST /api/sonicberry/search
        /// Demonstrates: Single query search
        /// 
        /// UI Location: Main search form (fallback mode)
        /// </summary>
        public async Task<List<SearchResult>> ExecuteSimpleSearchAsync(string query, int topResults)
        {
            _apiLogService.AddLog("Lumina Search", "WebSearch", 
                $"📋 Query: '{query}', TopN: {topResults}\n" +
                $"  API: POST /api/sonicberry/search");
            
            var startTime = DateTime.Now;
            var results = await _luminaSearchService.ExecuteWebSearchAsync(query, topResults);
            var duration = (DateTime.Now - startTime).TotalMilliseconds;
            
            _apiLogService.AddLog("Lumina Search", "WebSearch", 
                $"✅ Found {results.Count} results ({duration:F0}ms)");
            
            return results;
        }

        /// <summary>
        /// Extract company information from Wikipedia using Find API
        /// 
        /// Lumina API: POST /api/sonicberry/find
        /// Demonstrates: Content extraction from structured pages
        /// 
        /// UI Location: 🏢 Company Overview card (left column)
        /// Frontend Flow:
        /// 1. Search completes → page loads
        /// 2. JavaScript calls loadCompanyInfo() in search.js
        /// 3. AJAX request to /Home/FindCompanyInfo
        /// 4. Card displays extracted fields (Founded, Headquarters, etc.)
        /// 
        /// Retry Feature:
        /// - If extraction fails, click "🔄 Retry" button in the card
        /// - Retry button calls loadCompanyInfo() again
        /// 
        /// Implementation Details:
        /// - First searches Wikipedia for the company page
        /// - Then uses Find API to extract structured information
        /// - Returns fields like Founded, Headquarters, CEO, Revenue, etc.
        /// </summary>
        public async Task<FindApiResult> FindCompanyInfoAsync(string companyName)
        {
            // Step 1: Search Wikipedia for company page
            _apiLogService.AddLog("Lumina Search", "WikipediaSearch", 
                $"📋 Searching Wikipedia for '{companyName}'\n" +
                $"  API: POST /api/sonicberry/search\n" +
                $"  Query: '{companyName} Wikipedia'");
            
            var wikiSearchStart = DateTime.Now;
            var wikipediaSearchResults = await _luminaSearchService.ExecuteWebSearchAsync($"{companyName} Wikipedia", 3);
            var wikiSearchDuration = (DateTime.Now - wikiSearchStart).TotalMilliseconds;
            
            var wikipediaUrl = wikipediaSearchResults
                .FirstOrDefault(r => r.Url?.Contains("wikipedia.org/wiki/") == true)?.Url;
            
            if (string.IsNullOrEmpty(wikipediaUrl))
            {
                _apiLogService.AddLog("Lumina Search", "WikipediaSearch", 
                    $"⚠️ No Wikipedia URL found ({wikiSearchDuration:F0}ms)");
                return new FindApiResult 
                { 
                    Success = false, 
                    ErrorMessage = "No Wikipedia page found for this company" 
                };
            }
            
            _apiLogService.AddLog("Lumina Search", "WikipediaSearch", 
                $"✅ Found: {wikipediaUrl} ({wikiSearchDuration:F0}ms)");
            
            // Step 2: Extract company info using Find API
            _apiLogService.AddLog("Lumina Find", "ExtractCompanyInfo", 
                $"📋 Extracting company info from Wikipedia\n" +
                $"  API: POST /api/sonicberry/find\n" +
                $"  URL: {wikipediaUrl}\n" +
                $"  Purpose: Extract structured fields (Founded, HQ, CEO, etc.)");
            
            var infoStartTime = DateTime.Now;
            var companyInfo = await _luminaSearchService.ExtractCompanyInfoAsync(wikipediaUrl);
            var infoDuration = (DateTime.Now - infoStartTime).TotalMilliseconds;
            
            if (companyInfo != null && companyInfo.Fields.Any())
            {
                _apiLogService.AddLog("Lumina Find", "ExtractCompanyInfo", 
                    $"✅ Extracted {companyInfo.Fields.Count} fields ({infoDuration:F0}ms)\n" +
                    $"  Fields: {string.Join(", ", companyInfo.Fields.Select(f => f.FieldName))}");
                
                return new FindApiResult
                {
                    Success = true,
                    CompanyInfo = companyInfo
                };
            }
            else
            {
                _apiLogService.AddLog("Lumina Find", "ExtractCompanyInfo", 
                    $"⚠️ No information found ({infoDuration:F0}ms)");
                return new FindApiResult
                {
                    Success = false,
                    ErrorMessage = "No company information found on the page"
                };
            }
        }
    }

    /// <summary>
    /// Result model for Find API operation
    /// </summary>
    public class FindApiResult
    {
        public bool Success { get; set; }
        public CompanyInfo? CompanyInfo { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
