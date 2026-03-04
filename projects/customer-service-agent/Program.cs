using Microsoft.Lumina.Client.ApiProxy;
using MinimalApiCall.Internal;

namespace MinimalApiCall;

/// <summary>
/// Lumina API Demo - Simple Web UI for PM learning.
/// Run: dotnet run → Open http://localhost:8400
/// </summary>
class Program
{
    static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseUrls("http://localhost:8405");

        var app = builder.Build();
        app.UseStaticFiles();

        // Load config
        var config = app.Configuration;
        var luminaEndpoint = config["LuminaConfiguration:ApiEndpoint"] ?? "";
        var cuaEndpoint = config["LuminaConfiguration:CuaEndpoint"] ?? "";
        var llmEndpoint = config["CopilotApi:Endpoint"] ?? "http://localhost:4141";
        var llmModel = config["CopilotApi:Model"] ?? "gpt-4";

        // Load Partner Context configuration
        var partnerContext = new PartnerContextConfiguration();
        config.GetSection("PartnerContext").Bind(partnerContext);

        // Validate and log Partner Context
        if (partnerContext.HasPartnerContext)
        {
            if (!partnerContext.IsValid())
            {
                Console.WriteLine("[Warning] Partner Context has invalid hierarchy...");
            }
            Console.WriteLine($"[Config] {partnerContext.GetSummary()}");
        }
        else
        {
            Console.WriteLine("[Config] Partner Context: Not configured (optional)");
        }

        // Token provider (lazy init - only created when API is called)
        LauncherTokenProvider? launcherTokenProvider = null;
        Func<Task<string>> tokenProvider = async () =>
        {
            launcherTokenProvider ??= new LauncherTokenProvider(config);
            return await launcherTokenProvider.GetTokenAsync();
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
                    LuminaApiTokenProvider = async () => await tokenProvider(),
                    Partner = partnerContext.Partner,
                    ScenarioGroup = partnerContext.ScenarioGroup,
                    ScenarioName = partnerContext.ScenarioName,
                    Application = partnerContext.Application,
                    Component = partnerContext.Component
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
        MemoryService? memoryService = null;
        CustomerServiceAgent? customerServiceAgent = null;

        void EnsureInit()
        {
            if (searchApi == null && !string.IsNullOrEmpty(luminaEndpoint))
            {
                var proxy = GetOrCreateProxy();
                searchApi = new SearchApi(proxy);
                openApi = new OpenApi(proxy);
                findApi = new FindApi(proxy);
                memoryService = new MemoryService();
                llmExample = new LlmExample(searchApi, memoryService, llmEndpoint, llmModel);
                customerServiceAgent = new CustomerServiceAgent(searchApi, llmExample, llmEndpoint, llmModel);
            }
            if (cuaApi == null && !string.IsNullOrEmpty(cuaEndpoint))
            {
                cuaApi = new CuaApi(cuaEndpoint, tokenProvider, partnerContext);
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

        // POST /api/chat/customer-service - Customer service agent conversation
        app.MapPost("/api/chat/customer-service", async (CustomerServiceRequest req) =>
        {
            EnsureInit();
            if (customerServiceAgent == null)
                return Results.BadRequest(new { success = false, error = "Customer service agent not initialized" });

            try
            {
                var response = await customerServiceAgent.HandleMessageAsync(req.Message, req.ConversationId);
                return Results.Ok(response);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[API] Customer service agent error: {ex.Message}");
                return Results.BadRequest(new { success = false, error = ex.Message });
            }
        });

        // GET /api/conversation-history/{conversationId} - Get conversation history
        app.MapGet("/api/conversation-history/{conversationId}", (string conversationId) =>
        {
            EnsureInit();
            if (customerServiceAgent == null)
                return Results.BadRequest(new { success = false, error = "Customer service agent not initialized" });

            try
            {
                var history = customerServiceAgent.GetConversationHistory(conversationId);
                return Results.Ok(history);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[API] Get conversation history error: {ex.Message}");
                return Results.BadRequest(new { success = false, error = ex.Message });
            }
        });

        // GET /api/conversation-history/recent - Get recent conversations
        app.MapGet("/api/conversation-history/recent", (int limit = 10) =>
        {
            EnsureInit();
            if (customerServiceAgent == null)
                return Results.BadRequest(new { success = false, error = "Customer service agent not initialized" });

            try
            {
                var conversations = customerServiceAgent.GetRecentConversations(limit);
                return Results.Ok(conversations);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[API] Get recent conversations error: {ex.Message}");
                return Results.BadRequest(new { success = false, error = ex.Message });
            }
        });

        // GET /api/conversation-history/stats - Get conversation statistics
        app.MapGet("/api/conversation-history/stats", () =>
        {
            EnsureInit();
            if (customerServiceAgent == null)
                return Results.BadRequest(new { success = false, error = "Customer service agent not initialized" });

            try
            {
                var stats = customerServiceAgent.GetConversationStats();
                return Results.Ok(stats);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[API] Get conversation stats error: {ex.Message}");
                return Results.BadRequest(new { success = false, error = ex.Message });
            }
        });

        // GET /api/conversation-history/search - Search conversation history
        app.MapGet("/api/conversation-history/search", (string keyword, int maxResults = 50) =>
        {
            EnsureInit();
            if (customerServiceAgent == null)
                return Results.BadRequest(new { success = false, error = "Customer service agent not initialized" });

            if (string.IsNullOrWhiteSpace(keyword))
                return Results.BadRequest(new { success = false, error = "Keyword is required" });

            try
            {
                var results = customerServiceAgent.SearchHistory(keyword, maxResults);
                return Results.Ok(results);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[API] Search conversation history error: {ex.Message}");
                return Results.BadRequest(new { success = false, error = ex.Message });
            }
        });

        // Serve customer-service-agent.html as default
        app.MapGet("/", () => Results.Redirect("/customer-service-agent.html"));

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
        Console.WriteLine("║        Open: http://localhost:8400                   ║");
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
