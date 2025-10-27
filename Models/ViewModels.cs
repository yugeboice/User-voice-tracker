namespace LuminaSearchConsole.Models
{
    public class SearchViewModel
    {
        public string Query { get; set; } = string.Empty;
        public int TopResults { get; set; } = 5;
        public bool IsAuthenticated { get; set; }
        public bool HasSearched { get; set; }
        public List<SearchResult>? SearchResults { get; set; }
        public BatchSearchResult? BatchSearchResult { get; set; }
        public bool IsBatchSearch { get; set; }
    }

    public class BatchSearchResult
    {
        public string CompanyName { get; set; } = string.Empty;
        public List<SearchResult> StockResults { get; set; } = new List<SearchResult>();
        public List<SearchResult> NewsResults { get; set; } = new List<SearchResult>();
    }

    public class SearchResult
    {
        public string? Title { get; set; }
        public string? Url { get; set; }
        public string? Summary { get; set; }
        public string? SemanticDocument { get; set; }
    }

    public class OpenContentRequest
    {
        public string Url { get; set; } = string.Empty;
    }

    public class ErrorViewModel
    {
        public string? RequestId { get; set; }
        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
    }
}