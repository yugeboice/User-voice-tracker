using System.Text.Json;
using System.Text.RegularExpressions;
using System.Net.Http;
using Microsoft.Lumina.Client.Models.Sonicberry;
using MinimalApiCall;

namespace MinimalApiCall.Services;

#region Data Models

/// <summary>
/// 榜单配置
/// </summary>
public record LeaderboardConfig(
    string Id,
    string Name,
    string Url,
    string[] Keywords,
    string Description
);

/// <summary>
/// Arena 问答请求
/// </summary>
public record ArenaAskRequest(
    string Question,
    string? LeaderboardId = null,
    string? Model = null
);

/// <summary>
/// Arena 问答响应
/// </summary>
public record ArenaAskResponse(
    bool Success,
    LeaderboardInfo? Leaderboard,
    string? Screenshot,
    string? Response,
    string? Error = null,
    DateTime? CapturedAt = null,
    List<LeaderboardResult>? AllResults = null,
    List<SourceReference>? Sources = null
);

/// <summary>
/// 榜单信息
/// </summary>
public record LeaderboardInfo(string Id, string Name, string Url, string? Description = null);

/// <summary>
/// 来源引用
/// </summary>
public record SourceReference(string Title, string Url, string? Snippet = null);

/// <summary>
/// 缓存的榜单数据
/// </summary>
public record CachedLeaderboardData(
    string Screenshot,
    DateTime CapturedAt,
    string? Content = null
);

/// <summary>
/// 榜单结果（用于多榜单查询）
/// </summary>
public record LeaderboardResult(
    LeaderboardInfo Leaderboard,
    string? Screenshot,
    DateTime CapturedAt
);

/// <summary>
/// 截图历史记录
/// </summary>
public record ScreenshotHistory(
    string LeaderboardId,
    string FileName,
    string Url,
    DateTime CapturedAt
);

#endregion

/// <summary>
/// 🚀 ArenaWatch - 智能模型评测助手
/// 
/// 【核心功能】
/// - 多榜单聚合：LMSYS Arena、HELM、HuggingFace Open LLM
/// - 两区块搜索：榜单官方数据 + 外部评测新闻
/// - 智能模型识别：支持 50+ 模型名称变体
/// - LLM 问答：基于实际数据的智能分析
/// </summary>
public class ArenaService
{
    private readonly CuaApi? _cuaApi;
    private readonly LlmExample? _llmExample;
    private readonly SearchApi? _searchApi;
    private readonly ConversationHistoryService? _conversationService;
    private readonly MemoryService? _memoryService;

    // 缓存系统
    private static readonly Dictionary<string, CachedLeaderboardData> _cache = new();
    private static readonly Dictionary<string, List<SourceReference>> _sourcesCache = new();
    private static readonly object _cacheLock = new();
    private static readonly TimeSpan CacheExpiry = TimeSpan.FromHours(1);

    // 用于直接抓取网页内容
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(25) };

    /// <summary>
    /// 支持的评测榜单
    /// </summary>
    public static readonly Dictionary<string, LeaderboardConfig> Leaderboards = new()
    {
        ["lmsys"] = new LeaderboardConfig(
            Id: "lmsys",
            Name: "LM Arena",
            Url: "https://lmarena.ai/leaderboard",
            Keywords: new[] { "lmsys", "arena", "chatbot arena", "lmarena", "elo" },
            Description: "基于真实用户投票的人类偏好排名"
        ),
        ["helm"] = new LeaderboardConfig(
            Id: "helm",
            Name: "HELM Benchmark",
            Url: "https://crfm.stanford.edu/helm/capabilities/latest/#/leaderboard",
            Keywords: new[] { "helm", "stanford", "crfm", "holistic" },
            Description: "斯坦福大学的全面学术评测榜单"
        ),
        ["huggingface"] = new LeaderboardConfig(
            Id: "huggingface",
            Name: "HuggingFace Open LLM",
            Url: "https://huggingface.co/spaces/open-llm-leaderboard/open_llm_leaderboard",
            Keywords: new[] { "huggingface", "hf", "open llm", "open-llm" },
            Description: "开源模型评测榜单"
        )
    };

    public ArenaService(
        CuaApi? cuaApi = null,
        LlmExample? llmExample = null,
        SearchApi? searchApi = null,
        ConversationHistoryService? conversationService = null,
        MemoryService? memoryService = null)
    {
        _cuaApi = cuaApi;
        _llmExample = llmExample;
        _searchApi = searchApi;
        _conversationService = conversationService;
        _memoryService = memoryService;

        // 启动时加载缓存
        LoadCacheFromFiles();
    }

    #region Public API

    /// <summary>
    /// 智能问答入口 - 增强日志版
    /// </summary>
    public async Task<ArenaAskResponse> AskAsync(ArenaAskRequest request)
    {
        try
        {
            Console.WriteLine($"\n{'=' * 60}");
            Console.WriteLine($"[Arena] 🚀 收到问题: {request.Question}");
            Console.WriteLine($"[Arena] 时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"{'=' * 60}");

            // 1. 匹配榜单
            var leaderboardId = request.LeaderboardId ?? MatchLeaderboard(request.Question);

            // 如果需要查询所有榜单
            if (leaderboardId == null && ShouldQueryAllLeaderboards(request.Question))
            {
                Console.WriteLine($"[Arena] 📊 检测到多榜单查询，将查询所有 {Leaderboards.Count} 个榜单");
                return await AskAllLeaderboardsAsync(request.Question);
            }

            // 默认使用 HELM
            leaderboardId ??= "helm";

            if (!Leaderboards.TryGetValue(leaderboardId, out var config))
            {
                Console.WriteLine($"[Arena] ❌ 不支持的榜单: {leaderboardId}");
                return new ArenaAskResponse(
                    Success: false,
                    Leaderboard: null,
                    Screenshot: null,
                    Response: null,
                    Error: $"不支持的榜单: {leaderboardId}"
                );
            }

            Console.WriteLine($"[Arena] 📌 匹配到榜单: {config.Name}");
            Console.WriteLine($"[Arena] 🔗 URL: {config.Url}");

            // 2. 获取内容和截图
            string? screenshot = null;
            string? content = null;
            DateTime capturedAt = DateTime.UtcNow;

            // 尝试使用缓存
            var cached = GetCachedData(leaderboardId);
            if (cached != null)
            {
                var cacheAge = DateTime.UtcNow - cached.CapturedAt;
                Console.WriteLine($"[Arena] 💾 使用缓存数据 (缓存于 {cacheAge.TotalMinutes:F0} 分钟前)");
                screenshot = cached.Screenshot;
                capturedAt = cached.CapturedAt;
            }

            // 搜索、网页抓取和截图并行执行
            Task<string?>? searchTask = null;
            Task<string?>? screenshotTask = null;
            Task<string?>? htmlFetchTask = null;

            // Step 1: 启动搜索 (Block 1: 榜单精确搜索)
            if (_searchApi != null)
            {
                Console.WriteLine($"[Arena] 🔍 Step 1: 搜索 {config.Name} 相关内容...");
                searchTask = FetchLeaderboardContentAsync(config, request.Question);
            }
            else
            {
                Console.WriteLine($"[Arena] ⚠ SearchApi 不可用，跳过搜索");
            }

            // Step 1b: 同时直接抓取榜单网页内容
            Console.WriteLine($"[Arena] 🌐 Step 1b: 直接抓取榜单网页...");
            htmlFetchTask = FetchLeaderboardHtmlAsync(config, request.Question);

            // Step 2: 启动截图（带超时，仅供用户参考）
            if (screenshot == null && _cuaApi != null)
            {
                Console.WriteLine($"[Arena] 📸 Step 2: 截取榜单页面（后台，超时30秒）...");

                // 启动实际的截图任务（无超时）
                var fullScreenshotTask = CaptureLeaderboardWithRetryAsync(config.Url, 0, leaderboardId);

                // 创建一个带超时的包装器用于前台等待
                screenshotTask = WaitForScreenshotWithTimeoutAsync(fullScreenshotTask, TimeSpan.FromSeconds(30));

                // 后台继续执行完整截图任务，即使前台超时也会完成并保存
                _ = ContinueScreenshotInBackgroundAsync(fullScreenshotTask, leaderboardId, content);
            }
            else if (_cuaApi == null)
            {
                Console.WriteLine($"[Arena] ⚠ CuaApi 不可用，跳过截图");
            }

            // 等待搜索完成（优先级高）
            if (searchTask != null)
            {
                content = await searchTask;
                Console.WriteLine($"[Arena] 🔍 Step 1 完成: 获取内容 {content?.Length ?? 0} 字符");
            }

            // 等待网页抓取完成，合并到content
            if (htmlFetchTask != null)
            {
                try
                {
                    var htmlContent = await htmlFetchTask;
                    if (!string.IsNullOrEmpty(htmlContent))
                    {
                        Console.WriteLine($"[Arena] 🌐 Step 1b 完成: 网页抓取 {htmlContent.Length} 字符");
                        content = string.IsNullOrEmpty(content)
                            ? htmlContent
                            : $"{content}\n\n【榜单网页数据】\n{htmlContent}";
                    }
                    else
                    {
                        Console.WriteLine($"[Arena] ⚠ Step 1b: 网页抓取无结果");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Arena] ⚠ Step 1b: 网页抓取异常: {ex.Message}");
                }
            }

            // 尝试等截图（仅供用户参考，不影响文字回答）
            if (screenshotTask != null)
            {
                try
                {
                    screenshot = await screenshotTask;
                    capturedAt = DateTime.UtcNow;

                    if (!string.IsNullOrEmpty(screenshot))
                    {
                        UpdateCache(leaderboardId, screenshot, capturedAt, content);
                        Console.WriteLine($"[Arena] 📸 Step 2 完成: 截图成功");
                    }
                    else
                    {
                        Console.WriteLine($"[Arena] ⚠ Step 2: 截图前台超时，后台继续执行...");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Arena] ⚠ Step 2: 截图异常: {ex.Message}");
                }
            }

            // 3. 生成回答
            Console.WriteLine($"[Arena] 🤖 Step 3: AI 生成回答...");
            var response = await GenerateSingleResponseAsync(request.Question, config, screenshot != null, content);
            Console.WriteLine($"[Arena] 🤖 Step 3 完成: 回答 {response?.Length ?? 0} 字符");

            // 4. 保存对话
            if (_conversationService != null && !string.IsNullOrEmpty(response))
            {
                await _conversationService.SaveConversationAsync(
                    $"[Arena] {request.Question} (榜单: {config.Name})",
                    response
                );
            }

            // 保存截图到文件
            if (!string.IsNullOrEmpty(screenshot))
            {
                SaveScreenshotToFile(leaderboardId, screenshot);
            }

            Console.WriteLine($"[Arena] ✅ 请求处理完成");
            Console.WriteLine($"{'=' * 60}\n");

            return new ArenaAskResponse(
                Success: true,
                Leaderboard: new LeaderboardInfo(config.Id, config.Name, config.Url, config.Description),
                Screenshot: screenshot,
                Response: response,
                CapturedAt: capturedAt
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Arena] ❌ 错误: {ex.Message}");
            Console.WriteLine($"[Arena] Stack: {ex.StackTrace}");
            return new ArenaAskResponse(
                Success: false,
                Leaderboard: null,
                Screenshot: null,
                Response: null,
                Error: $"处理请求时出错: {ex.Message}"
            );
        }
    }

    /// <summary>
    /// 获取截图历史
    /// </summary>
    public List<ScreenshotHistory> GetScreenshotHistory()
    {
        var result = new List<ScreenshotHistory>();
        var baseDir = Path.Combine("wwwroot", "screenshots");

        if (!Directory.Exists(baseDir)) return result;

        foreach (var dir in Directory.GetDirectories(baseDir))
        {
            var lbId = Path.GetFileName(dir);
            foreach (var file in Directory.GetFiles(dir, "*.png")
                .OrderByDescending(f => File.GetCreationTime(f))
                .Take(10))
            {
                var fileName = Path.GetFileName(file);
                result.Add(new ScreenshotHistory(
                    LeaderboardId: lbId,
                    FileName: fileName,
                    Url: $"/screenshots/{lbId}/{fileName}",
                    CapturedAt: File.GetCreationTime(file)
                ));
            }
        }

        return result.OrderByDescending(s => s.CapturedAt).ToList();
    }

    /// <summary>
    /// 获取缓存状态
    /// </summary>
    public Dictionary<string, string> GetCacheStatus()
    {
        lock (_cacheLock)
        {
            return _cache.ToDictionary(
                kv => kv.Key,
                kv =>
                {
                    var age = DateTime.UtcNow - kv.Value.CapturedAt;
                    var valid = age < CacheExpiry;
                    return $"{(valid ? "✓" : "✗")} {age.TotalMinutes:F0}分钟前";
                }
            );
        }
    }

    #endregion

    #region Multi-Leaderboard Query

    /// <summary>
    /// 查询所有榜单
    /// </summary>
    private async Task<ArenaAskResponse> AskAllLeaderboardsAsync(string question)
    {
        var allResults = new List<LeaderboardResult>();
        var leaderboardContents = new Dictionary<string, string>();

        foreach (var (id, config) in Leaderboards)
        {
            Console.WriteLine($"[Arena] 正在处理 {config.Name}...");

            // 搜索内容
            string? content = null;
            if (_searchApi != null)
            {
                content = await FetchLeaderboardContentAsync(config, question);
                if (!string.IsNullOrEmpty(content))
                {
                    leaderboardContents[id] = content;
                }
            }

            // 获取截图
            string? screenshot = null;
            var cached = GetCachedData(id);

            if (cached != null)
            {
                screenshot = cached.Screenshot;
                allResults.Add(new LeaderboardResult(
                    new LeaderboardInfo(id, config.Name, config.Url, config.Description),
                    screenshot,
                    cached.CapturedAt
                ));
            }
            else if (_cuaApi != null)
            {
                screenshot = await CaptureLeaderboardWithRetryAsync(config.Url, 2, id);
                var now = DateTime.UtcNow;

                if (!string.IsNullOrEmpty(screenshot))
                {
                    UpdateCache(id, screenshot, now, content);
                    SaveScreenshotToFile(id, screenshot);

                    allResults.Add(new LeaderboardResult(
                        new LeaderboardInfo(id, config.Name, config.Url, config.Description),
                        screenshot,
                        now
                    ));
                }
            }
        }

        // Block 2: 外部来源搜索
        var externalNews = await FetchExternalNewsAsync(question);

        // 生成综合回答
        var response = await GenerateMultiLeaderboardResponseAsync(
            question, allResults, leaderboardContents, externalNews);

        // 收集来源
        var allSources = new List<SourceReference>();
        lock (_cacheLock)
        {
            foreach (var id in leaderboardContents.Keys)
            {
                if (_sourcesCache.TryGetValue(id, out var sources))
                    allSources.AddRange(sources);
            }
            if (_sourcesCache.TryGetValue("external_news", out var news))
                allSources.AddRange(news);
        }

        var first = allResults.FirstOrDefault();
        return new ArenaAskResponse(
            Success: true,
            Leaderboard: first?.Leaderboard,
            Screenshot: first?.Screenshot,
            Response: response,
            CapturedAt: DateTime.UtcNow,
            AllResults: allResults,
            Sources: allSources.Take(6).ToList()
        );
    }

    #endregion

    #region Block 1: 榜单精确搜索

    /// <summary>
    /// 模型关键词列表 - 支持连字符和空格格式
    /// 包含模型家族信息用于回退搜索
    /// </summary>
    private static readonly Dictionary<string, string[]> ModelFamilies = new()
    {
        // OpenAI o 系列
        ["o4"] = new[] { "o4-mini-high", "o4-mini", "o4 mini", "o4" },
        ["o3"] = new[] { "o3-mini-high", "o3-mini", "o3 mini", "o3" },
        ["o1"] = new[] { "o1-mini", "o1 mini", "o1-preview", "o1" },
        // OpenAI GPT 系列
        ["gpt"] = new[] { "gpt-5 mini", "gpt-5-mini", "gpt-5", "gpt 5", "gpt-4o-mini", "gpt-4o", "gpt-4-turbo", "gpt-4", "gpt 4" },
        // Anthropic Claude 系列
        ["claude"] = new[] { "claude 4 opus", "claude-4-opus", "claude 4 sonnet", "claude-4-sonnet", "claude-4", "claude 4",
                            "claude 3.5 sonnet", "claude-3.5-sonnet", "claude 3.5 opus", "claude-3.5-opus", "claude-3.5", "claude 3.5",
                            "claude-3", "claude 3", "claude" },
        // Google Gemini 系列
        ["gemini"] = new[] { "gemini 3 ultra", "gemini-3-ultra", "gemini 3 pro", "gemini-3-pro", "gemini-3", "gemini 3",
                            "gemini-2", "gemini 2", "gemini pro", "gemini" },
        // Meta Llama 系列
        ["llama"] = new[] { "llama 4 maverick", "llama-4-maverick", "llama-4", "llama 4", "llama-3.3", "llama 3.3", "llama-3", "llama 3", "llama" },
        // 阿里 Qwen 系列
        ["qwen"] = new[] { "qwen3 235b", "qwen3-235b", "qwen3 72b", "qwen3-72b", "qwen3", "qwen 3",
                          "qwen2.5", "qwen 2.5", "qwen2.5-72b", "qwen2.5-32b", "qwen" },
        // DeepSeek 系列
        ["deepseek"] = new[] { "deepseek r1", "deepseek-r1", "deepseek v3", "deepseek-v3", "deepseek" },
        // xAI Grok 系列
        ["grok"] = new[] { "grok 4", "grok-4", "grok 3", "grok-3", "grok" },
        // 其他
        ["kimi"] = new[] { "kimi" },
        ["mistral"] = new[] { "mistral" },
        ["phi"] = new[] { "phi-4", "phi 4" },
        ["command"] = new[] { "command-r" }
    };

    private static readonly string[] ModelKeywords = ModelFamilies.Values.SelectMany(v => v).ToArray();

    /// <summary>
    /// 从用户问题中提取模型名称
    /// </summary>
    private List<string> ExtractModelNames(string question)
    {
        var questionLower = question.ToLowerInvariant();
        var found = new List<string>();

        // 按长度降序排序，优先匹配更长的
        var sorted = ModelKeywords.OrderByDescending(k => k.Length);

        foreach (var keyword in sorted)
        {
            if (questionLower.Contains(keyword.ToLowerInvariant()))
            {
                // 检查是否已有更长的匹配包含这个
                if (!found.Any(f => f.ToLowerInvariant().Contains(keyword.ToLowerInvariant())))
                {
                    found.Add(keyword);
                    Console.WriteLine($"[Arena Block1] ✓ 识别到模型: {keyword}");
                    if (found.Count >= 3) break;
                }
            }
        }

        if (found.Count == 0)
        {
            Console.WriteLine($"[Arena Block1] ⚠ 未识别到具体模型名称");
        }

        return found;
    }

    /// <summary>
    /// Block 1: 在官方站点搜索榜单数据
    /// 增强版：支持同义词变换和家族回退搜索
    /// </summary>
    private async Task<string?> FetchLeaderboardContentAsync(LeaderboardConfig config, string question)
    {
        if (_searchApi == null) return null;

        try
        {
            Console.WriteLine($"\n[Arena Block1] ===== 开始搜索 {config.Name} =====");

            // 提取模型名称
            var models = ExtractModelNames(question);
            var modelQuery = models.Count > 0 ? string.Join(" ", models) : "";

            Console.WriteLine($"[Arena Block1] 识别到模型: [{string.Join(", ", models)}]");

            // 第一轮：精确搜索
            var searchQuery = BuildSearchQuery(config, modelQuery, precise: true);
            Console.WriteLine($"[Arena Block1] 精确搜索: {searchQuery}");

            var results = await _searchApi.SearchAsync(searchQuery, 8);
            Console.WriteLine($"[Arena Block1] 精确搜索结果数: {results?.Count ?? 0}");

            // 检查是否找到模型
            bool foundModel = models.Count == 0 || CheckModelInResults(models, results);

            // 第二轮：如果没找到，尝试同义词变换
            if (!foundModel && models.Count > 0)
            {
                Console.WriteLine($"[Arena Block1] ⚠ 未找到精确匹配，尝试同义词变换...");
                var alternativeModels = GetAlternativeModelNames(models);

                if (alternativeModels.Count > 0)
                {
                    var altQuery = BuildSearchQuery(config, string.Join(" ", alternativeModels), precise: false);
                    Console.WriteLine($"[Arena Block1] 同义词搜索: {altQuery}");

                    var altResults = await _searchApi.SearchAsync(altQuery, 5);
                    if (altResults != null && altResults.Count > 0)
                    {
                        results = results != null ? results.Concat(altResults).ToList() : altResults;
                        Console.WriteLine($"[Arena Block1] 同义词搜索新增结果: {altResults.Count}");
                    }
                }
            }

            // 第三轮：如果还没找到，搜索模型家族
            if (!foundModel && models.Count > 0)
            {
                Console.WriteLine($"[Arena Block1] ⚠ 尝试搜索模型家族...");
                var familyModels = GetFamilyModels(models);

                if (familyModels.Count > 0)
                {
                    var familyQuery = BuildSearchQuery(config, string.Join(" OR ", familyModels.Take(3)), precise: false);
                    Console.WriteLine($"[Arena Block1] 家族搜索: {familyQuery}");

                    var familyResults = await _searchApi.SearchAsync(familyQuery, 5);
                    if (familyResults != null && familyResults.Count > 0)
                    {
                        results = results != null ? results.Concat(familyResults).ToList() : familyResults;
                        Console.WriteLine($"[Arena Block1] 家族搜索新增结果: {familyResults.Count}");
                    }
                }
            }

            if (results == null || results.Count == 0)
            {
                Console.WriteLine($"[Arena Block1] ❌ 无任何搜索结果");
                return null;
            }

            // 过滤和处理结果
            var processedResults = ProcessSearchResults(config, results, models);

            // 收集来源
            var sources = processedResults
                .Where(r => !string.IsNullOrEmpty(r.Title))
                .Select(r => new SourceReference(r.Title!, r.Url ?? "", r.SemanticDocument?.Substring(0, Math.Min(100, r.SemanticDocument?.Length ?? 0))))
                .ToList();

            lock (_cacheLock)
            {
                _sourcesCache[config.Id] = sources;
            }

            // 构建内容
            var content = new List<string>();
            foreach (var r in processedResults.Take(5))
            {
                content.Add($"📌 {r.Title} ({r.Url})");
                if (!string.IsNullOrEmpty(r.SemanticDocument))
                {
                    var doc = r.SemanticDocument.Length > 1000
                        ? r.SemanticDocument.Substring(0, 1000) + "..."
                        : r.SemanticDocument;
                    content.Add(doc);
                }
            }

            Console.WriteLine($"[Arena Block1] ✓ 返回 {processedResults.Count} 条结果");
            return string.Join("\n\n", content);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Arena Block1] 搜索失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 构建搜索查询
    /// </summary>
    private string BuildSearchQuery(LeaderboardConfig config, string modelQuery, bool precise)
    {
        var sitePrefix = config.Id switch
        {
            "helm" => precise ? "site:crfm.stanford.edu" : "",
            "lmsys" => precise ? "site:lmarena.ai" : "",
            "huggingface" => precise ? "site:huggingface.co" : "",
            _ => ""
        };

        var suffix = config.Id switch
        {
            "helm" => "HELM benchmark leaderboard ranking score",
            "lmsys" => "arena elo rating leaderboard ranking",
            "huggingface" => "open-llm-leaderboard benchmark score",
            _ => "leaderboard ranking"
        };

        return $"{sitePrefix} {modelQuery} {suffix} 2025 2026".Trim();
    }

    /// <summary>
    /// 获取模型的替代名称（同义词）
    /// </summary>
    private List<string> GetAlternativeModelNames(List<string> models)
    {
        var alternatives = new List<string>();

        foreach (var model in models)
        {
            var modelLower = model.ToLowerInvariant();

            // 空格和连字符互换
            if (model.Contains(" "))
                alternatives.Add(model.Replace(" ", "-"));
            if (model.Contains("-"))
                alternatives.Add(model.Replace("-", " "));

            // 版本号变换
            if (modelLower.Contains("3") && !modelLower.Contains("2.5"))
                alternatives.Add(model.ToLowerInvariant().Replace("3", "2.5"));
            if (modelLower.Contains("4") && !modelLower.Contains("3"))
                alternatives.Add(model.ToLowerInvariant().Replace("4", "3"));

            // 大小写变换
            alternatives.Add(model.ToUpperInvariant());
            alternatives.Add(char.ToUpper(model[0]) + model.Substring(1).ToLowerInvariant());
        }

        return alternatives.Distinct().Take(5).ToList();
    }

    /// <summary>
    /// 获取同家族的其他模型
    /// </summary>
    private List<string> GetFamilyModels(List<string> models)
    {
        var familyModels = new List<string>();

        foreach (var model in models)
        {
            var modelLower = model.ToLowerInvariant();

            foreach (var (family, variants) in ModelFamilies)
            {
                if (variants.Any(v => modelLower.Contains(v.ToLowerInvariant()) || v.ToLowerInvariant().Contains(modelLower)))
                {
                    // 添加家族中的其他变体
                    foreach (var variant in variants.Where(v => v != model).Take(3))
                    {
                        familyModels.Add(variant);
                    }
                    break;
                }
            }
        }

        return familyModels.Distinct().ToList();
    }

    /// <summary>
    /// 检查搜索结果中是否包含目标模型（模糊匹配）
    /// 策略：精确匹配 > 核心词匹配（如 "gemini" + "pro"）> 家族名匹配（如 "gemini"）
    /// </summary>
    private bool CheckModelInResults(List<string> models, List<SearchResultItem>? results)
    {
        if (results == null || results.Count == 0) return false;

        foreach (var model in models)
        {
            var modelLower = model.ToLowerInvariant();

            // 合并所有文本用于匹配
            var allText = string.Join(" ", results
                .Select(r => $"{r.Title} {r.SemanticDocument}".ToLowerInvariant()));

            // 1. 精确匹配
            if (allText.Contains(modelLower))
            {
                Console.WriteLine($"[Arena Block1] ✓ 精确匹配到模型 '{model}'");
                return true;
            }

            // 2. 核心词匹配：拆分模型名的每个词，要求所有核心词都出现
            var tokens = modelLower.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length >= 2)
            {
                var allTokensFound = tokens.All(t => allText.Contains(t));
                if (allTokensFound)
                {
                    Console.WriteLine($"[Arena Block1] ✓ 核心词匹配到模型 '{model}' (tokens: {string.Join("+", tokens)})");
                    return true;
                }
            }

            // 3. 家族名匹配：找到模型家族名（第一个token），检查是否出现
            var familyName = tokens.FirstOrDefault();
            if (familyName != null && familyName.Length >= 3 && allText.Contains(familyName))
            {
                Console.WriteLine($"[Arena Block1] ✓ 家族名匹配到 '{familyName}' (来自 '{model}')");
                return true;
            }

            Console.WriteLine($"[Arena Block1] ⚠ 结果中未找到模型 '{model}'");
        }

        return false;
    }

    /// <summary>
    /// 处理搜索结果
    /// </summary>
    private List<SearchResultItem> ProcessSearchResults(LeaderboardConfig config, List<SearchResultItem> results, List<string> models)
    {
        // 过滤官方站点结果
        var officialDomains = config.Id switch
        {
            "helm" => new[] { "crfm.stanford.edu", "stanford.edu" },
            "lmsys" => new[] { "lmarena.ai", "lmsys.org" },
            "huggingface" => new[] { "huggingface.co" },
            _ => Array.Empty<string>()
        };

        var officialResults = results
            .Where(r => r.Url != null && officialDomains.Any(d =>
                r.Url.Contains(d, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Console.WriteLine($"[Arena Block1] 总结果: {results.Count}, 官方站点: {officialResults.Count}");

        // 如果没有官方结果，使用所有结果
        var finalResults = officialResults.Count > 0 ? officialResults : results;

        // 按模型匹配度排序
        if (models.Count > 0)
        {
            finalResults = finalResults
                .OrderByDescending(r => models.Count(m =>
                    (r.SemanticDocument?.ToLowerInvariant().Contains(m.ToLowerInvariant()) ?? false) ||
                    (r.Title?.ToLowerInvariant().Contains(m.ToLowerInvariant()) ?? false)))
                .ThenByDescending(r => r.SemanticDocument?.Length ?? 0)
                .ToList();
        }

        return finalResults.Take(5).ToList();
    }

    #endregion

    #region Block 1b: 直接网页抓取

    /// <summary>
    /// 直接抓取榜单网页内容，提取模型排名数据
    /// 不依赖截图，作为搜索的补充数据源
    /// </summary>
    private async Task<string?> FetchLeaderboardHtmlAsync(LeaderboardConfig config, string question)
    {
        try
        {
            // 每个榜单有不同的数据获取策略
            return config.Id switch
            {
                "lmsys" => await FetchLmArenaDataAsync(question),
                "huggingface" => await FetchHuggingFaceDataAsync(question),
                "helm" => await FetchHelmDataAsync(question),
                _ => null
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Arena HTML] ⚠ 网页抓取失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 抓取 LM Arena 排行榜数据
    /// 尝试多个API端点获取实时数据
    /// </summary>
    private async Task<string?> FetchLmArenaDataAsync(string question)
    {
        // 尝试多个已知的 LM Arena 数据端点
        var endpoints = new[]
        {
            "https://lmarena.ai/api/v1/leaderboard",
            "https://lmarena.ai/api/leaderboard",
            "https://lmarena.ai/leaderboard/data",
            // Hugging Face Spaces 后端 (Gradio API)
            "https://lmarena.ai/api/leaderboard-table",
        };

        foreach (var apiUrl in endpoints)
        {
            try
            {
                Console.WriteLine($"[Arena HTML] 尝试 LM Arena: {apiUrl}");
                using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                request.Headers.Add("Accept", "application/json, text/html, */*");

                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[Arena HTML] {apiUrl} 返回 {response.StatusCode}");
                    continue;
                }

                var content = await response.Content.ReadAsStringAsync();
                if (content.Length < 100)
                {
                    Console.WriteLine($"[Arena HTML] {apiUrl} 内容过短 ({content.Length}), 跳过");
                    continue;
                }

                Console.WriteLine($"[Arena HTML] ✓ LM Arena 返回 {content.Length} 字符");

                // JSON 数据
                if (content.TrimStart().StartsWith("{") || content.TrimStart().StartsWith("["))
                    return ParseLeaderboardJson(content, question);

                // HTML 数据
                var parsed = ExtractTextFromHtml(content, question);
                if (!string.IsNullOrEmpty(parsed)) return parsed;
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine($"[Arena HTML] {apiUrl} 超时");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Arena HTML] {apiUrl} 失败: {ex.Message}");
            }
        }

        // 所有API都失败，最后尝试主页HTML
        Console.WriteLine($"[Arena HTML] 所有API端点失败，尝试主页HTML...");
        return await FetchAndParseHtmlAsync("https://lmarena.ai/leaderboard", question);
    }

    /// <summary>
    /// 抓取 HuggingFace Open LLM 排行数据
    /// </summary>
    private async Task<string?> FetchHuggingFaceDataAsync(string question)
    {
        try
        {
            // HuggingFace Spaces 数据API
            var apiUrl = "https://huggingface.co/api/spaces/open-llm-leaderboard/open_llm_leaderboard";
            return await FetchAndParseHtmlAsync(apiUrl, question);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Arena HTML] HuggingFace 抓取失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 抓取 HELM 排行数据
    /// </summary>
    private async Task<string?> FetchHelmDataAsync(string question)
    {
        try
        {
            return await FetchAndParseHtmlAsync(
                "https://crfm.stanford.edu/helm/capabilities/latest/", question);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Arena HTML] HELM 抓取失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 通用：抓取HTML页面并提取与问题相关的文本
    /// </summary>
    private async Task<string?> FetchAndParseHtmlAsync(string url, string question)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            request.Headers.Add("Accept", "text/html,application/json");

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[Arena HTML] HTTP {response.StatusCode}: {url}");
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[Arena HTML] 获取页面 {content.Length} 字符: {url}");

            return ExtractTextFromHtml(content, question);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Arena HTML] 抓取失败 {url}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 尝试解析排行榜JSON数据
    /// </summary>
    private string? ParseLeaderboardJson(string json, string question)
    {
        try
        {
            var models = ExtractModelNames(question);
            var modelKeywords = models.Select(m => m.ToLowerInvariant()).ToList();

            // 取出JSON中与模型相关的片段
            var lines = json.Split('\n');
            var relevant = new List<string>();

            foreach (var line in lines)
            {
                var lineLower = line.ToLowerInvariant();
                // 匹配模型家族名（如 gemini, claude, gpt）
                var familyMatch = modelKeywords.Any(m =>
                {
                    var family = m.Split(new[] { ' ', '-' })[0];
                    return family.Length >= 3 && lineLower.Contains(family);
                });

                if (familyMatch || modelKeywords.Any(m => lineLower.Contains(m)))
                {
                    relevant.Add(line.Trim());
                }
            }

            if (relevant.Count > 0)
            {
                var result = string.Join("\n", relevant.Take(30));
                Console.WriteLine($"[Arena HTML] JSON中找到 {relevant.Count} 条相关数据");
                return result;
            }

            // 如果没找到特定模型，取前20行作为榜单概况
            var overview = string.Join("\n", lines.Where(l => l.Trim().Length > 5).Take(20));
            Console.WriteLine($"[Arena HTML] JSON中未找到特定模型，返回概况");
            return overview;
        }
        catch
        {
            return json.Length > 2000 ? json.Substring(0, 2000) : json;
        }
    }

    /// <summary>
    /// 从HTML中提取纯文本，重点提取表格和排名数据
    /// </summary>
    private string? ExtractTextFromHtml(string html, string question)
    {
        if (string.IsNullOrEmpty(html)) return null;

        var models = ExtractModelNames(question);
        var result = new List<string>();

        // 1. 提取 <title>
        var titleMatch = Regex.Match(html, @"<title[^>]*>(.*?)</title>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (titleMatch.Success)
            result.Add($"页面标题: {CleanHtmlText(titleMatch.Groups[1].Value)}");

        // 2. 提取表格数据 (<table>, <tr>, <td>)
        var tableMatches = Regex.Matches(html, @"<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var tableRows = new List<string>();
        foreach (Match tr in tableMatches)
        {
            var cells = Regex.Matches(tr.Groups[1].Value, @"<t[dh][^>]*>(.*?)</t[dh]>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (cells.Count > 0)
            {
                var row = string.Join(" | ", cells.Cast<Match>().Select(c => CleanHtmlText(c.Groups[1].Value)).Where(t => t.Length > 0));
                if (row.Length > 3) tableRows.Add(row);
            }
        }

        if (tableRows.Count > 0)
        {
            // 筛选包含模型名的行 + 表头
            var modelFamily = models.Select(m => m.Split(new[] { ' ', '-' })[0].ToLowerInvariant()).Where(f => f.Length >= 3).ToList();
            var relevantRows = tableRows.Where(r =>
            {
                var rLower = r.ToLowerInvariant();
                return modelFamily.Any(f => rLower.Contains(f)) || rLower.Contains("rank") || rLower.Contains("model") || rLower.Contains("score");
            }).Take(15).ToList();

            if (relevantRows.Count > 0)
            {
                result.Add("榜单表格数据:");
                result.AddRange(relevantRows);
                Console.WriteLine($"[Arena HTML] 从表格提取 {relevantRows.Count} 行相关数据");
            }
            else if (tableRows.Count > 0)
            {
                result.Add("榜单表格数据 (前10行):");
                result.AddRange(tableRows.Take(10));
            }
        }

        // 3. 提取JSON-LD或内嵌script数据（有些SPA把数据放在script里）
        var scriptMatches = Regex.Matches(html, @"<script[^>]*>(.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        foreach (Match script in scriptMatches)
        {
            var scriptContent = script.Groups[1].Value;
            // 查找包含模型数据的JSON块
            var modelFamily = models.Select(m => m.Split(new[] { ' ', '-' })[0].ToLowerInvariant()).Where(f => f.Length >= 3).ToList();

            if (modelFamily.Any(f => scriptContent.ToLowerInvariant().Contains(f)) &&
                (scriptContent.Contains("rank") || scriptContent.Contains("score") || scriptContent.Contains("elo")))
            {
                // 提取相关JSON片段
                var jsonSnippet = ExtractRelevantJsonFromScript(scriptContent, modelFamily);
                if (!string.IsNullOrEmpty(jsonSnippet))
                {
                    result.Add("网页内嵌数据:");
                    result.Add(jsonSnippet);
                    Console.WriteLine($"[Arena HTML] 从script提取相关数据 {jsonSnippet.Length} 字符");
                    break; // 只取第一个匹配的script
                }
            }
        }

        if (result.Count <= 1) // 只有title
        {
            // 最后手段：提取所有可见文本中的相关段落
            var plainText = CleanHtmlText(Regex.Replace(html, @"<script[^>]*>.*?</script>|<style[^>]*>.*?</style>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase));
            var paragraphs = plainText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => p.Length > 10)
                .ToList();

            var modelFamily = models.Select(m => m.Split(new[] { ' ', '-' })[0].ToLowerInvariant()).Where(f => f.Length >= 3).ToList();
            var relevantParas = paragraphs.Where(p => modelFamily.Any(f => p.ToLowerInvariant().Contains(f))).Take(10).ToList();

            if (relevantParas.Count > 0)
            {
                result.Add("页面文本中的相关内容:");
                result.AddRange(relevantParas);
            }
        }

        var finalResult = string.Join("\n", result);
        return finalResult.Length > 0 ? (finalResult.Length > 3000 ? finalResult.Substring(0, 3000) : finalResult) : null;
    }

    /// <summary>
    /// 从script标签中提取与模型相关的JSON片段
    /// </summary>
    private string? ExtractRelevantJsonFromScript(string script, List<string> modelFamilies)
    {
        try
        {
            var lines = script.Split('\n');
            var relevant = new List<string>();

            foreach (var line in lines)
            {
                var lineLower = line.ToLowerInvariant();
                if (modelFamilies.Any(f => lineLower.Contains(f)))
                {
                    relevant.Add(line.Trim());
                    if (relevant.Count >= 20) break;
                }
            }

            return relevant.Count > 0 ? string.Join("\n", relevant) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 清理HTML标签，提取纯文本
    /// </summary>
    private static string CleanHtmlText(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var text = Regex.Replace(html, @"<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"\s+", " ");
        return text.Trim();
    }

    #endregion

    #region Block 2: 外部来源搜索

    /// <summary>
    /// Block 2: 搜索非官方站点的评测和新闻
    /// </summary>
    private async Task<string?> FetchExternalNewsAsync(string question)
    {
        if (_searchApi == null) return null;

        try
        {
            Console.WriteLine($"[Arena Block2] ===== 外部来源搜索 =====");

            var models = ExtractModelNames(question);
            var modelQuery = models.Count > 0
                ? string.Join(" ", models.Take(2))
                : "LLM model";

            var searchQuery = $"{modelQuery} benchmark review evaluation 2025 2026";
            Console.WriteLine($"[Arena Block2] 搜索: {searchQuery}");

            var results = await _searchApi.SearchAsync(searchQuery, 8);

            if (results == null || results.Count == 0) return null;

            // 排除官方榜单站点
            var excludeDomains = new[] {
                "crfm.stanford.edu", "stanford.edu",
                "lmarena.ai", "lmsys.org",
                "huggingface.co"
            };

            var externalResults = results
                .Where(r => r.Url != null && !excludeDomains.Any(d =>
                    r.Url.Contains(d, StringComparison.OrdinalIgnoreCase)))
                .Take(3)
                .ToList();

            Console.WriteLine($"[Arena Block2] 外部来源: {externalResults.Count}");

            if (externalResults.Count == 0) return null;

            // 收集来源
            var sources = externalResults
                .Where(r => !string.IsNullOrEmpty(r.Title))
                .Select(r => new SourceReference(r.Title!, r.Url ?? ""))
                .ToList();

            lock (_cacheLock)
            {
                _sourcesCache["external_news"] = sources;
            }

            // 构建内容
            var content = externalResults
                .Where(r => !string.IsNullOrEmpty(r.SemanticDocument))
                .Select(r => $"📰 [{r.Title}]: {r.SemanticDocument?.Substring(0, Math.Min(200, r.SemanticDocument?.Length ?? 0))}...")
                .ToList();

            foreach (var r in externalResults)
            {
                Console.WriteLine($"[Arena Block2] 来源: {r.Title} - {r.Url}");
            }

            return content.Count > 0 ? string.Join("\n", content) : null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Arena Block2] 搜索失败: {ex.Message}");
            return null;
        }
    }

    #endregion

    #region LLM Response Generation

    /// <summary>
    /// 生成单榜单回答 - 增强版
    /// </summary>
    private async Task<string> GenerateSingleResponseAsync(
        string question, LeaderboardConfig config, bool hasScreenshot, string? content)
    {
        Console.WriteLine($"\n[Arena LLM] ===== 生成回答 =====");
        Console.WriteLine($"[Arena LLM] 问题: {question}");
        Console.WriteLine($"[Arena LLM] 榜单: {config.Name}");
        Console.WriteLine($"[Arena LLM] 有截图: {hasScreenshot}");
        Console.WriteLine($"[Arena LLM] 搜索内容长度: {content?.Length ?? 0} 字符");

        if (_llmExample == null)
        {
            Console.WriteLine($"[Arena LLM] ⚠ LLM 服务不可用，返回默认回答");
            return $"已为您查看 {config.Name} 榜单。{config.Description}。请查看截图了解最新排名。";
        }

        // 提取用户询问的模型
        var models = ExtractModelNames(question);
        var modelInfo = models.Count > 0 ? $"用户询问的模型: {string.Join(", ", models)}" : "用户未指定具体模型";

        var contentSection = !string.IsNullOrEmpty(content)
            ? $"\n\n【搜索到的榜单数据】:\n{content}"
            : "\n\n【搜索到的榜单数据】: 未找到具体数据";

        var prompt = $@"你是专业的LLM模型评测专家，请根据以下搜索数据和网页数据回答用户问题。

⚠ 重要提示：
- 搜索引擎数据可能有几天延迟，不一定是最新排名
- 如果数据中有【榜单网页数据】部分，该数据来自网站实时抓取，优先级最高
- 如果数据中没有实时网页数据，请在回答中注明'根据近期搜索数据'，提醒用户查看截图确认最新排名
- 不要编造具体排名数字，只引用数据中明确出现的信息

===== 用户问题 =====
{question}

===== 基本信息 =====
- 榜单: {config.Name}
- URL: {config.Url}
- 说明: {config.Description}
- {modelInfo}
- 截图: {(hasScreenshot ? "已获取，供用户参考查看" : "获取中或不可用")}{contentSection}

===== 回答要求 =====
请按以下结构回答：

1. **核心结论** - 直接回答用户问题（如果有具体排名，明确给出；如果没找到，坦诚说明）

2. **数据分析** - 如果搜索到相关数据：
   - 引用具体排名、分数
   - 分析模型在不同维度的表现（如有）
   - 与其他模型对比（如有）

3. **补充说明** - 如果没找到精确匹配：
   - 说明可能的原因（模型名称变体、榜单更新周期等）
   - 提供同家族其他模型的信息作为参考

4. **操作建议** - 提示用户可查看右侧截图获取最新可视化信息

===== 格式要求 =====
- 使用 Markdown 格式
- 重要数据用 **粗体** 突出
- 控制在 150-300 字
- 语言：中文
- 语气：专业、客观、有帮助";

        try
        {
            Console.WriteLine($"[Arena LLM] 发送 Prompt，长度: {prompt.Length} 字符");

            var messages = new[]
            {
                new { role = "system", content = "你是LLM模型评测专家。结论先行，数据支撑，坦诚沟通。如果没找到数据要坦诚说明并提供替代建议。" },
                new { role = "user", content = prompt }
            };

            var response = await _llmExample.ExecuteLlmRequestAsync(messages, 0.7);

            Console.WriteLine($"[Arena LLM] ✓ 收到回答，长度: {response?.Length ?? 0} 字符");
            Console.WriteLine($"[Arena LLM] 回答预览: {response?.Substring(0, Math.Min(100, response?.Length ?? 0))}...");

            return response ?? $"已为您查看 {config.Name} 榜单。请查看截图了解最新排名。";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Arena LLM] ❌ LLM 调用失败: {ex.Message}");
            return $"已为您查看 {config.Name} 榜单。请查看截图了解最新排名。";
        }
    }

    /// <summary>
    /// 生成多榜单综合回答 - 两区块结构（增强版）
    /// </summary>
    private async Task<string> GenerateMultiLeaderboardResponseAsync(
        string question,
        List<LeaderboardResult> results,
        Dictionary<string, string> leaderboardContents,
        string? externalNews)
    {
        Console.WriteLine($"\n[Arena LLM] ===== 生成多榜单回答 =====");
        Console.WriteLine($"[Arena LLM] 问题: {question}");
        Console.WriteLine($"[Arena LLM] 榜单数: {results.Count}");
        Console.WriteLine($"[Arena LLM] 有内容的榜单: {leaderboardContents.Count}");
        Console.WriteLine($"[Arena LLM] 有外部新闻: {!string.IsNullOrEmpty(externalNews)}");

        // 提取用户询问的模型
        var models = ExtractModelNames(question);

        // Block 1: 榜单官方数据
        var block1 = new List<string>();
        foreach (var r in results)
        {
            var id = r.Leaderboard.Id;
            if (leaderboardContents.TryGetValue(id, out var content) && !string.IsNullOrEmpty(content))
            {
                block1.Add($"📊 {r.Leaderboard.Name}（官方数据）:\n{content}");
            }
            else
            {
                block1.Add($"📊 {r.Leaderboard.Name}: {r.Leaderboard.Description} (请查看截图)");
            }
        }

        var hasBlock1 = leaderboardContents.Count > 0;
        var hasBlock2 = !string.IsNullOrEmpty(externalNews);

        if (_llmExample == null)
        {
            Console.WriteLine($"[Arena LLM] ⚠ LLM 服务不可用");
            return $"已查询 {results.Count} 个榜单。\n\n{string.Join("\n\n", block1)}\n\n请查看截图了解详细排名。";
        }

        var modelInfo = models.Count > 0 ? $"用户询问的模型: {string.Join(", ", models)}" : "用户未指定具体模型";

        var prompt = $@"你是专业的LLM模型评测专家。请根据以下多榜单数据回答用户问题。

===== 用户问题 =====
{question}

===== {modelInfo} =====

===== Block 1: 榜单官方数据（优先引用）=====
{string.Join("\n\n", block1)}

===== Block 2: 外部来源（补充参考）=====
{(hasBlock2 ? externalNews : "暂无外部来源")}

===== 回答要求 =====
请按以下结构回答：

1. **核心结论** - 综合各榜单数据，直接回答用户问题
   - 如果找到模型，给出各榜单排名对比
   - 如果没找到，说明原因并提供替代建议

2. **榜单对比** - 分析模型在不同榜单的表现差异（如果数据足够）

3. **深度分析** - 结合外部来源，分析模型优劣势

4. **查看建议** - 提示用户查看右侧截图获取完整排名

===== 格式要求 =====
- Markdown 格式，重要数据 **粗体**
- 200-300 字
- 中文回复";

        try
        {
            Console.WriteLine($"[Arena LLM] 发送多榜单 Prompt，长度: {prompt.Length} 字符");

            var messages = new[]
            {
                new { role = "system", content = "你是LLM评测专家。综合多榜单数据，结论先行，数据优先，坦诚沟通。" },
                new { role = "user", content = prompt }
            };

            var response = await _llmExample.ExecuteLlmRequestAsync(messages, 0.7);

            Console.WriteLine($"[Arena LLM] ✓ 收到回答，长度: {response?.Length ?? 0} 字符");

            return response ?? $"已查询 {results.Count} 个榜单。请查看截图了解详细排名。";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Arena LLM] ❌ LLM 调用失败: {ex.Message}");
            return $"已查询 {results.Count} 个榜单。请查看截图了解详细排名。";
        }
    }

    #endregion

    #region Helper Methods

    private bool ShouldQueryAllLeaderboards(string question)
    {
        var q = question.ToLowerInvariant();
        return q.Contains("各榜单") || q.Contains("所有榜单") || q.Contains("各个榜单") ||
               q.Contains("多个榜单") || q.Contains("哪个好") || q.Contains("对比") ||
               q.Contains("比较") || (q.Contains("排名") && !MatchesSpecificLeaderboard(q));
    }

    private bool MatchesSpecificLeaderboard(string q)
    {
        foreach (var config in Leaderboards.Values)
        {
            if (config.Keywords.Any(k => q.Contains(k.ToLowerInvariant())))
                return true;
        }
        return false;
    }

    private string? MatchLeaderboard(string question)
    {
        var q = question.ToLowerInvariant();

        if (ShouldQueryAllLeaderboards(question))
            return null;

        foreach (var (id, config) in Leaderboards)
        {
            if (config.Keywords.Any(k => q.Contains(k.ToLowerInvariant())))
                return id;
        }

        return null;
    }

    private CachedLeaderboardData? GetCachedData(string leaderboardId)
    {
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(leaderboardId, out var cached))
            {
                if (DateTime.UtcNow - cached.CapturedAt < CacheExpiry)
                    return cached;
            }
            return null;
        }
    }

    private void UpdateCache(string leaderboardId, string screenshot, DateTime capturedAt, string? content)
    {
        lock (_cacheLock)
        {
            _cache[leaderboardId] = new CachedLeaderboardData(screenshot, capturedAt, content);
        }
    }

    /// <summary>
    /// 后台继续执行截图任务，即使前台超时也会完成并保存
    /// </summary>
    private async Task ContinueScreenshotInBackgroundAsync(Task<string?> screenshotTask, string leaderboardId, string? content)
    {
        try
        {
            // 等待截图任务真正完成（不限时）
            var screenshot = await screenshotTask;

            if (!string.IsNullOrEmpty(screenshot))
            {
                var capturedAt = DateTime.UtcNow;

                // 更新缓存
                UpdateCache(leaderboardId, screenshot, capturedAt, content);

                // 保存到文件
                SaveScreenshotToFile(leaderboardId, screenshot);

                Console.WriteLine($"[Arena] 🎉 后台截图完成并已保存: {leaderboardId}, 大小: {screenshot.Length} 字符");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Arena] ⚠ 后台截图失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 等待截图任务，但有超时限制（不会取消原任务）
    /// </summary>
    private async Task<string?> WaitForScreenshotWithTimeoutAsync(Task<string?> screenshotTask, TimeSpan timeout)
    {
        try
        {
            var completed = await Task.WhenAny(screenshotTask, Task.Delay(timeout));
            if (completed == screenshotTask)
            {
                return await screenshotTask;
            }
            else
            {
                Console.WriteLine($"[Arena] ⏰ 前台等待超时 ({timeout.TotalSeconds}s)，后台继续执行");
                return null;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Arena] ⚠ 截图异常: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 带超时的截图，避免 CUA 卡死阻塞整个请求。
    /// 只尝试一次，超时后放弃。
    /// </summary>
    private async Task<string?> CaptureWithTimeoutAsync(string url, string leaderboardId, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            var task = CaptureLeaderboardWithRetryAsync(url, 0, leaderboardId); // 0 retries = 仅尝试1次
            var completed = await Task.WhenAny(task, Task.Delay(timeout));
            if (completed == task)
            {
                return await task;
            }
            else
            {
                Console.WriteLine($"[Arena] ⏰ 截图超时 ({timeout.TotalSeconds}s)，跳过");
                return null;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Arena] ⚠ 截图异常: {ex.Message}");
            return null;
        }
    }

    private async Task<string?> CaptureLeaderboardWithRetryAsync(string url, int maxRetries, string? leaderboardId)
    {
        if (_cuaApi == null) return null;

        for (int i = 0; i <= maxRetries; i++)
        {
            try
            {
                Console.WriteLine($"[Arena] 截图尝试 {i + 1}/{maxRetries + 1}...");

                var actions = new CuaAction[]
                {
                    new CuaAction { Action = "wait" },
                    new CuaAction { Action = "scroll", X = 0, Y = 100 },
                    new CuaAction { Action = "wait" },
                    new CuaAction { Action = "scroll", X = 0, Y = -100 },
                    new CuaAction { Action = "wait" }
                };

                var screenshot = await _cuaApi.CaptureScreenshotAsync(url, actions);

                if (!string.IsNullOrEmpty(screenshot))
                {
                    Console.WriteLine($"[Arena] 截图成功，大小: {screenshot.Length} 字符");
                    return screenshot;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Arena] 截图失败: {ex.Message}");
                if (i < maxRetries) await Task.Delay(2000);
            }
        }

        return null;
    }

    private void SaveScreenshotToFile(string leaderboardId, string base64)
    {
        try
        {
            var dir = Path.Combine("wwwroot", "screenshots", leaderboardId);
            Directory.CreateDirectory(dir);

            var fileName = $"{leaderboardId}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            var filePath = Path.Combine(dir, fileName);

            var bytes = Convert.FromBase64String(base64);
            File.WriteAllBytes(filePath, bytes);

            Console.WriteLine($"[Arena] 截图已保存: {fileName}");

            // 清理旧文件，保留最近10张
            var files = Directory.GetFiles(dir, "*.png")
                .OrderByDescending(f => File.GetCreationTime(f))
                .Skip(10)
                .ToList();

            foreach (var f in files)
            {
                File.Delete(f);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Arena] 保存截图失败: {ex.Message}");
        }
    }

    private void LoadCacheFromFiles()
    {
        try
        {
            var baseDir = Path.Combine("wwwroot", "screenshots");
            if (!Directory.Exists(baseDir)) return;

            foreach (var dir in Directory.GetDirectories(baseDir))
            {
                var lbId = Path.GetFileName(dir);
                var latestFile = Directory.GetFiles(dir, "*.png")
                    .OrderByDescending(f => File.GetCreationTime(f))
                    .FirstOrDefault();

                if (latestFile != null)
                {
                    var createdAt = File.GetCreationTime(latestFile);
                    if (DateTime.Now - createdAt < CacheExpiry)
                    {
                        var bytes = File.ReadAllBytes(latestFile);
                        var base64 = Convert.ToBase64String(bytes);

                        lock (_cacheLock)
                        {
                            _cache[lbId] = new CachedLeaderboardData(base64, createdAt.ToUniversalTime());
                        }

                        var age = DateTime.Now - createdAt;
                        Console.WriteLine($"[Arena] 已从文件恢复缓存: {lbId} ({age.TotalMinutes:F0}分钟前)");
                    }
                }
            }

            Console.WriteLine($"[Arena] 缓存加载完成，共 {_cache.Count} 个榜单");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Arena] 加载缓存失败: {ex.Message}");
        }
    }

    #endregion
}
