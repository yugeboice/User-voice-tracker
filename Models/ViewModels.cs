namespace LuminaSearchConsole.Models
{
    public class SearchViewModel
    {
        public string Query { get; set; } = string.Empty;
        public int TopResults { get; set; } = 5;
        public bool IsAuthenticated { get; set; }
        public bool HasSearched { get; set; }
        public List<SearchResult>? SearchResults { get; set; }
    }

    public class SearchResult
    {
        public string? Title { get; set; }
        public string? Url { get; set; }
        public string? Summary { get; set; }
        public string? SemanticDocument { get; set; }
    }

    public class ErrorViewModel
    {
        public string? RequestId { get; set; }
        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
    }
}