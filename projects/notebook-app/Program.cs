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
        builder.Configuration.AddJsonFile(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "appsettings.json"), optional: true, reloadOnChange: true);
        builder.WebHost.UseUrls("http://localhost:8403");

        var app = builder.Build();
        
        // Serve files from wwwroot
        app.UseStaticFiles();
        
        // Serve notebook frontend files from notebooks/Frontend folder
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
                Path.Combine(Directory.GetCurrentDirectory(), "notebooks", "Frontend")),
            RequestPath = "/notebooks/Frontend"
        });

        // Load config
        var config = app.Configuration;
        var luminaEndpoint = config["LuminaConfiguration:ApiEndpoint"] ?? "";
        var cuaEndpoint = config["LuminaConfiguration:CuaEndpoint"] ?? "";
        var llmEndpoint = config["CopilotApi:Endpoint"] ?? "http://localhost:4242";
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
        NotebookApi? notebookApi = null;
        ChatApi? chatApi = null;
        StudioApi? studioApi = null;
        
        // Initialize storage, image service, and skill invoker
        var storage = new NotebookStorage("notebooks/Data");
        var imageService = new ImageGenerationService(llmEndpoint);
        var skillInvoker = new SkillInvoker("python", Path.Combine(Directory.GetCurrentDirectory(), "skills"));

        void EnsureInit()
        {
            if (searchApi == null && !string.IsNullOrEmpty(luminaEndpoint))
            {
                var proxy = GetOrCreateProxy();
                searchApi = new SearchApi(proxy);
                openApi = new OpenApi(proxy);
                findApi = new FindApi(proxy);
                llmExample = new LlmExample(searchApi, llmEndpoint, llmModel);
                notebookApi = new NotebookApi(searchApi, openApi, storage);
                chatApi = new ChatApi(notebookApi, storage, llmEndpoint, llmModel, searchApi);
                studioApi = new StudioApi(notebookApi, storage, llmEndpoint, llmModel, imageService, skillInvoker);
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

        // ========== Notebook Management API Routes ==========
        
        // GET /api/notebooks - Get all notebooks
        app.MapGet("/api/notebooks", () =>
        {
            EnsureInit();
            if (notebookApi == null) return Results.BadRequest("Notebook API not initialized");

            var notebooks = notebookApi.GetAllNotebooks();
            return Results.Ok(new { notebooks });
        });

        // GET /api/notebooks/{id} - Get a specific notebook
        app.MapGet("/api/notebooks/{id}", (string id) =>
        {
            EnsureInit();
            if (notebookApi == null) return Results.BadRequest("Notebook API not initialized");

            var notebook = notebookApi.GetNotebook(id);
            if (notebook == null) return Results.NotFound("Notebook not found");

            return Results.Ok(notebook);
        });

        // POST /api/notebooks - Create a new notebook
        app.MapPost("/api/notebooks", (CreateNotebookRequest req) =>
        {
            EnsureInit();
            if (notebookApi == null) return Results.BadRequest("Notebook API not initialized");

            var notebook = notebookApi.CreateNotebook(req.Title, req.Description);
            return Results.Ok(new { success = true, notebook });
        });

        // PUT /api/notebooks/{id} - Update a notebook
        app.MapPut("/api/notebooks/{id}", (string id, UpdateNotebookRequest req) =>
        {
            EnsureInit();
            if (notebookApi == null) return Results.BadRequest("Notebook API not initialized");

            var success = notebookApi.UpdateNotebook(id, req.Title, req.Description);
            if (!success) return Results.NotFound("Notebook not found");

            return Results.Ok(new { success = true });
        });

        // DELETE /api/notebooks/{id} - Delete a notebook
        app.MapDelete("/api/notebooks/{id}", (string id) =>
        {
            EnsureInit();
            if (notebookApi == null) return Results.BadRequest("Notebook API not initialized");

            var deleted = notebookApi.DeleteNotebook(id);
            if (!deleted) return Results.NotFound("Notebook not found");

            return Results.Ok(new { success = true });
        });

        // ========== Notebook Source API Routes ==========
        
        // POST /api/notebook/sources/text - Add text source
        app.MapPost("/api/notebook/sources/text", async (AddTextSourceRequest req) =>
        {
            EnsureInit();
            if (notebookApi == null) return Results.BadRequest("Notebook API not initialized");
            if (string.IsNullOrEmpty(req.NotebookId)) return Results.BadRequest("NotebookId is required");

            var source = await notebookApi.AddTextSourceAsync(req.NotebookId, req.Title, req.Content);
            return Results.Ok(source);
        });

        // POST /api/notebook/sources/url - Add URL source
        app.MapPost("/api/notebook/sources/url", async (AddUrlSourceRequest req) =>
        {
            EnsureInit();
            if (notebookApi == null) return Results.BadRequest("Notebook API not initialized");
            if (string.IsNullOrEmpty(req.NotebookId)) return Results.BadRequest("NotebookId is required");

            try
            {
                var source = await notebookApi.AddUrlSourceAsync(req.NotebookId, req.Url);
                return Results.Ok(source);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(ex.Message);
            }
        });

        // POST /api/notebook/{notebookId}/sources/file - Add file source
        app.MapPost("/api/notebook/{notebookId}/sources/file", async (string notebookId, HttpRequest request) =>
        {
            EnsureInit();
            if (notebookApi == null) return Results.BadRequest("Notebook API not initialized");
            if (string.IsNullOrEmpty(notebookId)) return Results.BadRequest("NotebookId is required");

            try
            {
                if (!request.HasFormContentType)
                {
                    return Results.BadRequest("Request must be multipart/form-data");
                }

                var form = await request.ReadFormAsync();
                var file = form.Files.GetFile("file");
                
                if (file == null || file.Length == 0)
                {
                    return Results.BadRequest("No file uploaded");
                }

                var source = await notebookApi.AddFileSourceAsync(notebookId, file);
                return Results.Ok(source);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                return Results.BadRequest($"File upload failed: {ex.Message}");
            }
        });

        // POST /api/notebook/search/preview - Preview search results
        app.MapPost("/api/notebook/search/preview", async (AddSearchSourceRequest req) =>
        {
            EnsureInit();
            if (notebookApi == null) return Results.BadRequest("Notebook API not initialized");

            try
            {
                var results = await notebookApi.PreviewSearchAsync(req.Query);
                return Results.Ok(results);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(ex.Message);
            }
        });

        // POST /api/notebook/sources/search - Add selected search sources
        app.MapPost("/api/notebook/sources/search", async (AddSearchSourcesRequest req) =>
        {
            EnsureInit();
            if (notebookApi == null) return Results.BadRequest("Notebook API not initialized");
            if (string.IsNullOrEmpty(req.NotebookId)) return Results.BadRequest("NotebookId is required");

            try
            {
                var sources = await notebookApi.AddSearchSourcesAsync(req.NotebookId, req.Query, req.SelectedIndices, req.SearchResults);
                return Results.Ok(sources);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(ex.Message);
            }
        });

        // GET /api/notebook/sources - Get all sources for a notebook
        app.MapGet("/api/notebook/sources", (string? notebookId) =>
        {
            EnsureInit();
            if (notebookApi == null) return Results.BadRequest("Notebook API not initialized");
            if (string.IsNullOrEmpty(notebookId)) return Results.BadRequest("notebookId query parameter is required");

            var sources = notebookApi.GetAllSources(notebookId);
            return Results.Ok(sources);
        });

        // DELETE /api/notebook/sources/{id} - Delete a source
        app.MapDelete("/api/notebook/sources/{id}", (string id, string? notebookId) =>
        {
            EnsureInit();
            if (notebookApi == null) return Results.BadRequest("Notebook API not initialized");
            if (string.IsNullOrEmpty(notebookId)) return Results.BadRequest("notebookId query parameter is required");

            var deleted = notebookApi.DeleteSource(notebookId, id);
            if (!deleted) return Results.NotFound("Source not found");

            return Results.Ok(new { success = true });
        });

        // DELETE /api/notebook/clear - Clear all sources and generations for a notebook
        app.MapDelete("/api/notebook/clear", (string? notebookId) =>
        {
            EnsureInit();
            if (notebookApi == null || studioApi == null) 
                return Results.BadRequest("APIs not initialized");
            if (string.IsNullOrEmpty(notebookId)) return Results.BadRequest("notebookId query parameter is required");

            notebookApi.ClearAllSources(notebookId);
            studioApi.ClearAllGenerations(notebookId);

            return Results.Ok(new { success = true, message = "All data cleared" });
        });

        // ========== Chat API Routes ==========

        // POST /api/notebook/chat - Send chat message with RAG context
        app.MapPost("/api/notebook/chat", async (ChatRequest req) =>
        {
            EnsureInit();
            if (chatApi == null) return Results.BadRequest("Chat API not initialized");
            if (string.IsNullOrEmpty(req.NotebookId)) return Results.BadRequest("NotebookId is required");

            try
            {
                var response = await chatApi.SendMessageAsync(req.NotebookId, req.Message, req.IncludeHistory, req.EnableSearch);
                return Results.Ok(response);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(ex.Message);
            }
        });

        // GET /api/notebook/chat/history - Get conversation history
        app.MapGet("/api/notebook/chat/history", (string? notebookId) =>
        {
            EnsureInit();
            if (chatApi == null) return Results.BadRequest("Chat API not initialized");
            if (string.IsNullOrEmpty(notebookId)) return Results.BadRequest("notebookId query parameter is required");

            var history = chatApi.GetHistory(notebookId);
            return Results.Ok(history);
        });

        // DELETE /api/notebook/chat/history - Clear conversation history
        app.MapDelete("/api/notebook/chat/history", (string? notebookId) =>
        {
            EnsureInit();
            if (chatApi == null) return Results.BadRequest("Chat API not initialized");
            if (string.IsNullOrEmpty(notebookId)) return Results.BadRequest("notebookId query parameter is required");

            chatApi.ClearHistory(notebookId);
            return Results.Ok(new { success = true });
        });

        // ========== Studio API Routes ==========

        // POST /api/notebook/studio/generate - Generate studio content
        app.MapPost("/api/notebook/studio/generate", (GenerateRequest req) =>
        {
            EnsureInit();
            if (studioApi == null) return Results.BadRequest("Studio API not initialized");
            if (string.IsNullOrEmpty(req.NotebookId)) return Results.BadRequest("NotebookId is required");

            try
            {
                // Start generation asynchronously in background
                var generation = studioApi.StartGeneration(req.NotebookId, req.Type, req.CustomPrompt);
                return Results.Ok(generation);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(ex.Message);
            }
        });

        // GET /api/notebook/studio/generations - Get all generations
        app.MapGet("/api/notebook/studio/generations", (string? notebookId) =>
        {
            EnsureInit();
            if (studioApi == null) return Results.BadRequest("Studio API not initialized");
            if (string.IsNullOrEmpty(notebookId)) return Results.BadRequest("notebookId query parameter is required");

            var generations = studioApi.GetAllGenerations(notebookId);
            return Results.Ok(generations);
        });

        // GET /api/notebook/studio/generations/{id} - Get specific generation
        app.MapGet("/api/notebook/studio/generations/{id}", (string id, string? notebookId) =>
        {
            EnsureInit();
            if (studioApi == null) return Results.BadRequest("Studio API not initialized");
            if (string.IsNullOrEmpty(notebookId)) return Results.BadRequest("notebookId query parameter is required");

            var generation = studioApi.GetGeneration(notebookId, id);
            if (generation == null) return Results.NotFound("Generation not found");

            return Results.Ok(generation);
        });

        // DELETE /api/notebook/studio/generations/{id} - Delete generation
        app.MapDelete("/api/notebook/studio/generations/{id}", (string id, string? notebookId) =>
        {
            EnsureInit();
            if (studioApi == null) return Results.BadRequest("Studio API not initialized");
            if (string.IsNullOrEmpty(notebookId)) return Results.BadRequest("notebookId query parameter is required");

            var deleted = studioApi.DeleteGeneration(notebookId, id);
            if (!deleted) return Results.NotFound("Generation not found");

            return Results.Ok(new { success = true });
        });

        // GET /api/notebook/images/{notebookId}/{filename} - Get generated image
        app.MapGet("/api/notebook/images/{notebookId}/{filename}", (string notebookId, string filename) =>
        {
            try
            {
                var imagesDir = storage.GetImagesDirectory(notebookId);
                var imagePath = Path.Combine(imagesDir, filename);

                if (!File.Exists(imagePath))
                    return Results.NotFound("Image not found");

                var imageBytes = File.ReadAllBytes(imagePath);
                return Results.File(imageBytes, "image/png");
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error loading image: {ex.Message}");
            }
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

// Notebook management request models
record CreateNotebookRequest(string Title, string? Description);
record UpdateNotebookRequest(string? Title, string? Description);
