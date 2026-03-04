using Microsoft.Lumina.Client.Models.Sonicberry;

namespace MinimalApiCall;

/// <summary>
/// Competitor Analysis API - Integrates Search, Open, LLM, and CUA for automated competitive intelligence.
/// 
/// Workflow:
/// 1. Generate smart search queries for each competitor
/// 2. Batch search recent news (last 7 days)
/// 3. Open top articles to get full content
/// 4. Generate comprehensive analysis report with LLM
/// 5. Capture competitor website screenshots
/// </summary>
public class CompetitorAnalysisApi
{
    private readonly SearchApi _searchApi;
    private readonly OpenApi _openApi;
    private readonly LlmExample _llmExample;
    private readonly CuaApi? _cuaApi;

    public CompetitorAnalysisApi(
        SearchApi searchApi,
        OpenApi openApi,
        LlmExample llmExample,
        CuaApi? cuaApi = null)
    {
        _searchApi = searchApi;
        _openApi = openApi;
        _llmExample = llmExample;
        _cuaApi = cuaApi;
    }

    /// <summary>
    /// Complete competitor analysis workflow.
    /// </summary>
    public async Task<CompetitorAnalysisResult> AnalyzeCompetitorAsync(
        string competitorName,
        string? websiteUrl = null,
        int recencyDays = 7,
        int articlesPerQuery = 3,
        int maxArticlesToOpen = 2)
    {
        var result = new CompetitorAnalysisResult
        {
            CompetitorName = competitorName,
            WebsiteUrl = websiteUrl,
            Timestamp = DateTime.UtcNow
        };

        try
        {
            Console.WriteLine($"\n[Competitor Analysis] Starting analysis for: {competitorName}");

            // Step 1: Generate search queries
            Console.WriteLine("[Step 1/5] Generating smart search queries...");
            var searchQueries = await _llmExample.GenerateSearchQueriesAsync(competitorName);
            result.SearchQueries = searchQueries;
            Console.WriteLine($"  Generated {searchQueries.Count} queries");

            // Step 2: Batch search
            Console.WriteLine("[Step 2/5] Searching recent news...");
            var searchResults = await _searchApi.BatchSearchAsync(searchQueries, articlesPerQuery, recencyDays);
            
            var allResults = searchResults.Values.SelectMany(r => r).ToList();
            result.SearchResultsCount = allResults.Count;
            Console.WriteLine($"  Found {allResults.Count} total articles");

            // Step 3: Open top articles for full content
            Console.WriteLine("[Step 3/5] Opening top articles to get full content...");
            var topArticles = allResults
                .Where(r => !string.IsNullOrEmpty(r.Url))
                .Take(maxArticlesToOpen)
                .Select(r => r.Url!)
                .ToList();

            var openResults = await _openApi.BatchOpenUrlsAsync(topArticles);
            var fullArticles = openResults
                .Where(r => r != null && !string.IsNullOrEmpty(r.Content))
                .Select(r => r!.Content!)
                .ToList();
            
            result.FullArticlesCount = fullArticles.Count;
            Console.WriteLine($"  Retrieved {fullArticles.Count} full articles");

            // Step 4: Generate analysis report
            Console.WriteLine("[Step 4/5] Generating comprehensive analysis report...");
            var report = await _llmExample.GenerateCompetitorReportAsync(
                competitorName, 
                allResults, 
                fullArticles);
            result.AnalysisReport = report;
            Console.WriteLine("  Report generated successfully");

            // Step 5: Capture website screenshot (optional)
            if (!string.IsNullOrEmpty(websiteUrl) && _cuaApi != null)
            {
                Console.WriteLine("[Step 5/5] Capturing website screenshot...");
                try
                {
                    var screenshot = await _cuaApi.CaptureScreenshotAsync(websiteUrl);
                    result.WebsiteScreenshot = screenshot;
                    Console.WriteLine("  Screenshot captured successfully");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Screenshot failed: {ex.Message}");
                    result.Errors.Add($"Screenshot capture failed: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine("[Step 5/5] Skipping screenshot (no URL or CUA not configured)");
            }

            result.Success = true;
            Console.WriteLine($"[Competitor Analysis] ✓ Analysis completed for {competitorName}\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Competitor Analysis] ✗ Error: {ex.Message}\n");
            result.Success = false;
            result.Errors.Add($"Analysis failed: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Analyze multiple competitors in batch.
    /// </summary>
    public async Task<List<CompetitorAnalysisResult>> AnalyzeMultipleCompetitorsAsync(
        List<CompetitorInput> competitors,
        int recencyDays = 7,
        int articlesPerQuery = 3,
        int maxArticlesToOpen = 2)
    {
        var results = new List<CompetitorAnalysisResult>();

        Console.WriteLine($"\n[Batch Analysis] Starting analysis for {competitors.Count} competitors\n");
        Console.WriteLine("=".PadRight(60, '='));

        for (int i = 0; i < competitors.Count; i++)
        {
            var competitor = competitors[i];
            Console.WriteLine($"\n[{i + 1}/{competitors.Count}] Analyzing: {competitor.Name}");
            
            var result = await AnalyzeCompetitorAsync(
                competitor.Name,
                competitor.WebsiteUrl,
                recencyDays,
                articlesPerQuery,
                maxArticlesToOpen);
            
            results.Add(result);

            // Delay between competitors to avoid overwhelming services
            if (i < competitors.Count - 1)
            {
                Console.WriteLine("Waiting 3 seconds before next competitor...");
                await Task.Delay(3000);
            }
        }

        Console.WriteLine("\n" + "=".PadRight(60, '='));
        Console.WriteLine($"[Batch Analysis] Completed! {results.Count(r => r.Success)}/{competitors.Count} successful\n");

        return results;
    }
}

/// <summary>Input for competitor analysis.</summary>
public class CompetitorInput
{
    public string Name { get; set; } = string.Empty;
    public string? WebsiteUrl { get; set; }
}

/// <summary>Result of competitor analysis.</summary>
public class CompetitorAnalysisResult
{
    public string CompetitorName { get; set; } = string.Empty;
    public string? WebsiteUrl { get; set; }
    public DateTime Timestamp { get; set; }
    public bool Success { get; set; }
    
    public List<string> SearchQueries { get; set; } = new();
    public int SearchResultsCount { get; set; }
    public int FullArticlesCount { get; set; }
    
    public string AnalysisReport { get; set; } = string.Empty;
    public string? WebsiteScreenshot { get; set; }
    
    public List<string> Errors { get; set; } = new();
}
