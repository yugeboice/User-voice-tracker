using Microsoft.Lumina.Client.ApiProxy;
using MinimalApiCall.Internal;
using MinimalApiCall.Services;

namespace MinimalApiCall;

/// <summary>
/// Request model for Phase 2 - analyze selected models
/// </summary>
public record ArenaPhase2Request(List<string> SelectedModels);

/// <summary>
/// Lumina API Demo - Simple Web UI for PM learning.
/// Run: dotnet run → Open http://localhost:8400
/// </summary>
class Program
{
    static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Configuration.AddJsonFile(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "appsettings.json"), optional: true, reloadOnChange: true);
        builder.WebHost.UseUrls("http://localhost:8406");

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
                Console.WriteLine("[Warning] Partner Context has invalid hierarchy. Fields are hierarchical - you cannot skip levels.");
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
                    // Apply Partner Context
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
        ConversationHistoryService? conversationHistoryService = null;

        void EnsureInit()
        {
            if (searchApi == null && !string.IsNullOrEmpty(luminaEndpoint))
            {
                var proxy = GetOrCreateProxy();
                searchApi = new SearchApi(proxy);
                openApi = new OpenApi(proxy);
                findApi = new FindApi(proxy);
                memoryService = new MemoryService();
                conversationHistoryService = new ConversationHistoryService();

                // [Teaching] Clean initialization - no more circular dependency workaround!
                // Step 1: Create LlmExample first (without memory extraction)
                llmExample = new LlmExample(searchApi, memoryService, null, conversationHistoryService, llmEndpoint, llmModel);

                // Step 2: Create MemoryExtractionService with a delegate to LlmExample's method
                // This is cleaner than the previous "temp instance" approach
                var memoryExtractionService = new MemoryExtractionService(llmExample.ExecuteLlmRequestAsync);

                // Step 3: Inject the memory extraction service
                llmExample.SetMemoryExtractionService(memoryExtractionService);
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

        // POST /api/chat - Chat with Memory (supports both Hard Memory and Conditional Memory)
        app.MapPost("/api/chat", async (WebChatRequest req) =>
        {
            EnsureInit();
            if (llmExample == null) return Results.BadRequest("LLM not configured");

            // [Teaching] UseConditionalMemory controls memory injection strategy:
            // - false (default): Hard Memory - all memories are injected
            // - true: Conditional Memory - LLM selects relevant memories
            var result = await llmExample.ChatAsync(req.Message, req.UseConditionalMemory);
            // [Teaching] Return both the response and any saved memory info
            return Results.Ok(new { 
                response = result.Response, 
                savedMemory = result.SavedMemory,
                memoryMode = req.UseConditionalMemory ? "conditional" : "hard"
            });
        });

        // GET /api/profile - Get user profile
        app.MapGet("/api/profile", async () =>
        {
            EnsureInit();
            if (memoryService == null) return Results.BadRequest("Memory service not initialized");

            var profile = await memoryService.LoadUserProfileAsync();
            return Results.Ok(profile);
        });

        // PUT /api/profile - Update user profile
        app.MapPut("/api/profile", async (UserProfile profile) =>
        {
            EnsureInit();
            if (memoryService == null) return Results.BadRequest("Memory service not initialized");

            var success = await memoryService.SaveUserProfileAsync(profile);
            return success ? Results.Ok(new { success = true }) : Results.BadRequest(new { success = false });
        });

        // GET /api/memories - Get all memories
        app.MapGet("/api/memories", async () =>
        {
            EnsureInit();
            if (memoryService == null) return Results.BadRequest("Memory service not initialized");

            var memories = await memoryService.GetMemoriesAsync();
            return Results.Ok(memories);
        });

        // DELETE /api/memories/{id} - Delete a memory
        app.MapDelete("/api/memories/{id}", async (string id) =>
        {
            EnsureInit();
            if (memoryService == null) return Results.BadRequest("Memory service not initialized");

            var success = await memoryService.DeleteMemoryAsync(id);
            return success ? Results.Ok(new { success = true }) : Results.BadRequest(new { success = false });
        });

        // GET /api/conversations - Get conversation history
        app.MapGet("/api/conversations", async (int? limit) =>
        {
            EnsureInit();
            if (conversationHistoryService == null) return Results.BadRequest("Conversation history service not initialized");

            var conversations = await conversationHistoryService.GetConversationsAsync(limit ?? 50);
            return Results.Ok(conversations);
        });

        // DELETE /api/conversations/{id} - Delete a specific conversation
        app.MapDelete("/api/conversations/{id}", async (string id) =>
        {
            EnsureInit();
            if (conversationHistoryService == null) return Results.BadRequest("Conversation history service not initialized");

            var success = await conversationHistoryService.DeleteConversationAsync(id);
            return success ? Results.Ok(new { success = true }) : Results.BadRequest(new { success = false });
        });

        // DELETE /api/conversations - Clear all conversation history
        app.MapDelete("/api/conversations", async () =>
        {
            EnsureInit();
            if (conversationHistoryService == null) return Results.BadRequest("Conversation history service not initialized");

            var success = await conversationHistoryService.ClearAllConversationsAsync();
            return success ? Results.Ok(new { success = true }) : Results.BadRequest(new { success = false });
        });

        // ========== ArenaWatch API Routes ==========
        ArenaService? arenaService = null;
        
        ArenaService GetArenaService()
        {
            if (arenaService == null)
            {
                EnsureInit();
                arenaService = new ArenaService(cuaApi, llmExample, searchApi, conversationHistoryService, memoryService);
            }
            return arenaService;
        }

        // POST /api/arena/ask - Arena 智能问答
        app.MapPost("/api/arena/ask", async (ArenaAskRequest req) =>
        {
            var service = GetArenaService();
            var result = await service.AskAsync(req);
            return Results.Ok(result);
        });

        // GET /api/arena/screenshots - 获取截图历史
        app.MapGet("/api/arena/screenshots", () =>
        {
            var service = GetArenaService();
            var history = service.GetScreenshotHistory();
            return Results.Ok(history);
        });

        // GET /api/arena/cache - 获取缓存状态
        app.MapGet("/api/arena/cache", () =>
        {
            var service = GetArenaService();
            var status = service.GetCacheStatus();
            return Results.Ok(status);
        });

        // GET /api/arena/leaderboards - 获取支持的榜单列表
        app.MapGet("/api/arena/leaderboards", () =>
        {
            var leaderboards = ArenaService.Leaderboards.Values
                .Select(c => new { c.Id, c.Name, c.Url, c.Description })
                .ToList();
            return Results.Ok(leaderboards);
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
// [Teaching] UseConditionalMemory: When true, LLM decides which memories are relevant (Conditional Memory)
//            When false, all memories are injected (Hard Memory)
record WebChatRequest(string Message, bool UseConditionalMemory = false);
