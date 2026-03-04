using System.Text;
using System.Text.Json;
using Microsoft.Lumina.Client.Models.Sonicberry;

namespace MinimalApiCall.Services;

/// <summary>
/// Service to monitor AI Arena leaderboard and generate analysis reports.
/// Combines CUA (visual proof), Search (data gathering), and LLM (analysis).
/// </summary>
public class ArenaMonitor
{
    private readonly CuaApi _cuaApi;
    private readonly SearchApi _searchApi;
    private readonly HttpClient _httpClient;
    private readonly string _llmEndpoint;
    private readonly string _model;

    public ArenaMonitor(CuaApi cuaApi, SearchApi searchApi, string llmEndpoint = "http://localhost:4141", string model = "gpt-4")
    {
        _cuaApi = cuaApi;
        _searchApi = searchApi;
        _httpClient = new HttpClient();
        _llmEndpoint = llmEndpoint;
        _model = model;
    }

    /// <summary>
    /// Result object containing step-by-step progress and final report.
    /// </summary>
    public class ArenaReportResult
    {
        public string ScreenshotBase64 { get; set; } = "";
        public List<string> TopModels { get; set; } = new();
        public int TotalSourcesSearched { get; set; } = 0;
        public string ReportHtml { get; set; } = "";
    }

    /// <summary>
    /// Phase 1 Result: Screenshot and Top 10 models for user selection.
    /// </summary>
    public class ArenaPhase1Result
    {
        public string ScreenshotBase64 { get; set; } = "";
        public List<string> TopModels { get; set; } = new();
    }

    /// <summary>
    /// Phase 2 Result: Analysis report for selected models.
    /// </summary>
    public class ArenaPhase2Result
    {
        public List<string> SelectedModels { get; set; } = new();
        public int TotalSourcesSearched { get; set; } = 0;
        public string ReportHtml { get; set; } = "";
    }

    /// <summary>
    /// Phase 1: Capture screenshot and identify Top 10 models for user selection.
    /// </summary>
    public async Task<ArenaPhase1Result> GetTopModelsAsync()
    {
        var result = new ArenaPhase1Result();

        // Step 1: Capture Screenshot of the Leaderboard
        Console.WriteLine("[ArenaMonitor] Phase 1 Step 1: Capturing screenshot...");
        result.ScreenshotBase64 = await CaptureLeaderboardAsync() ?? "";

        // Step 2: Identify Top 10 Models via Search + LLM
        Console.WriteLine("[ArenaMonitor] Phase 1 Step 2: Identifying top 10 models...");
        var (topModels, _) = await IdentifyTopModelsAsync(10);
        result.TopModels = topModels;
        Console.WriteLine($"[ArenaMonitor] Top 10 models identified: {string.Join(", ", topModels)}");

        return result;
    }

    /// <summary>
    /// Phase 2: Analyze selected models and generate report.
    /// </summary>
    public async Task<ArenaPhase2Result> AnalyzeModelsAsync(List<string> selectedModels)
    {
        var result = new ArenaPhase2Result { SelectedModels = selectedModels };
        int totalSources = 0;

        // Deep Dive - Search for news/reviews for each selected model
        Console.WriteLine($"[ArenaMonitor] Phase 2: Analyzing {selectedModels.Count} models...");
        var modelAnalyses = new List<(string Model, string Analysis)>();
        foreach (var model in selectedModels)
        {
            Console.WriteLine($"[ArenaMonitor] Searching for: {model}");
            var (analysis, sources) = await SearchAndSummarizeModelAsync(model);
            modelAnalyses.Add((model, analysis));
            totalSources += sources;
        }
        result.TotalSourcesSearched = totalSources;

        // Generate Final HTML Report (McKinsey Style)
        Console.WriteLine("[ArenaMonitor] Phase 2: Generating final HTML report...");
        result.ReportHtml = await GenerateFinalHtmlReportAsync(selectedModels, modelAnalyses);

        return result;
    }

    /// <summary>
    /// Legacy: Full workflow in one call (for backward compatibility).
    /// </summary>
    public async Task<ArenaReportResult> GenerateReportAsync()
    {
        var result = new ArenaReportResult();
        int totalSources = 0;

        // Step 1: Capture Screenshot of the Leaderboard
        Console.WriteLine("[ArenaMonitor] Step 1: Capturing screenshot...");
        result.ScreenshotBase64 = await CaptureLeaderboardAsync() ?? "";

        // Step 2: Identify Top 5 Models via Search + LLM
        Console.WriteLine("[ArenaMonitor] Step 2: Identifying top 5 models...");
        var (topModels, step2Sources) = await IdentifyTopModelsAsync(5);
        result.TopModels = topModels;
        totalSources += step2Sources;
        Console.WriteLine($"[ArenaMonitor] Top models identified: {string.Join(", ", topModels)}");

        // Step 3: Deep Dive - Search for news/reviews for each model
        Console.WriteLine("[ArenaMonitor] Step 3: Deep diving into top models...");
        var modelAnalyses = new List<(string Model, string Analysis)>();
        foreach (var model in topModels)
        {
            var (analysis, sources) = await SearchAndSummarizeModelAsync(model);
            modelAnalyses.Add((model, analysis));
            totalSources += sources;
        }
        result.TotalSourcesSearched = totalSources;

        // Step 4: Generate Final HTML Report (McKinsey Style)
        Console.WriteLine("[ArenaMonitor] Step 4: Generating final HTML report...");
        result.ReportHtml = await GenerateFinalHtmlReportAsync(topModels, modelAnalyses);

        return result;
    }

    private async Task<string?> CaptureLeaderboardAsync()
    {
        return await _cuaApi.CaptureScreenshotAsync("https://lmarena.ai/leaderboard", new[]
        {
            new CuaAction { Action = "wait" },
            new CuaAction { Action = "wait" },
            new CuaAction { Action = "scroll", X = 0, Y = 500 }
        });
    }

    private async Task<(List<string> Models, int SourceCount)> IdentifyTopModelsAsync(int topN = 10)
    {
        var searchResults = await _searchApi.SearchAsync("current LMSYS Chatbot Arena leaderboard top models ranking list December 2024", 5);
        var context = BuildContextFromResults(searchResults);

        var prompt = $@"Based on the search results, identify the current Top {topN} AI models on the LMSYS Chatbot Arena Leaderboard.
Return ONLY the names of the {topN} models, separated by commas. Do not add numbering or extra text.
Example: GPT-4o, Gemini 1.5 Pro, Claude 3.5 Sonnet, Llama 3.1, Mistral Large";

        var response = await CallLlmAsync(prompt, context);
        
        var models = response.Split(',')
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Take(topN)
            .ToList();

        if (models.Count == 0)
        {
            models = new List<string> { "GPT-4o", "Gemini 1.5 Pro", "Claude 3.5 Sonnet", "Llama 3.1 405B", "Grok-2" };
        }

        return (models, searchResults.Count);
    }

    private async Task<(string Analysis, int SourceCount)> SearchAndSummarizeModelAsync(string modelName)
    {
        var query = $"{modelName} AI model recent news reviews reddit youtube performance benchmark";
        var results = await _searchApi.SearchAsync(query, 5);
        var context = BuildContextFromResults(results);

        var prompt = $@"Analyze the recent performance, news, and community sentiment (Reddit/YouTube) for the AI model: {modelName}.
Provide:
- Key Strengths (2-3 bullet points)
- Key Weaknesses (2-3 bullet points)
- Recent News/Updates (1-2 sentences)
- Community Sentiment (positive/mixed/negative with brief reason)
Keep it concise.";

        var analysis = await CallLlmAsync(prompt, context);
        return (analysis, results.Count);
    }

    private async Task<string> GenerateFinalHtmlReportAsync(List<string> topModels, List<(string Model, string Analysis)> modelAnalyses)
    {
        var analysisContext = new StringBuilder();
        foreach (var (model, analysis) in modelAnalyses)
        {
            analysisContext.AppendLine($"### {model}");
            analysisContext.AppendLine(analysis);
            analysisContext.AppendLine();
        }

        var prompt = $@"You are a McKinsey-style AI consultant. Generate an executive briefing report on the current AI Arena Leaderboard.

Top 5 Models: {string.Join(", ", topModels)}

Model Analysis Data:
{analysisContext}

Generate ONLY the inner HTML content (no <html>, <head>, <body> tags). Use this structure:
1. Executive Summary section with key insights
2. A comparison table showing all 5 models with columns: Model, Strengths, Weaknesses, Sentiment
3. Individual model deep-dive sections
4. Strategic Recommendations section

Use these CSS classes (already defined):
- .executive-summary (blue gradient header box)
- .comparison-table (styled table)
- .model-card (individual model sections)
- .recommendation-box (highlighted recommendations)
- h2, h3 for headings
- .strength (green text), .weakness (red text), .neutral (gray text)

Make it professional, data-driven, and visually structured. Use bullet points and clear formatting.";

        var htmlContent = await CallLlmAsync(prompt, "");
        
        // Wrap in a styled container
        return WrapInMcKinseyStyle(htmlContent);
    }

    private string WrapInMcKinseyStyle(string content)
    {
        return $@"
<style>
    .mckinsey-report {{
        font-family: 'Segoe UI', 'Helvetica Neue', Arial, sans-serif;
        color: #1a1a2e;
        line-height: 1.6;
        max-width: 1200px;
        margin: 0 auto;
        padding: 40px;
        background: linear-gradient(135deg, #f8f9fc 0%, #e8ecf4 100%);
    }}
    .mckinsey-report h1 {{
        font-size: 2.2em;
        color: #0a2540;
        border-bottom: 4px solid #0066cc;
        padding-bottom: 15px;
        margin-bottom: 30px;
    }}
    .mckinsey-report h2 {{
        font-size: 1.5em;
        color: #0a2540;
        margin-top: 35px;
        margin-bottom: 20px;
        padding-left: 15px;
        border-left: 5px solid #0066cc;
    }}
    .mckinsey-report h3 {{
        font-size: 1.2em;
        color: #2c3e50;
        margin-top: 25px;
    }}
    .executive-summary {{
        background: linear-gradient(135deg, #0066cc 0%, #004499 100%);
        color: white;
        padding: 30px;
        border-radius: 12px;
        margin-bottom: 30px;
        box-shadow: 0 10px 30px rgba(0,102,204,0.3);
    }}
    .executive-summary h2 {{
        color: white;
        border-left-color: #66b3ff;
        margin-top: 0;
    }}
    .executive-summary ul {{
        list-style: none;
        padding: 0;
    }}
    .executive-summary li {{
        padding: 8px 0;
        padding-left: 25px;
        position: relative;
    }}
    .executive-summary li::before {{
        content: '→';
        position: absolute;
        left: 0;
        color: #66b3ff;
    }}
    .comparison-table {{
        width: 100%;
        border-collapse: collapse;
        margin: 25px 0;
        background: white;
        border-radius: 12px;
        overflow: hidden;
        box-shadow: 0 5px 20px rgba(0,0,0,0.1);
    }}
    .comparison-table th {{
        background: linear-gradient(135deg, #0a2540 0%, #1a3a5c 100%);
        color: white;
        padding: 18px 15px;
        text-align: left;
        font-weight: 600;
        text-transform: uppercase;
        font-size: 0.85em;
        letter-spacing: 0.5px;
    }}
    .comparison-table td {{
        padding: 15px;
        border-bottom: 1px solid #e8ecf4;
    }}
    .comparison-table tr:hover {{
        background: #f0f7ff;
    }}
    .comparison-table tr:last-child td {{
        border-bottom: none;
    }}
    .model-card {{
        background: white;
        border-radius: 12px;
        padding: 25px;
        margin: 20px 0;
        box-shadow: 0 5px 20px rgba(0,0,0,0.08);
        border-left: 5px solid #0066cc;
    }}
    .model-card h3 {{
        margin-top: 0;
        color: #0066cc;
    }}
    .recommendation-box {{
        background: linear-gradient(135deg, #f0f7ff 0%, #e6f0ff 100%);
        border: 2px solid #0066cc;
        border-radius: 12px;
        padding: 25px;
        margin-top: 30px;
    }}
    .recommendation-box h2 {{
        color: #0066cc;
        margin-top: 0;
    }}
    .strength {{ color: #00875a; font-weight: 600; }}
    .weakness {{ color: #de350b; font-weight: 600; }}
    .neutral {{ color: #5e6c84; }}
    ul {{
        padding-left: 20px;
    }}
    li {{
        margin: 8px 0;
    }}
    .badge {{
        display: inline-block;
        padding: 4px 12px;
        border-radius: 20px;
        font-size: 0.8em;
        font-weight: 600;
    }}
    .badge-positive {{ background: #e3fcef; color: #00875a; }}
    .badge-negative {{ background: #ffebe6; color: #de350b; }}
    .badge-mixed {{ background: #fff7e6; color: #ff8b00; }}
</style>
<div class=""mckinsey-report"">
    <h1>🏆 AI Arena Leaderboard Analysis</h1>
    <p style=""color:#5e6c84; margin-bottom:30px;"">Generated on {DateTime.Now:MMMM dd, yyyy} | Powered by Lumina API</p>
    {content}
</div>";
    }

    private string BuildContextFromResults(List<SearchResultItem> results)
    {
        var sb = new StringBuilder();
        foreach (var result in results)
        {
            sb.AppendLine($"Title: {result.Title}");
            sb.AppendLine($"URL: {result.Url}");
            sb.AppendLine($"Snippet: {result.SemanticDocument}");
            sb.AppendLine("---");
        }
        return sb.ToString();
    }

    private async Task<string> CallLlmAsync(string systemPrompt, string userContext)
    {
        var messages = new List<object>
        {
            new { role = "system", content = systemPrompt }
        };

        if (!string.IsNullOrEmpty(userContext))
        {
            messages.Add(new { role = "user", content = $"Context:\n{userContext}" });
        }

        var requestBody = new
        {
            model = _model,
            messages = messages,
            temperature = 0.3,
            max_tokens = 3000
        };

        try
        {
            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{_llmEndpoint}/v1/chat/completions", content);
            
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[LLM Error] {response.StatusCode}: {error}");
                return "Error generating analysis.";
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);
            return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LLM Exception] {ex.Message}");
            return "Error calling LLM.";
        }
    }
}
