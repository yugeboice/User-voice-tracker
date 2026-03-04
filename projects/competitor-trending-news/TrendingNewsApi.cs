using Microsoft.Lumina.Client.Models.Sonicberry;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MinimalApiCall;

/// <summary>
/// Trending News Tracker - Daily competitor news monitoring with caching.
/// Tracks top 10 trending news for specified competitors.
/// </summary>
public class TrendingNewsApi
{
    private readonly SearchApi _searchApi;
    private readonly string _cacheDirectory;
    private static readonly string[] DefaultCompetitors = new[] 
    { 
        "Microsoft", "Google", "OpenAI", "Meta", "X (Twitter)", "Amazon" 
    };

    public TrendingNewsApi(SearchApi searchApi)
    {
        _searchApi = searchApi;
        _cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MinimalApiCall", "TrendingNews");
        Directory.CreateDirectory(_cacheDirectory);
    }

    /// <summary>
    /// Get trending news for a single competitor.
    /// Uses daily cache to avoid excessive API calls.
    /// </summary>
    public async Task<SingleCompetitorNewsResult> GetCompetitorNewsAsync(
        string competitor,
        bool forceRefresh = false,
        int topN = 10)
    {
        var today = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");
        var cacheKey = competitor.Replace(" ", "").Replace("(", "").Replace(")", "");
        var cacheFile = Path.Combine(_cacheDirectory, $"news_{cacheKey}_{today}.json");

        // Try to load from cache if not forcing refresh
        if (!forceRefresh && File.Exists(cacheFile))
        {
            try
            {
                var cachedJson = await File.ReadAllTextAsync(cacheFile);
                var cached = JsonSerializer.Deserialize<SingleCompetitorNewsResult>(cachedJson);
                if (cached != null && cached.News.Count > 0)
                {
                    Console.WriteLine($"[Trending News] Loaded from cache: {cacheFile}");
                    cached.FromCache = true;
                    cached.Message = "已从今日缓存加载";
                    return cached;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Trending News] Cache load failed: {ex.Message}");
            }
        }

        // Fetch fresh data
        Console.WriteLine($"[Trending News] Fetching news for {competitor}...");
        var allNews = new List<NewsItem>();

        try
        {
            // Search for recent news (last 1 day)
            var searchQuery = $"{competitor} news";
            var searchResults = await _searchApi.SearchWithRecencyAsync(searchQuery, topN, recencyDays: 1);

            foreach (var searchResult in searchResults)
            {
                var newsItem = new NewsItem
                {
                    Title = searchResult.Title ?? "",
                    Description = ExtractDescription(searchResult),
                    Url = searchResult.Url ?? "",
                    Source = ExtractSource(searchResult.Url ?? ""),
                    PublishedDate = ExtractDate(searchResult),
                    Thumbnail = ExtractThumbnail(searchResult),
                    Competitor = competitor,
                    Score = CalculateRelevanceScore(searchResult, competitor)
                };

                if (!string.IsNullOrEmpty(newsItem.Title))
                {
                    allNews.Add(newsItem);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Error searching {competitor}: {ex.Message}");
        }

        // Sort by score and take top N
        var topNews = allNews
            .OrderByDescending(n => n.Score)
            .Take(topN)
            .ToList();

        Console.WriteLine($"[Trending News] Found {topNews.Count} news items for {competitor}");

        var result = new SingleCompetitorNewsResult
        {
            Competitor = competitor,
            News = topNews,
            LastUpdated = DateTime.UtcNow,
            Success = topNews.Count > 0,
            Message = topNews.Count > 0 ? "成功获取最新新闻" : "未找到相关新闻",
            FromCache = false
        };

        // Save to cache
        try
        {
            var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(cacheFile, json);
            Console.WriteLine($"[Trending News] Saved cache to {cacheFile}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Trending News] Cache save failed: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Get trending news for specified competitors.
    /// Uses daily cache to avoid excessive API calls.
    /// </summary>
    public async Task<TrendingNewsResult> GetTrendingNewsAsync(
        string[]? competitors = null, 
        bool forceRefresh = false,
        int topN = 10)
    {
        competitors ??= DefaultCompetitors;
        var today = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");
        var cacheKey = string.Join("_", competitors.Select(c => c.Replace(" ", "").Replace("(", "").Replace(")", "")));
        var cacheFile = Path.Combine(_cacheDirectory, $"news_{cacheKey}_{today}.json");

        // Try to load from cache if not forcing refresh
        if (!forceRefresh && File.Exists(cacheFile))
        {
            try
            {
                var cachedJson = await File.ReadAllTextAsync(cacheFile);
                var cached = JsonSerializer.Deserialize<TrendingNewsResult>(cachedJson);
                if (cached != null && cached.News.Count > 0)
                {
                    Console.WriteLine($"[Trending News] Loaded from cache: {cacheFile}");
                    cached.FromCache = true;
                    cached.Message = "已从今日缓存加载";
                    return cached;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Trending News] Cache load failed: {ex.Message}");
            }
        }

        // Fetch fresh data
        Console.WriteLine($"[Trending News] Fetching fresh news for {competitors.Length} competitors...");
        var allNews = new List<NewsItem>();

        foreach (var competitor in competitors)
        {
            try
            {
                Console.WriteLine($"  Searching: {competitor}");
                
                // Search for recent news (last 1 day)
                var searchQuery = $"{competitor} news";
                var results = await _searchApi.SearchWithRecencyAsync(searchQuery, topN, recencyDays: 1);

                foreach (var result in results)
                {
                    var newsItem = new NewsItem
                    {
                        Title = result.Title ?? "",
                        Description = ExtractDescription(result),
                        Url = result.Url ?? "",
                        Source = ExtractSource(result.Url ?? ""),
                        PublishedDate = ExtractDate(result),
                        Thumbnail = ExtractThumbnail(result),
                        Competitor = competitor,
                        Score = CalculateRelevanceScore(result, competitor)
                    };

                    if (!string.IsNullOrEmpty(newsItem.Title))
                    {
                        allNews.Add(newsItem);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Error searching {competitor}: {ex.Message}");
            }
        }

        // Sort by score and take top N
        var topNews = allNews
            .OrderByDescending(n => n.Score)
            .Take(topN)
            .ToList();

        Console.WriteLine($"[Trending News] Found {topNews.Count} news items");

        var apiResult = new TrendingNewsResult
        {
            News = topNews,
            LastUpdated = DateTime.UtcNow,
            Competitors = competitors.ToList(),
            Success = topNews.Count > 0,
            Message = topNews.Count > 0 ? "成功获取最新新闻" : "未找到相关新闻",
            FromCache = false
        };

        // Save to cache
        try
        {
            var json = JsonSerializer.Serialize(apiResult, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(cacheFile, json);
            Console.WriteLine($"[Trending News] Saved cache to {cacheFile}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Trending News] Cache save failed: {ex.Message}");
        }

        return apiResult;
    }

    private string ExtractDescription(SearchResultItem result)
    {
        if (!string.IsNullOrEmpty(result.SemanticDocument))
        {
            var desc = result.SemanticDocument.Trim();
            return desc.Length > 200 ? desc.Substring(0, 200) + "..." : desc;
        }
        return "No description available";
    }

    private string ExtractSource(string url)
    {
        try
        {
            var uri = new Uri(url);
            var host = uri.Host.Replace("www.", "");
            return char.ToUpper(host[0]) + host.Substring(1);
        }
        catch
        {
            return "Unknown";
        }
    }

    private DateTime ExtractDate(SearchResultItem result)
    {
        // Try to parse date from result metadata
        // For now, use current date as placeholder
        return DateTime.UtcNow;
    }

    private string? ExtractThumbnail(SearchResultItem result)
    {
        // Try to extract thumbnail URL from result
        // This is a simplified version - in production, you'd parse image URLs from the result
        var url = result.Url ?? "";
        if (url.Contains("youtube") || url.Contains("youtu.be"))
        {
            // Extract YouTube video ID and construct thumbnail
            var videoIdMatch = Regex.Match(url, @"(?:youtube\.com/watch\?v=|youtu\.be/)([a-zA-Z0-9_-]+)");
            if (videoIdMatch.Success)
            {
                return $"https://img.youtube.com/vi/{videoIdMatch.Groups[1].Value}/mqdefault.jpg";
            }
        }
        return null;
    }

    private double CalculateRelevanceScore(SearchResultItem result, string competitor)
    {
        double score = 0;

        // Base score from search ranking (implicit in order)
        score += 10;

        // Boost if competitor name is in title
        if (result.Title?.Contains(competitor, StringComparison.OrdinalIgnoreCase) == true)
        {
            score += 20;
        }

        // Boost for reputable sources
        var url = result.Url?.ToLower() ?? "";
        if (url.Contains("techcrunch") || url.Contains("theverge") || 
            url.Contains("reuters") || url.Contains("bloomberg") ||
            url.Contains("wsj.com") || url.Contains("nytimes"))
        {
            score += 15;
        }

        // Boost for recent content
        score += 10; // Recent by default due to recency filter

        return score;
    }

    public CacheStatus GetCacheStatus(string[]? competitors = null)
    {
        competitors ??= DefaultCompetitors;
        var today = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");
        var cacheKey = string.Join("_", competitors.Select(c => c.Replace(" ", "").Replace("(", "").Replace(")", "")));
        var cacheFile = Path.Combine(_cacheDirectory, $"news_{cacheKey}_{today}.json");

        if (File.Exists(cacheFile))
        {
            var fileInfo = new FileInfo(cacheFile);
            return new CacheStatus
            {
                HasCache = true,
                IsToday = true,
                LastUpdated = fileInfo.LastWriteTimeUtc,
                Message = "今日缓存可用"
            };
        }

        return new CacheStatus
        {
            HasCache = false,
            IsToday = false,
            LastUpdated = DateTime.MinValue,
            Message = "无缓存，需要刷新"
        };
    }
}

/// <summary>News item with all display information.</summary>
public class NewsItem
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Url { get; set; } = "";
    public string Source { get; set; } = "";
    public DateTime PublishedDate { get; set; }
    public string? Thumbnail { get; set; }
    public string Competitor { get; set; } = "";
    public double Score { get; set; }
}

/// <summary>Result from single competitor news query.</summary>
public class SingleCompetitorNewsResult
{
    public string Competitor { get; set; } = "";
    public List<NewsItem> News { get; set; } = new();
    public DateTime LastUpdated { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public bool FromCache { get; set; }
}

/// <summary>Result from trending news query.</summary>
public class TrendingNewsResult
{
    public List<NewsItem> News { get; set; } = new();
    public DateTime LastUpdated { get; set; }
    public List<string> Competitors { get; set; } = new();
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public bool FromCache { get; set; }
}

/// <summary>Cache status information.</summary>
public class CacheStatus
{
    public bool HasCache { get; set; }
    public bool IsToday { get; set; }
    public DateTime LastUpdated { get; set; }
    public string Message { get; set; } = "";
}
