using Microsoft.Lumina.Client.ApiProxy;

namespace MinimalApiCall;

/// <summary>
/// Lumina API Demo - Simple Web UI for PM learning.
/// Run: dotnet run → Open http://localhost:8402
/// </summary>
class Program
{
    static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseUrls("http://localhost:8402");

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
        SmartChatService? smartChatService = null;
        CompanionChatService? companionChatService = null;
        LocalKnowledgeService? knowledgeService = null;
        PptGeneratorService? pptGenerator = null;
        TextToSpeechService? ttsService = null;
        CompetitorAnalysisApi? competitorAnalysisApi = null;

        void EnsureInit()
        {
            if (searchApi == null && !string.IsNullOrEmpty(luminaEndpoint))
            {
                var proxy = GetOrCreateProxy();
                searchApi = new SearchApi(proxy);
                openApi = new OpenApi(proxy);
                findApi = new FindApi(proxy);
                llmExample = new LlmExample(searchApi, llmEndpoint, llmModel);
                knowledgeService = new LocalKnowledgeService();
                smartChatService = new SmartChatService(searchApi, llmEndpoint, llmModel, knowledgeService);
                companionChatService = new CompanionChatService(llmEndpoint, llmModel);
                
                // 初始化PPT生成服务（输出到知识库路径或临时目录）
                var pptOutputPath = knowledgeService.GetKnowledgePath() ?? Path.Combine(Path.GetTempPath(), "LuminaPPT");
                pptGenerator = new PptGeneratorService(pptOutputPath);
                
                // 初始化TTS服务（输出到临时目录）
                var ttsOutputPath = Path.Combine(Path.GetTempPath(), "LuminaTTS");
                ttsService = new TextToSpeechService(ttsOutputPath);
            }
            if (cuaApi == null && !string.IsNullOrEmpty(cuaEndpoint))
            {
                cuaApi = new CuaApi(cuaEndpoint, tokenProvider);
            }
            // Initialize CompetitorAnalysisApi after both searchApi and cuaApi are ready
            if (competitorAnalysisApi == null && searchApi != null && openApi != null)
            {
                competitorAnalysisApi = new CompetitorAnalysisApi(searchApi, openApi, cuaApi, llmEndpoint, llmModel);
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

        // POST /api/chat - Chat with web search + LLM (combined in one step)
        app.MapPost("/api/chat", async (WebChatRequest req) =>
        {
            EnsureInit();
            if (searchApi == null || llmExample == null) 
                return Results.BadRequest("Search or LLM not configured");

            try
            {
                // Always search and use LLM to generate answer
                var answer = await llmExample.SearchAndSummarizeAsync(req.Question, req.TopN);
                return Results.Ok(new { answer, timestamp = DateTime.UtcNow });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { answer = $"抱歉，处理您的问题时出现错误: {ex.Message}", timestamp = DateTime.UtcNow });
            }
        });

        // POST /api/smart-chat - 智能对话（两步走策略：理解意图 → 生成搜索词 → 并行搜索 → 生成回答）
        app.MapPost("/api/smart-chat", async (WebSmartChatRequest req) =>
        {
            EnsureInit();
            if (smartChatService == null) 
                return Results.BadRequest("Smart Chat service not configured");

            try
            {
                var response = await smartChatService.ChatAsync(req.SessionId, req.Question, req.TopN);
                return Results.Ok(response);
            }
            catch (Exception ex)
            {
                return Results.Ok(new SmartChatResponse 
                { 
                    SessionId = req.SessionId,
                    UserMessage = req.Question,
                    Answer = $"抱歉，处理您的问题时出现错误: {ex.Message}",
                    Error = ex.Message,
                    Timestamp = DateTime.UtcNow 
                });
            }
        });

        // POST /api/companion-chat - 陪伴聊天（纯对话，不搜索）
        app.MapPost("/api/companion-chat", async (WebCompanionChatRequest req) =>
        {
            EnsureInit();
            if (companionChatService == null)
                return Results.BadRequest("Companion Chat service not configured");

            try
            {
                var response = await companionChatService.ChatAsync(req.SessionId, req.Message, req.Personality);
                return Results.Ok(response);
            }
            catch (Exception ex)
            {
                return Results.Ok(new CompanionChatResponse
                {
                    SessionId = req.SessionId,
                    UserMessage = req.Message,
                    Reply = $"抱歉，我现在有点不舒服... {ex.Message}",
                    Error = ex.Message,
                    Timestamp = DateTime.UtcNow
                });
            }
        });

        // POST /api/companion-clear - 清除陪伴聊天历史
        app.MapPost("/api/companion-clear", (WebClearCompanionRequest req) =>
        {
            EnsureInit();
            if (companionChatService == null)
                return Results.BadRequest("Companion Chat service not configured");

            companionChatService.ClearHistory(req.SessionId);
            return Results.Ok(new { success = true, message = "对话历史已清除" });
        });

        // GET /api/companion-stats/{sessionId} - 获取对话统计
        app.MapGet("/api/companion-stats/{sessionId}", (string sessionId) =>
        {
            EnsureInit();
            if (companionChatService == null)
                return Results.BadRequest("Companion Chat service not configured");

            var stats = companionChatService.GetStats(sessionId);
            return Results.Ok(stats);
        });

        // POST /api/tts/generate - 生成语音
        app.MapPost("/api/tts/generate", async (WebTtsRequest req) =>
        {
            EnsureInit();
            if (ttsService == null)
            {
                // TTS服务未配置，直接返回使用Web Speech
                return Results.Ok(new 
                { 
                    success = true,
                    useWebSpeech = true,
                    voiceStyle = req.VoiceStyle ?? "甜美女生",
                    text = req.Text,
                    message = "TTS service not configured, using Web Speech API"
                });
            }

            try
            {
                var fileName = await ttsService.GenerateSpeechAsync(req.Text, req.VoiceStyle ?? "甜美女生");
                
                // 如果是web_speech前缀，返回特殊标记
                if (fileName.StartsWith("web_speech:"))
                {
                    return Results.Ok(new 
                    { 
                        success = true,
                        useWebSpeech = true,
                        voiceStyle = fileName.Replace("web_speech:", ""),
                        text = req.Text
                    });
                }

                return Results.Ok(new 
                { 
                    success = true,
                    useWebSpeech = false,
                    fileName,
                    audioUrl = $"/api/tts/audio/{fileName}"
                });
            }
            catch (Exception ex)
            {
                // 发生错误时也返回200，让前端使用Web Speech API
                return Results.Ok(new 
                { 
                    success = true,
                    useWebSpeech = true,
                    voiceStyle = req.VoiceStyle ?? "甜美女生",
                    text = req.Text,
                    message = $"TTS generation failed, using Web Speech API. Error: {ex.Message}"
                });
            }
        });

        // GET /api/tts/audio/{fileName} - 获取音频文件
        app.MapGet("/api/tts/audio/{fileName}", (string fileName) =>
        {
            var ttsPath = Path.Combine(Path.GetTempPath(), "LuminaTTS");
            var filePath = Path.Combine(ttsPath, fileName);

            if (!File.Exists(filePath))
                return Results.NotFound();

            var fileBytes = File.ReadAllBytes(filePath);
            return Results.File(fileBytes, "audio/mpeg", fileName);
        });

        // GET /api/tts/voices - 获取可用的声音列表
        app.MapGet("/api/tts/voices", () =>
        {
            EnsureInit();
            if (ttsService == null)
                return Results.BadRequest("TTS service not configured");

            var voices = ttsService.GetAvailableVoices();
            return Results.Ok(new { voices });
        });

        // POST /api/clear-chat-history - 清除对话历史
        app.MapPost("/api/clear-chat-history", (WebClearHistoryRequest req) =>
        {
            EnsureInit();
            if (smartChatService == null) 
                return Results.BadRequest("Smart Chat service not configured");

            smartChatService.ClearHistory(req.SessionId);
            return Results.Ok(new { success = true, message = "对话历史已清除" });
        });

        // POST /api/knowledge/set-path - 设置知识库路径
        app.MapPost("/api/knowledge/set-path", (WebSetKnowledgePathRequest req) =>
        {
            EnsureInit();
            if (knowledgeService == null)
                return Results.BadRequest("Knowledge service not configured");

            try
            {
                knowledgeService.SetKnowledgePath(req.Path);
                return Results.Ok(new { success = true, path = req.Path, message = "知识库路径已设置" });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { success = false, message = ex.Message });
            }
        });

        // GET /api/knowledge/get-path - 获取当前知识库路径
        app.MapGet("/api/knowledge/get-path", () =>
        {
            EnsureInit();
            if (knowledgeService == null)
                return Results.BadRequest("Knowledge service not configured");

            var path = knowledgeService.GetKnowledgePath();
            return Results.Ok(new { path });
        });

        // GET /api/knowledge/files - 获取知识库文件列表
        app.MapGet("/api/knowledge/files", () =>
        {
            EnsureInit();
            if (knowledgeService == null)
                return Results.BadRequest("Knowledge service not configured");

            var files = knowledgeService.GetFileList();
            return Results.Ok(new { files, count = files.Count });
        });

        // POST /api/knowledge/save - 手动保存对话
        app.MapPost("/api/knowledge/save", async (WebSaveConversationRequest req) =>
        {
            EnsureInit();
            if (knowledgeService == null)
                return Results.BadRequest("Knowledge service not configured");

            try
            {
                var filePath = await knowledgeService.SaveConversationAsync(req.Question, req.Answer, req.Format ?? "md");
                return Results.Ok(new { success = true, filePath, fileName = Path.GetFileName(filePath) });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { success = false, message = ex.Message });
            }
        });

        // POST /api/agent/set-path - 设置Agent路径
        app.MapPost("/api/agent/set-path", (WebSetAgentPathRequest req) =>
        {
            EnsureInit();
            if (knowledgeService == null)
                return Results.BadRequest("Knowledge service not configured");

            try
            {
                knowledgeService.SetAgentPath(req.Path);
                return Results.Ok(new { success = true, path = req.Path, message = "Agent路径已设置" });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { success = false, message = ex.Message });
            }
        });

        // GET /api/agent/get-path - 获取Agent路径
        app.MapGet("/api/agent/get-path", () =>
        {
            EnsureInit();
            if (knowledgeService == null)
                return Results.BadRequest("Knowledge service not configured");

            var path = knowledgeService.GetAgentPath();
            return Results.Ok(new { path });
        });

        // GET /api/agent/list - 获取所有可用的Agent
        app.MapGet("/api/agent/list", () =>
        {
            EnsureInit();
            if (knowledgeService == null)
                return Results.BadRequest("Knowledge service not configured");

            var agents = knowledgeService.GetAvailableAgents();
            return Results.Ok(new { agents, count = agents.Count });
        });

        // GET /api/agent/content/{agentName} - 获取指定Agent的内容
        app.MapGet("/api/agent/content/{agentName}", async (string agentName) =>
        {
            EnsureInit();
            if (knowledgeService == null)
                return Results.BadRequest("Knowledge service not configured");

            var content = await knowledgeService.GetAgentContentAsync(agentName);
            if (content == null)
                return Results.NotFound(new { success = false, message = $"Agent '{agentName}' not found" });

            return Results.Ok(new { success = true, agentName, content });
        });

        // POST /api/ppt/generate - 生成PPT文件
        app.MapPost("/api/ppt/generate", async (WebGeneratePptRequest req) =>
        {
            EnsureInit();
            if (pptGenerator == null)
                return Results.BadRequest("PPT Generator not configured");

            try
            {
                string filePath;
                
                if (req.Slides != null && req.Slides.Count > 0)
                {
                    // 使用结构化数据生成
                    filePath = pptGenerator.GenerateFromData(req.Title, req.Slides);
                }
                else if (!string.IsNullOrEmpty(req.Content))
                {
                    // 使用Markdown内容生成
                    filePath = pptGenerator.GenerateFromMarkdown(req.Title, req.Content);
                }
                else
                {
                    return Results.BadRequest(new { success = false, message = "需要提供Content或Slides" });
                }

                var fileName = Path.GetFileName(filePath);
                return Results.Ok(new 
                { 
                    success = true, 
                    fileName, 
                    filePath,
                    downloadUrl = $"/api/ppt/download/{fileName}",
                    message = "PPT生成成功！"
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { success = false, message = $"生成PPT失败: {ex.Message}" });
            }
        });

        // GET /api/ppt/download/{fileName} - 下载PPT文件
        app.MapGet("/api/ppt/download/{fileName}", (string fileName) =>
        {
            EnsureInit();
            if (pptGenerator == null || knowledgeService == null)
                return Results.BadRequest("Service not configured");

            var pptPath = knowledgeService.GetKnowledgePath() ?? Path.Combine(Path.GetTempPath(), "LuminaPPT");
            var filePath = Path.Combine(pptPath, fileName);

            if (!File.Exists(filePath))
                return Results.NotFound(new { success = false, message = "文件不存在" });

            var fileBytes = File.ReadAllBytes(filePath);
            return Results.File(fileBytes, "application/vnd.openxmlformats-officedocument.presentationml.presentation", fileName);
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

        // POST /api/competitor-analysis - Competitor analysis with AI-generated queries
        app.MapPost("/api/competitor-analysis", async (WebCompetitorAnalysisRequest req) =>
        {
            EnsureInit();
            if (competitorAnalysisApi == null) return Results.BadRequest("Competitor Analysis API not configured");

            var result = await competitorAnalysisApi.AnalyzeCompetitorsAsync(req.Competitors, req.WebsiteUrls, req.ResultsPerCompetitor);
            return Results.Ok(result);
        });

        // POST /api/competitor-analysis-report - Generate AI analysis report from competitor data
        app.MapPost("/api/competitor-analysis-report", async (CompetitorAnalysisResult analysisResult) =>
        {
            EnsureInit();
            if (competitorAnalysisApi == null) return Results.BadRequest("Competitor Analysis API not configured");

            var report = await competitorAnalysisApi.GenerateAnalysisReportAsync(analysisResult);
            return Results.Ok(new { report });
        });

        // POST /api/competitor-complete-report - One-click generate complete competitor report
        app.MapPost("/api/competitor-complete-report", async (WebCompetitorAnalysisRequest req) =>
        {
            EnsureInit();
            if (competitorAnalysisApi == null) return Results.BadRequest("Competitor Analysis API not configured");

            var fullReport = await competitorAnalysisApi.GenerateCompleteReportAsync(req.Competitors, req.WebsiteUrls, req.ResultsPerCompetitor);
            return Results.Ok(fullReport);
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
        Console.WriteLine("║        Open: http://localhost:8402                   ║");
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
record WebCompetitorAnalysisRequest(List<string> Competitors, Dictionary<string, string>? WebsiteUrls = null, int ResultsPerCompetitor = 5);
record WebChatRequest(string Question, int TopN = 5);
record WebSmartChatRequest(string SessionId, string Question, int TopN = 5);
record WebClearHistoryRequest(string SessionId);
record WebSetKnowledgePathRequest(string Path);
record WebSaveConversationRequest(string Question, string Answer, string? Format = "md");
record WebSetAgentPathRequest(string Path);
record WebGeneratePptRequest(string Title, string? Content = null, List<SlideData>? Slides = null);
record WebCompanionChatRequest(string SessionId, string Message, string? Personality = null);
record WebClearCompanionRequest(string SessionId);
record WebTtsRequest(string Text, string? VoiceStyle = "甜美女生");
