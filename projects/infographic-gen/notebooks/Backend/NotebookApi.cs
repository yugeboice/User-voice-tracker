using System.Collections.Concurrent;
using System.Text;

namespace MinimalApiCall;

public class NotebookApi
{
    // In-memory cache for fast access
    private static readonly ConcurrentDictionary<string, LuminaApiDemo.Notebook> _notebooks = new();
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, Source>> _notebookSources = new();
    
    private readonly SearchApi _searchApi;
    private readonly OpenApi _openApi;
    private readonly NotebookStorage _storage;

    public NotebookApi(SearchApi searchApi, OpenApi openApi, NotebookStorage storage)
    {
        _searchApi = searchApi;
        _openApi = openApi;
        _storage = storage;
        
        // Load all notebooks from disk on startup
        LoadFromDisk();
    }

    private void LoadFromDisk()
    {
        var notebooks = _storage.LoadAllNotebooks();
        foreach (var notebook in notebooks)
        {
            _notebooks[notebook.Id] = notebook;
            
            // Load sources for each notebook
            var sources = _storage.LoadSources(notebook.Id);
            var sourceDict = new ConcurrentDictionary<string, Source>();
            foreach (var source in sources)
            {
                sourceDict[source.Id] = source;
            }
            _notebookSources[notebook.Id] = sourceDict;
        }
        Console.WriteLine($"[NotebookApi] Loaded {notebooks.Count} notebooks from disk");
    }

    // Notebook management methods
    public IEnumerable<LuminaApiDemo.Notebook> GetAllNotebooks()
    {
        return _notebooks.Values
            .Where(n => !n.IsDeleted)
            .OrderByDescending(n => n.UpdatedAt);
    }

    public LuminaApiDemo.Notebook? GetNotebook(string notebookId)
    {
        _notebooks.TryGetValue(notebookId, out var notebook);
        return notebook;
    }

    public LuminaApiDemo.Notebook CreateNotebook(string title, string? description)
    {
        var notebook = new LuminaApiDemo.Notebook
        {
            Id = Guid.NewGuid().ToString(),
            Title = title,
            Description = description ?? string.Empty,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            SourceCount = 0
        };

        _notebooks[notebook.Id] = notebook;
        _notebookSources[notebook.Id] = new ConcurrentDictionary<string, Source>();
        
        // Persist to disk
        _storage.SaveNotebook(notebook);
        SaveNotebookIndex();
        
        return notebook;
    }

    public bool UpdateNotebook(string notebookId, string? title, string? description)
    {
        if (!_notebooks.TryGetValue(notebookId, out var notebook))
            return false;

        if (title != null) notebook.Title = title;
        if (description != null) notebook.Description = description;
        notebook.UpdatedAt = DateTime.UtcNow;

        _notebooks[notebookId] = notebook;
        
        // Persist to disk
        _storage.SaveNotebook(notebook);
        SaveNotebookIndex();
        
        return true;
    }

    public bool DeleteNotebook(string notebookId)
    {
        if (!_notebooks.TryGetValue(notebookId, out var notebook))
            return false;
        
        // Soft delete: mark as deleted instead of removing
        notebook.IsDeleted = true;
        notebook.UpdatedAt = DateTime.UtcNow;
        
        // Persist to disk
        _storage.SaveNotebook(notebook);
        SaveNotebookIndex();
        
        return true;
    }

    private void SaveNotebookIndex()
    {
        _storage.SaveNotebookIndex(_notebooks.Values);
    }

    // Helper to get sources for a notebook
    private ConcurrentDictionary<string, Source> GetNotebookSourcesDict(string notebookId)
    {
        if (!_notebookSources.TryGetValue(notebookId, out var sources))
        {
            sources = new ConcurrentDictionary<string, Source>();
            _notebookSources[notebookId] = sources;
        }
        return sources;
    }

    private void UpdateNotebookSourceCount(string notebookId)
    {
        if (_notebooks.TryGetValue(notebookId, out var notebook))
        {
            var sources = GetNotebookSourcesDict(notebookId);
            notebook.SourceCount = sources.Count;
            notebook.UpdatedAt = DateTime.UtcNow;
            _notebooks[notebookId] = notebook;
            
            // Persist changes
            _storage.SaveNotebook(notebook);
            _storage.SaveSources(notebookId, sources.Values);
            SaveNotebookIndex();
        }
    }

    // Source management methods (now require notebookId)
    public Task<Source> AddTextSourceAsync(string notebookId, string? title, string content)
    {
        var sources = GetNotebookSourcesDict(notebookId);
        
        var source = new Source
        {
            Id = Guid.NewGuid().ToString(),
            Type = "text",
            Title = title ?? content.Substring(0, Math.Min(50, content.Length)) + "...",
            Content = content,
            CreatedAt = DateTime.UtcNow
        };

        sources[source.Id] = source;
        UpdateNotebookSourceCount(notebookId);
        return Task.FromResult(source);
    }

    public async Task<Source> AddFileSourceAsync(string notebookId, IFormFile file)
    {
        // Validate file extension
        var ext = Path.GetExtension(file.FileName).ToLower();
        if (ext != ".txt" && ext != ".md")
        {
            throw new ArgumentException("Only .txt and .md files are supported");
        }

        // Validate file size (10MB limit)
        const long MaxFileSize = 10 * 1024 * 1024;
        if (file.Length > MaxFileSize)
        {
            throw new ArgumentException("File size exceeds 10MB limit");
        }

        // Read file content with UTF-8 encoding
        string content;
        using (var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8))
        {
            content = await reader.ReadToEndAsync();
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("File content is empty");
        }

        var sources = GetNotebookSourcesDict(notebookId);
        
        var source = new Source
        {
            Id = Guid.NewGuid().ToString(),
            Type = "file",
            Title = file.FileName,
            Content = content,
            FileName = file.FileName,
            CreatedAt = DateTime.UtcNow
        };

        sources[source.Id] = source;
        UpdateNotebookSourceCount(notebookId);
        return source;
    }

    public async Task<Source> AddUrlSourceAsync(string notebookId, string url)
    {
        // Use OpenApi to fetch URL content
        var result = await _openApi.OpenUrlAsync(url);
        
        if (result == null || string.IsNullOrEmpty(result.Content))
        {
            throw new Exception("Failed to fetch URL content");
        }

        var sources = GetNotebookSourcesDict(notebookId);
        
        var source = new Source
        {
            Id = Guid.NewGuid().ToString(),
            Type = "url",
            Title = result.Title ?? url,
            Content = result.Content,
            Url = url,
            CreatedAt = DateTime.UtcNow
        };

        sources[source.Id] = source;
        UpdateNotebookSourceCount(notebookId);
        return source;
    }

    public async Task<List<SearchResultPreview>> PreviewSearchAsync(string query)
    {
        // Use SearchApi to perform search and return top 10 results for user selection
        var results = await _searchApi.SearchAsync(query, topN: 10);
        
        if (results == null || !results.Any())
        {
            throw new Exception("Search returned no results");
        }

        return results.Select((r, index) => new SearchResultPreview
        {
            Index = index,
            Title = r.Title ?? "Untitled",
            Url = r.Url ?? "",
            Snippet = (r.SemanticDocument != null && r.SemanticDocument.Length > 0) 
                ? r.SemanticDocument.Substring(0, Math.Min(200, r.SemanticDocument.Length)) 
                : (r.Title ?? ""),
            FullContent = r.SemanticDocument ?? r.Title ?? ""
        }).ToList();
    }

    public Task<List<Source>> AddSearchSourcesAsync(string notebookId, string query, List<int> selectedIndices, List<SearchResultPreview> searchResults)
    {
        var sources = GetNotebookSourcesDict(notebookId);
        var addedSources = new List<Source>();

        foreach (var index in selectedIndices)
        {
            var result = searchResults.FirstOrDefault(r => r.Index == index);
            if (result == null) continue;

            var source = new Source
            {
                Id = Guid.NewGuid().ToString(),
                Type = "search",
                Title = result.Title,
                Content = result.FullContent,
                Url = result.Url,
                Query = query,
                CreatedAt = DateTime.UtcNow
            };

            sources[source.Id] = source;
            addedSources.Add(source);
        }

        UpdateNotebookSourceCount(notebookId);
        return Task.FromResult(addedSources);
    }

    public IEnumerable<Source> GetAllSources(string notebookId)
    {
        var sources = GetNotebookSourcesDict(notebookId);
        return sources.Values.OrderByDescending(s => s.CreatedAt);
    }

    public Source? GetSource(string notebookId, string sourceId)
    {
        var sources = GetNotebookSourcesDict(notebookId);
        sources.TryGetValue(sourceId, out var source);
        return source;
    }

    public bool DeleteSource(string notebookId, string sourceId)
    {
        var sources = GetNotebookSourcesDict(notebookId);
        var removed = sources.TryRemove(sourceId, out _);
        if (removed)
        {
            UpdateNotebookSourceCount(notebookId);
        }
        return removed;
    }

    public void ClearAllSources(string notebookId)
    {
        var sources = GetNotebookSourcesDict(notebookId);
        sources.Clear();
        UpdateNotebookSourceCount(notebookId);
    }
}

public record Source
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public required string Title { get; init; }
    public required string Content { get; init; }
    public string? Url { get; init; }
    public string? Query { get; init; }
    public string? FileName { get; init; }
    public DateTime CreatedAt { get; init; }
}

public record SearchResultPreview
{
    public int Index { get; init; }
    public required string Title { get; init; }
    public required string Url { get; init; }
    public required string Snippet { get; init; }
    public required string FullContent { get; init; }
}

public record AddTextSourceRequest(string NotebookId, string? Title, string Content);
public record AddUrlSourceRequest(string NotebookId, string Url);
public record AddSearchSourceRequest(string Query);
public record AddSearchSourcesRequest(string NotebookId, string Query, List<int> SelectedIndices, List<SearchResultPreview> SearchResults);
