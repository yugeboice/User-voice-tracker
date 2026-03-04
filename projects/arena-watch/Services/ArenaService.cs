using System.Text.Json;
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
            Console.WriteLine($"\n{'='* 60}");
            Console.WriteLine($"[Arena] 🚀 收到问题: {request.Question}");
            Console.WriteLine($"[Arena] 时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"{'='* 60}");
            
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
            
            // 获取搜索内容 (Block 1: 榜单精确搜索)
            if (_searchApi != null)
            {
                Console.WriteLine($"[Arena] 🔍 Step 1: 搜索 {config.Name} 相关内容...");
                content = await FetchLeaderboardContentAsync(config, request.Question);
                Console.WriteLine($"[Arena] 🔍 Step 1 完成: 获取内容 {content?.Length ?? 0} 字符");
            }
            else
            {
                Console.WriteLine($"[Arena] ⚠ SearchApi 不可用，跳过搜索");
            }
            
            // 如果没有缓存，截图
            if (screenshot == null && _cuaApi != null)
            {
                Console.WriteLine($"[Arena] 📸 Step 2: 截取榜单页面...");
                screenshot = await CaptureLeaderboardWithRetryAsync(config.Url, 2, leaderboardId);
                capturedAt = DateTime.UtcNow;
                
                if (!string.IsNullOrEmpty(screenshot))
                {
                    UpdateCache(leaderboardId, screenshot, capturedAt, content);
                    Console.WriteLine($"[Arena] 📸 Step 2 完成: 截图成功");
                }
                else
                {
                    Console.WriteLine($"[Arena] ⚠ Step 2: 截图失败");
                }
            }
            else if (_cuaApi == null)
            {
                Console.WriteLine($"[Arena] ⚠ CuaApi 不可用，跳过截图");
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
            Console.WriteLine($"{'='* 60}\n");

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
                kv => {
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
    /// 检查搜索结果中是否包含目标模型
    /// </summary>
    private bool CheckModelInResults(List<string> models, List<SearchResultItem>? results)
    {
        if (results == null || results.Count == 0) return false;
        
        foreach (var model in models)
        {
            var found = results.Any(r =>
                (r.SemanticDocument?.ToLowerInvariant().Contains(model.ToLowerInvariant()) ?? false) ||
                (r.Title?.ToLowerInvariant().Contains(model.ToLowerInvariant()) ?? false));
            
            if (found)
            {
                Console.WriteLine($"[Arena Block1] ✓ 结果中找到模型 '{model}'");
                return true;
            }
            else
            {
                Console.WriteLine($"[Arena Block1] ⚠ 结果中未找到模型 '{model}'");
            }
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

        var prompt = $@"你是专业的LLM模型评测专家，请根据以下信息回答用户问题。

===== 用户问题 =====
{question}

===== 基本信息 =====
- 榜单: {config.Name}
- URL: {config.Url}
- 说明: {config.Description}
- {modelInfo}
- 截图: {(hasScreenshot ? "已获取最新截图" : "未获取截图")}{contentSection}

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
   - 建议用户查看截图或访问官方链接

4. **操作建议** - 提示查看右侧截图获取最新信息

===== 格式要求 =====
- 使用 Markdown 格式
- 重要数据用 **粗体** 突出
- 控制在 150-250 字
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
