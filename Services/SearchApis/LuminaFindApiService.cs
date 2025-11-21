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

        public LuminaFindApiService(
            LuminaSearchService luminaSearchService,
            ApiLogService apiLogService)
        {
            _luminaSearchService = luminaSearchService;
            _apiLogService = apiLogService;
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
            _apiLogService.AddLog("Lumina Search API", "POST /api/sonicberry/search (Step 1/2)", 
                $"Parameters\n" +
                $"{{\n" +
                $"  \"q\": \"{companyName} Wikipedia\",\n" +
                $"  \"topN\": 3,\n" +
                $"  \"source\": \"WebWithBing\"\n" +
                $"}}", true, "Company Overview");
            
            var wikiSearchStart = DateTime.Now;
            var wikipediaSearchResults = await _luminaSearchService.ExecuteWebSearchAsync($"{companyName} Wikipedia", 3);
            var wikiSearchDuration = (DateTime.Now - wikiSearchStart).TotalMilliseconds;
            
            var wikipediaUrl = wikipediaSearchResults
                .FirstOrDefault(r => r.Url?.Contains("wikipedia.org/wiki/") == true)?.Url;
            
            if (string.IsNullOrEmpty(wikipediaUrl))
            {
                _apiLogService.AddLog("Lumina Search API", "POST /api/sonicberry/search (Step 1/2)", 
                    $"Result\n" +
                    $"{{\n" +
                    $"  \"results_count\": {wikipediaSearchResults.Count},\n" +
                    $"  \"wikipedia_url\": null,\n" +
                    $"  \"response_time_ms\": {wikiSearchDuration:F0}\n" +
                    $"}}", false, "Company Overview");
                return new FindApiResult 
                { 
                    Success = false, 
                    ErrorMessage = "No Wikipedia page found for this company" 
                };
            }
            
            _apiLogService.AddLog("Lumina Search API", "POST /api/sonicberry/search (Step 1/2)", 
                $"Result\n" +
                $"{{\n" +
                $"  \"results_count\": {wikipediaSearchResults.Count},\n" +
                $"  \"wikipedia_url\": \"{wikipediaUrl}\",\n" +
                $"  \"response_time_ms\": {wikiSearchDuration:F0}\n" +
                $"}}", true, "Company Overview");
            
            // Step 2: Extract company info using Find API
            _apiLogService.AddLog("Lumina Find API", "POST /api/sonicberry/find (Step 2/2)", 
                $"Parameters\n" +
                $"{{\n" +
                $"  \"url\": \"{wikipediaUrl}\",\n" +
                $"  \"patterns\": [\"Founded\", \"Headquarters\", \"Revenue\", \"Industry\", \"Type\"]\n" +
                $"}}", true, "Company Overview");
            
            var infoStartTime = DateTime.Now;
            var companyInfo = await _luminaSearchService.ExtractCompanyInfoAsync(wikipediaUrl);
            var infoDuration = (DateTime.Now - infoStartTime).TotalMilliseconds;
            
            if (companyInfo != null && companyInfo.Fields.Any())
            {
                var fieldsJson = string.Join(",\n    ", companyInfo.Fields.Select(f => 
                    $"\"{f.FieldName}\": \"{f.Content.Substring(0, Math.Min(30, f.Content.Length))}{(f.Content.Length > 30 ? "..." : "")}\""));
                _apiLogService.AddLog("Lumina Find API", "POST /api/sonicberry/find (Step 2/2)", 
                    $"Result\n" +
                    $"{{\n" +
                    $"  \"fields_extracted\": {companyInfo.Fields.Count},\n" +
                    $"  \"data\": {{\n    {fieldsJson}\n  }},\n" +
                    $"  \"response_time_ms\": {infoDuration:F0}\n" +
                    $"}}", true, "Company Overview");
                
                return new FindApiResult
                {
                    Success = true,
                    CompanyInfo = companyInfo
                };
            }
            else
            {
                _apiLogService.AddLog("Lumina Find API", "POST /api/sonicberry/find (Step 2/2)", 
                    $"Result\n" +
                    $"{{\n" +
                    $"  \"fields_extracted\": 0,\n" +
                    $"  \"response_time_ms\": {infoDuration:F0}\n" +
                    $"}}", false, "Company Overview");
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
