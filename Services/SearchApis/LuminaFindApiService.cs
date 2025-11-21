using LuminaSearchConsole.Models;

namespace LuminaSearchConsole.Services.SearchApis
{
    /// <summary>
    /// Lumina Find API - Demonstrates POST /api/sonicberry/find
    /// 
    /// API Endpoint: POST /api/sonicberry/find
    /// Purpose: Extract structured information from web pages
    /// 
    /// UI Integration:
    /// - 🏢 Company Overview card (left column after search)
    /// - Displays extracted fields: Founded, Headquarters, CEO, Revenue, etc.
    /// 
    /// How to try:
    /// 1. Search for a company (e.g., "Microsoft")
    /// 2. Wait for page to load (card shows loading spinner)
    /// 3. View extracted company info in "🏢 Company Overview" card
    /// 4. If extraction fails, click "🔄 Retry" button in the card
    /// 
    /// Frontend code: wwwroot/js/search.js → loadCompanyInfo()
    /// 
    /// Implementation:
    /// - Step 1: Search Wikipedia for the company page (Search API)
    /// - Step 2: Extract structured info from Wikipedia (Find API)
    /// </summary>
    public class LuminaFindApiService
    {
        private readonly LuminaSearchService _luminaSearchService;
        private readonly ApiLogService _apiLogService;
        private readonly ILogger<LuminaFindApiService> _logger;

        public LuminaFindApiService(
            LuminaSearchService luminaSearchService,
            ApiLogService apiLogService,
            ILogger<LuminaFindApiService> logger)
        {
            _luminaSearchService = luminaSearchService;
            _apiLogService = apiLogService;
            _logger = logger;
        }

        /// <summary>
        /// Extract company information from Wikipedia using Find API
        /// 
        /// Process:
        /// 1. Search for company's Wikipedia page (using Search API)
        /// 2. Extract structured fields from the page (using Find API)
        /// 
        /// Returns fields like: Founded, Headquarters, CEO, Revenue, Industry, etc.
        /// 
        /// Frontend trigger: AJAX call from loadCompanyInfo() in search.js
        /// Retry feature: Retry button calls this method again
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
