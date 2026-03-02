using Microsoft.Lumina.Client.ApiProxy;

namespace MinimalApiCall;

/// <summary>
/// Lumina API Demo - Simple Web UI for PM learning.
/// Run: dotnet run → Open http://localhost:8401
/// </summary>
class Program
{
    static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseUrls("http://localhost:8401");

        var app = builder.Build();
        app.UseStaticFiles();

        // Load config
        var config = app.Configuration;
        var luminaEndpoint = config["LuminaConfiguration:ApiEndpoint"] ?? "";
        var cuaEndpoint = config["LuminaConfiguration:CuaEndpoint"] ?? "";
        var llmEndpoint = config["CopilotApi:Endpoint"] ?? "http://localhost:4141";
        var llmModel = config["CopilotApi:Model"] ?? "gpt-4";

        // Token provider (lazy init - only created when API is called)
        TokenService? tokenService = null;
        Func<Task<string>> tokenProvider = async () =>
        {
            tokenService ??= new TokenService(config);
            return await tokenService.GetTokenAsync();
        };

        // ========== Centralized Proxy Management ==========
        // One shared LuminaServiceApiProxy for all APIs (Search, Open, Find)
        LuminaServiceApiProxy? sharedProxy = null;
        var httpClientFactory = new DefaultHttpClientFactory();

        LuminaServiceApiProxy GetOrCreateProxy()
        {
            if (sharedProxy == null && !string.IsNullOrEmpty(luminaEndpoint))
            {
                var options = new LuminaApiOptions
                {
                    Endpoint = luminaEndpoint,
                    LuminaApiTokenProvider = async () => await tokenProvider()
                };
                sharedProxy = new LuminaServiceApiProxy(options, httpClientFactory);
            }
            return sharedProxy!;
        }

        // API clients (lazy init, all share the same proxy)
        SearchApi? searchApi = null;
        OpenApi? openApi = null;
        FindApi? findApi = null;
        CuaApi? cuaApi = null;
        LlmExample? llmExample = null;
        CompetitorAnalysisApi? competitorAnalysisApi = null;
        TrendingNewsApi? trendingNewsApi = null;

        void EnsureInit()
        {
            if (searchApi == null && !string.IsNullOrEmpty(luminaEndpoint))
            {
                var proxy = GetOrCreateProxy();
                searchApi = new SearchApi(proxy);
                openApi = new OpenApi(proxy);
                findApi = new FindApi(proxy);
                llmExample = new LlmExample(searchApi, llmEndpoint, llmModel);
                trendingNewsApi = new TrendingNewsApi(searchApi);
            }
            if (cuaApi == null && !string.IsNullOrEmpty(cuaEndpoint))
            {
                cuaApi = new CuaApi(cuaEndpoint, tokenProvider);
            }
            if (competitorAnalysisApi == null && searchApi != null && openApi != null && llmExample != null)
            {
                competitorAnalysisApi = new CompetitorAnalysisApi(searchApi, openApi, llmExample, cuaApi);
            }
        }

        // ========== API Routes ==========

        // POST /api/login - Trigger login and get token
        app.MapPost("/api/login", async () =>
        {
            try
            {
                var token = await tokenProvider();
                return Results.Ok(new { success = true, message = "Login successful" });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { success = false, message = ex.Message });
            }
        });

        // POST /api/search - Search with optional LLM summarization
        app.MapPost("/api/search", async (WebSearchRequest req) =>
        {
            EnsureInit();
            if (searchApi == null) return Results.BadRequest("LuminaConfiguration:Endpoint not configured");

            if (req.UseLlm && llmExample != null)
            {
                var summary = await llmExample.SearchAndSummarizeAsync(req.Query, req.TopN);
                return Results.Ok(new { type = "llm", summary });
            }
            else
            {
                var results = await searchApi.SearchAsync(req.Query, req.TopN);
                return Results.Ok(new { type = "search", results });
            }
        });

        // POST /api/llm-summary - Get LLM summary for a query (separate from search)
        app.MapPost("/api/llm-summary", async (WebLlmRequest req) =>
        {
            EnsureInit();
            if (llmExample == null) return Results.BadRequest("LLM not configured");

            var summary = await llmExample.SearchAndSummarizeAsync(req.Query, req.TopN);
            return Results.Ok(new { summary });
        });

        // POST /api/open - Open URL with optional Find
        app.MapPost("/api/open", async (WebOpenRequest req) =>
        {
            EnsureInit();
            if (openApi == null) return Results.BadRequest("LuminaConfiguration:Endpoint not configured");

            var response = await openApi.OpenUrlAsync(req.Url);
            if (response == null) return Results.BadRequest("Failed to open URL");

            object? findResult = null;
            if (!string.IsNullOrEmpty(req.FindPattern) && findApi != null)
            {
                var find = await findApi.FindInPageAsync(response.SessionId, req.FindPattern);
                findResult = find?.Results?.Take(10).Select(r => new { r.LineIdx, r.Template });
            }

            return Results.Ok(new
            {
                sessionId = response.SessionId,
                title = response.Title,
                content = response.Content?.Length > 2000 ? response.Content[..2000] + "..." : response.Content,
                findResults = findResult
            });
        });

        // POST /api/cua - CUA screenshot
        app.MapPost("/api/cua", async (WebCuaRequest req) =>
        {
            EnsureInit();
            if (cuaApi == null) return Results.BadRequest("LuminaConfiguration:CuaEndpoint not configured");

            var screenshot = await cuaApi.CaptureScreenshotAsync(req.Url);
            if (screenshot == null) return Results.BadRequest("Failed to capture screenshot");

            return Results.Ok(new { screenshot });
        });

        // POST /api/competitor-analysis - Complete competitor analysis
        app.MapPost("/api/competitor-analysis", async (WebCompetitorAnalysisRequest req) =>
        {
            EnsureInit();
            if (competitorAnalysisApi == null) 
                return Results.BadRequest("Competitor Analysis not configured. Ensure Lumina endpoint is set.");

            if (req.Competitors == null || req.Competitors.Count == 0)
                return Results.BadRequest("At least one competitor is required");

            var competitors = req.Competitors.Select(c => new CompetitorInput
            {
                Name = c.Name,
                WebsiteUrl = c.WebsiteUrl
            }).ToList();

            var results = await competitorAnalysisApi.AnalyzeMultipleCompetitorsAsync(
                competitors,
                req.RecencyDays,
                req.ArticlesPerQuery,
                req.MaxArticlesToOpen);

            return Results.Ok(new { results });
        });

        // POST /api/trending-news/competitor - Get news for single competitor
        app.MapPost("/api/trending-news/competitor", async (WebSingleCompetitorRequest req) =>
        {
            EnsureInit();
            if (trendingNewsApi == null)
                return Results.BadRequest("Trending News not configured. Ensure Lumina endpoint is set.");

            var result = await trendingNewsApi.GetCompetitorNewsAsync(
                req.Competitor,
                req.ForceRefresh,
                req.TopN);

            return Results.Ok(result);
        });

        // POST /api/trending-news - Get trending news for competitors
        app.MapPost("/api/trending-news", async (WebTrendingNewsRequest req) =>
        {
            EnsureInit();
            if (trendingNewsApi == null)
                return Results.BadRequest("Trending News not configured. Ensure Lumina endpoint is set.");

            var competitors = req.Competitors?.Length > 0 ? req.Competitors : null;
            var result = await trendingNewsApi.GetTrendingNewsAsync(
                competitors, 
                req.ForceRefresh, 
                req.TopN);

            return Results.Ok(result);
        });

        // GET /api/trending-news/status - Get cache status
        app.MapPost("/api/trending-news/status", (WebTrendingNewsStatusRequest req) =>
        {
            EnsureInit();
            if (trendingNewsApi == null)
                return Results.BadRequest("Trending News not configured");

            var status = trendingNewsApi.GetCacheStatus(req.Competitors);
            return Results.Ok(status);
        });

        // Serve index.html as default
        app.MapGet("/", () => Results.Redirect("/index.html"));

        // Release all CUA computers on shutdown
        app.Lifetime.ApplicationStopping.Register(() =>
        {
            if (cuaApi != null && cuaApi.ActiveComputers.Count > 0)
            {
                Console.WriteLine($"[Shutdown] Releasing {cuaApi.ActiveComputers.Count} active CUA computer(s)...");
                cuaApi.ReleaseAllAsync().GetAwaiter().GetResult();
            }
        });

        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════╗");
        Console.WriteLine("║        Lumina API Demo - Web UI                      ║");
        Console.WriteLine("║        Open: http://localhost:8401                   ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════╝");
        Console.WriteLine();

        app.Run();
    }
}

// Request models for Web API
record WebSearchRequest(string Query, int TopN = 5, bool UseLlm = false);
record WebLlmRequest(string Query, int TopN = 5);
record WebOpenRequest(string Url, string? FindPattern = null);
record WebCuaRequest(string Url);
record WebCompetitorAnalysisRequest(
    List<CompetitorInputWeb> Competitors,
    int RecencyDays = 7,
    int ArticlesPerQuery = 3,
    int MaxArticlesToOpen = 2);
record CompetitorInputWeb(string Name, string? WebsiteUrl = null);
record WebSingleCompetitorRequest(string Competitor, bool ForceRefresh = false, int TopN = 10);
record WebTrendingNewsRequest(string[]? Competitors = null, bool ForceRefresh = false, int TopN = 10);
record WebStockPriceRequest(string[] Competitors);
record WebTrendingNewsStatusRequest(string[]? Competitors = null);
