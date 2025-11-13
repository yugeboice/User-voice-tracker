namespace LuminaSearchConsole.Models
{
    /// <summary>
    /// Main view model for search page.
    /// Contains search parameters, authentication state, and search results.
    /// </summary>
    public class SearchViewModel
    {
        /// <summary>User's search query text</summary>
        public string Query { get; set; } = string.Empty;
        
        /// <summary>Number of search results to return (default: 5)</summary>
        public int TopResults { get; set; } = 5;
        
        /// <summary>Whether user has authenticated and has valid access token</summary>
        public bool IsAuthenticated { get; set; }
        
        /// <summary>Whether a search has been executed (for displaying results section)</summary>
        public bool HasSearched { get; set; }
        
        /// <summary>Simple search results (used when IsBatchSearch = false)</summary>
        public List<SearchResult>? SearchResults { get; set; }
        
        /// <summary>Batch search results containing stock + news (used when IsBatchSearch = true)</summary>
        public BatchSearchResult? BatchSearchResult { get; set; }
        
        /// <summary>Flag to indicate which result type to display</summary>
        public bool IsBatchSearch { get; set; }
        
        /// <summary>Stock price information extracted using Find API</summary>
        public LuminaSearchConsole.StockPriceInfo? StockPriceInfo { get; set; }
        
        /// <summary>API call logs for displaying execution flow</summary>
        public List<Services.ApiLogEntry>? ApiLogs { get; set; }
    }

    /// <summary>
    /// Batch search result containing both stock information and news articles.
    /// Used to display company-specific search with separate stock/news sections.
    /// </summary>
    public class BatchSearchResult
    {
        /// <summary>Company name that was searched</summary>
        public string CompanyName { get; set; } = string.Empty;
        
        /// <summary>Stock-related search results</summary>
        public List<SearchResult> StockResults { get; set; } = new List<SearchResult>();
        
        /// <summary>News-related search results</summary>
        public List<SearchResult> NewsResults { get; set; } = new List<SearchResult>();
    }

    /// <summary>
    /// Individual search result item.
    /// Contains title, URL, and content summary from Lumina Search API.
    /// </summary>
    public class SearchResult
    {
        /// <summary>Result title</summary>
        public string? Title { get; set; }
        
        /// <summary>Source URL</summary>
        public string? Url { get; set; }
        
        /// <summary>Brief content summary</summary>
        public string? Summary { get; set; }
        
        /// <summary>Semantic document extracted from the page</summary>
        public string? SemanticDocument { get; set; }
    }

    /// <summary>
    /// Request model for opening content from URL.
    /// Used in AJAX request to retrieve full content from a search result.
    /// </summary>
    public class OpenContentRequest
    {
        /// <summary>URL to extract content from using Lumina Open API</summary>
        public string Url { get; set; } = string.Empty;
    }

    /// <summary>
    /// Request model for CUA test with company search.
    /// </summary>
    public class TestCuaRequest
    {
        /// <summary>Company name to search for stock price on Bing</summary>
        public string CompanyName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Error page view model (standard ASP.NET Core template).
    /// </summary>
    public class ErrorViewModel
    {
        public string? RequestId { get; set; }
        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
    }
}