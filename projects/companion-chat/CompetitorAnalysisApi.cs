using System.Text;
using System.Text.Json;
using Microsoft.Lumina.Client.Models.Sonicberry;

namespace MinimalApiCall;

/// <summary>
/// Competitor analysis API that uses LLM to generate search queries and searches recent news.
/// </summary>
public class CompetitorAnalysisApi
{
    private readonly SearchApi _searchApi;
    private readonly OpenApi _openApi;
    private readonly CuaApi? _cuaApi;
    private readonly HttpClient _httpClient;
    private readonly string _llmEndpoint;
    private readonly string _model;

    public CompetitorAnalysisApi(SearchApi searchApi, OpenApi openApi, CuaApi? cuaApi, string llmEndpoint = "http://localhost:4141", string model = "gpt-4")
    {
        _searchApi = searchApi;
        _openApi = openApi;
        _cuaApi = cuaApi;
        _httpClient = new HttpClient();
        _llmEndpoint = llmEndpoint;
        _model = model;
    }

    /// <summary>
    /// Analyze competitors by generating targeted search queries and fetching recent news.
    /// </summary>
    /// <param name="competitors">List of competitor names (e.g., "OpenAI", "Google AI", "Anthropic")</param>
    /// <param name="websiteUrls">Optional dictionary mapping competitor names to their website URLs</param>
    /// <param name="resultsPerCompetitor">Number of results to return per competitor (default: 5)</param>
    public async Task<CompetitorAnalysisResult> AnalyzeCompetitorsAsync(
        List<string> competitors, 
        Dictionary<string, string>? websiteUrls = null,
        int resultsPerCompetitor = 5)
    {
        var result = new CompetitorAnalysisResult
        {
            AnalysisDate = DateTime.UtcNow,
            Competitors = new List<CompetitorInfo>()
        };

        foreach (var competitor in competitors)
        {
            var competitorInfo = new CompetitorInfo
            {
                Name = competitor,
                SearchQueries = new List<string>(),
                NewsResults = new List<NewsItem>()
            };

            try
            {
                // Step 0: Get website URL (use provided URL or generate one)
                string websiteUrl;
                if (websiteUrls != null && websiteUrls.TryGetValue(competitor, out var providedUrl) && !string.IsNullOrEmpty(providedUrl))
                {
                    websiteUrl = providedUrl;
                }
                else
                {
                    websiteUrl = await GenerateWebsiteUrlAsync(competitor);
                }
                competitorInfo.WebsiteUrl = websiteUrl;
                
                if (_cuaApi != null && !string.IsNullOrEmpty(websiteUrl))
                {
                    try
                    {
                        Console.WriteLine($"[Competitor Analysis] Capturing screenshot for {competitor} ({websiteUrl})...");
                        var screenshot = await _cuaApi.CaptureScreenshotAsync(websiteUrl);
                        competitorInfo.Screenshot = screenshot;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Competitor Analysis] Failed to capture screenshot for {competitor}: {ex.Message}");
                    }
                }

                // Step 1: Use LLM to generate precise search queries for this competitor
                var searchQueries = await GenerateSearchQueriesAsync(competitor);
                competitorInfo.SearchQueries = searchQueries;

                // Step 2: Search for recent news using generated queries
                var allResults = new List<SearchResultItem>();
                
                foreach (var query in searchQueries)
                {
                    // Add time filter for last week
                    var timeFilteredQuery = $"{query} site:news after:{DateTime.UtcNow.AddDays(-7):yyyy-MM-dd}";
                    var searchResults = await _searchApi.SearchAsync(timeFilteredQuery, 3);
                    allResults.AddRange(searchResults);
                }

                // Step 3: Deduplicate and take top N results
                var uniqueResults = allResults
                    .GroupBy(r => r.Url)
                    .Select(g => g.First())
                    .Take(resultsPerCompetitor)
                    .ToList();

                // Step 4: Convert to NewsItem format and fetch full content for top 2
                for (int i = 0; i < uniqueResults.Count; i++)
                {
                    var r = uniqueResults[i];
                    var newsItem = new NewsItem
                    {
                        Title = r.Title ?? "No Title",
                        Url = r.Url ?? "",
                        Snippet = r.SemanticDocument?.Substring(0, Math.Min(r.SemanticDocument.Length, 200)) ?? r.Title ?? "",
                        Source = ExtractDomain(r.Url ?? ""),
                        PublishedDate = DateTime.UtcNow // Use current time as SearchResultItem doesn't have CreatedDateTime
                    };

                    // Fetch full content for top 2 articles
                    if (i < 2 && !string.IsNullOrEmpty(newsItem.Url))
                    {
                        try
                        {
                            var openResult = await _openApi.OpenUrlAsync(newsItem.Url);
                            if (openResult != null && !string.IsNullOrEmpty(openResult.Content))
                            {
                                newsItem.FullContent = openResult.Content;
                                newsItem.HasFullContent = true;
                            }
                        }
                        catch
                        {
                            // If fetching fails, keep the snippet
                            newsItem.HasFullContent = false;
                        }
                    }

                    competitorInfo.NewsResults.Add(newsItem);
                }

                competitorInfo.Status = "Success";
            }
            catch (Exception ex)
            {
                competitorInfo.Status = $"Error: {ex.Message}";
            }

            result.Competitors.Add(competitorInfo);
        }

        return result;
    }

    /// <summary>
    /// Use LLM to generate the official website URL for a competitor.
    /// </summary>
    private async Task<string> GenerateWebsiteUrlAsync(string competitorName)
    {
        var systemPrompt = @"You are a helpful assistant that provides official website URLs for companies.
Return ONLY the URL in the format: https://example.com
Do not include 'www.' prefix. Return only the main domain.";

        var userMessage = $"What is the official website URL for: {competitorName}";

        var requestBody = new
        {
            model = _model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage }
            },
            temperature = 0.3,
            max_tokens = 50
        };

        try
        {
            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_llmEndpoint}/v1/chat/completions", content);
            
            if (!response.IsSuccessStatusCode)
            {
                return GuessWebsiteUrl(competitorName);
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            var llmResponse = JsonSerializer.Deserialize<JsonElement>(responseJson);
            
            var url = llmResponse.GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString()?.Trim() ?? "";

            // Validate URL format
            if (Uri.TryCreate(url, UriKind.Absolute, out _))
            {
                return url;
            }

            return GuessWebsiteUrl(competitorName);
        }
        catch
        {
            return GuessWebsiteUrl(competitorName);
        }
    }

    /// <summary>
    /// Fallback method to guess website URL based on company name.
    /// </summary>
    private string GuessWebsiteUrl(string competitorName)
    {
        // Simple heuristic: convert to lowercase, remove spaces, add .com
        var domain = competitorName.ToLower()
            .Replace(" ", "")
            .Replace("ai", "")
            .Replace("inc", "")
            .Replace(".", "")
            .Trim();
        
        return $"https://{domain}.com";
    }

    /// <summary>
    /// Use LLM to generate targeted search queries for a competitor.
    /// </summary>
    private async Task<List<string>> GenerateSearchQueriesAsync(string competitorName)
    {
        var systemPrompt = @"You are a research assistant that generates precise search queries for competitor analysis. 
Your task is to create 3-4 diverse search queries that will help find the most relevant recent news about a company.
Focus on: product launches, funding/investments, partnerships, industry trends, and market positioning.
Return ONLY a JSON array of search query strings, nothing else.";

        var userMessage = $@"Generate 3-4 precise search queries to find recent news about: {competitorName}

Examples:
- ""[Company] latest product launch""
- ""[Company] funding news 2025""
- ""[Company] partnership announcement""
- ""[Company] market share report""

Return format: [""query1"", ""query2"", ""query3""]";

        var requestBody = new
        {
            model = _model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage }
            },
            temperature = 0.7,
            max_tokens = 300
        };

        try
        {
            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_llmEndpoint}/v1/chat/completions", content);
            
            if (!response.IsSuccessStatusCode)
            {
                return new List<string> { $"{competitorName} latest news", $"{competitorName} product launch" };
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            var llmResponse = JsonSerializer.Deserialize<JsonElement>(responseJson);
            
            var llmContent = llmResponse.GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "";

            // Try to parse as JSON array
            try
            {
                var queries = JsonSerializer.Deserialize<List<string>>(llmContent);
                if (queries != null && queries.Count > 0)
                    return queries;
            }
            catch
            {
                // If parsing fails, extract queries manually
                var lines = llmContent.Split('\n')
                    .Select(l => l.Trim().Trim('"', '[', ']', ','))
                    .Where(l => !string.IsNullOrWhiteSpace(l) && l.Length > 10)
                    .Take(4)
                    .ToList();
                
                if (lines.Count > 0)
                    return lines;
            }

            // Fallback queries
            return new List<string>
            {
                $"{competitorName} latest news",
                $"{competitorName} product launch 2025",
                $"{competitorName} funding announcement"
            };
        }
        catch
        {
            // Fallback in case of any error
            return new List<string>
            {
                $"{competitorName} latest news",
                $"{competitorName} recent developments"
            };
        }
    }

    /// <summary>
    /// Extract domain name from URL for display.
    /// </summary>
    private string ExtractDomain(string url)
    {
        try
        {
            var uri = new Uri(url);
            return uri.Host.Replace("www.", "");
        }
        catch
        {
            return "Unknown Source";
        }
    }

    /// <summary>
    /// One-click generate complete competitor analysis report.
    /// This method combines analysis and report generation in one call.
    /// </summary>
    public async Task<CompetitorFullReport> GenerateCompleteReportAsync(
        List<string> competitors,
        Dictionary<string, string>? websiteUrls = null,
        int resultsPerCompetitor = 5)
    {
        // Step 1: Analyze competitors (fetch news, screenshots, etc.)
        var analysisResult = await AnalyzeCompetitorsAsync(competitors, websiteUrls, resultsPerCompetitor);

        // Step 2: Generate AI report based on analysis
        var report = await GenerateAnalysisReportAsync(analysisResult);

        // Step 3: Return combined result
        return new CompetitorFullReport
        {
            AnalysisResult = analysisResult,
            AiReport = report
        };
    }

    /// <summary>
    /// Generate a comprehensive analysis report based on competitor analysis results.
    /// </summary>
    public async Task<string> GenerateAnalysisReportAsync(CompetitorAnalysisResult analysisResult)
    {
        // Build context from all competitors
        var contextBuilder = new StringBuilder();
        contextBuilder.AppendLine("# 竞品分析数据");
        contextBuilder.AppendLine();

        foreach (var comp in analysisResult.Competitors)
        {
            contextBuilder.AppendLine($"## {comp.Name}");
            contextBuilder.AppendLine();

            foreach (var news in comp.NewsResults)
            {
                contextBuilder.AppendLine($"### 新闻: {news.Title}");
                contextBuilder.AppendLine($"来源: {news.Source}");
                contextBuilder.AppendLine($"日期: {news.PublishedDate:yyyy-MM-dd}");
                contextBuilder.AppendLine($"链接: {news.Url}");
                contextBuilder.AppendLine();

                // Use full content if available, otherwise use snippet
                if (news.HasFullContent && !string.IsNullOrEmpty(news.FullContent))
                {
                    // Limit content length to avoid token overflow
                    var content = news.FullContent.Length > 3000 
                        ? news.FullContent.Substring(0, 3000) + "..." 
                        : news.FullContent;
                    contextBuilder.AppendLine($"内容: {content}");
                }
                else
                {
                    contextBuilder.AppendLine($"内容: {news.Snippet}");
                }
                contextBuilder.AppendLine();
                contextBuilder.AppendLine("---");
                contextBuilder.AppendLine();
            }
        }

        var context = contextBuilder.ToString();

        // Create prompt for LLM
        var systemPrompt = @"你是一位资深的市场分析师和战略咨询顾问。你的任务是基于竞品的最新新闻和动态，生成一份深入、专业且可执行的竞品分析报告。

报告要求：
1. 使用中文撰写，语气专业但易懂
2. 基于实际数据和新闻内容进行分析，不要臆造信息
3. 从以下维度分析每个竞品：
   - 产品功能更新：最近发布了什么新功能或产品？
   - 市场策略：有什么定价、合作、扩张、融资动作？
   - 用户反馈：用户和媒体的评价如何？有什么反响？
   - 竞争洞察：这些动态对我们有什么启示？我们应该关注什么？

4. 输出格式：
   - 每个竞品单独一节（## 标题）
   - 在各竞品分析后，添加「关键信息对比表」（使用 Markdown 表格）
   - 最后添加「总结与建议」部分，包括：
     * 行业趋势总结
     * 竞争态势分析
     * 对我们的行动建议（具体、可执行）

5. 如果某些维度没有足够信息，明确说明「暂无相关信息」，不要编造
6. 使用专业术语，但要确保非技术人员也能理解
7. 突出重点信息，使用加粗、列表等格式增强可读性";

        var userMessage = $@"请基于以下竞品的最新新闻和动态，生成一份全面的竞品分析报告：

{context}

请按照系统提示中的要求，生成结构清晰、洞察深入的分析报告。";

        return await CallLlmForReportAsync(systemPrompt, userMessage);
    }

    /// <summary>
    /// Call LLM to generate the analysis report.
    /// </summary>
    private async Task<string> CallLlmForReportAsync(string systemPrompt, string userMessage)
    {
        var requestBody = new
        {
            model = _model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage }
            },
            temperature = 0.7,
            max_tokens = 4000
        };

        try
        {
            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_llmEndpoint}/v1/chat/completions", content);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                return $"❌ 生成报告失败: HTTP {response.StatusCode}\n{errorContent}";
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            var llmResponse = JsonSerializer.Deserialize<JsonElement>(responseJson);
            
            var reportContent = llmResponse.GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "";

            return reportContent;
        }
        catch (Exception ex)
        {
            return $"❌ 生成报告时发生错误: {ex.Message}";
        }
    }
}

/// <summary>
/// Result of competitor analysis.
/// </summary>
public class CompetitorAnalysisResult
{
    public DateTime AnalysisDate { get; set; }
    public List<CompetitorInfo> Competitors { get; set; } = new();
}

/// <summary>
/// Complete competitor report including analysis data and AI-generated report.
/// </summary>
public class CompetitorFullReport
{
    public CompetitorAnalysisResult AnalysisResult { get; set; } = new();
    public string AiReport { get; set; } = "";
}

/// <summary>
/// Information about a single competitor.
/// </summary>
public class CompetitorInfo
{
    public string Name { get; set; } = "";
    public string WebsiteUrl { get; set; } = "";
    public string? Screenshot { get; set; }
    public List<string> SearchQueries { get; set; } = new();
    public List<NewsItem> NewsResults { get; set; } = new();
    public string Status { get; set; } = "";
}

/// <summary>
/// A single news item.
/// </summary>
public class NewsItem
{
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string Snippet { get; set; } = "";
    public string Source { get; set; } = "";
    public DateTime PublishedDate { get; set; }
    public string FullContent { get; set; } = "";
    public bool HasFullContent { get; set; } = false;
}
