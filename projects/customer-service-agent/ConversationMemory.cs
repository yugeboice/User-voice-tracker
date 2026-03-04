using System.Text.Json;
using System.Text.Json.Serialization;

namespace MinimalApiCall;

/// <summary>
/// 对话记忆系统 - 用于存储新的问答对作为知识库扩展
/// </summary>
public class ConversationMemory
{
    private static readonly string _memoryFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "conversation_memory.json");
    private readonly List<MemoryEntry> _memories = new();
    private readonly object _lock = new();

    public ConversationMemory()
    {
        LoadMemories();
    }

    /// <summary>
    /// 添加新的问答记忆
    /// </summary>
    public void AddMemory(string question, string answer, string category, string scenario)
    {
        lock (_lock)
        {
            var entry = new MemoryEntry
            {
                Id = Guid.NewGuid().ToString(),
                Question = question,
                Answer = answer,
                Category = category,
                Scenario = scenario, // "Search" or "CUA"
                Timestamp = DateTime.UtcNow,
                UsageCount = 0,
                IsApproved = false // 需要人工审核后才能成为正式知识库
            };

            _memories.Add(entry);
            SaveMemories();

            Console.WriteLine($"[Memory] Added new Q&A: {question} -> Category: {category}, Scenario: {scenario}");
        }
    }

    /// <summary>
    /// 搜索相似问题（用于匹配用户提问）
    /// </summary>
    public MemoryEntry? FindSimilarQuestion(string question)
    {
        lock (_lock)
        {
            var lowerQuestion = question.ToLower();

            // 1. 精确匹配
            var exactMatch = _memories.FirstOrDefault(m =>
                m.IsApproved &&
                m.Question.Equals(question, StringComparison.OrdinalIgnoreCase));

            if (exactMatch != null)
            {
                exactMatch.UsageCount++;
                SaveMemories();
                return exactMatch;
            }

            // 2. 关键词匹配（提取用户问题的关键词）
            var keywords = ExtractKeywords(question);
            var candidates = _memories
                .Where(m => m.IsApproved)
                .Select(m => new
                {
                    Memory = m,
                    Score = CalculateSimilarity(keywords, ExtractKeywords(m.Question))
                })
                .Where(x => x.Score > 0.5) // 相似度阈值
                .OrderByDescending(x => x.Score)
                .FirstOrDefault();

            if (candidates != null)
            {
                candidates.Memory.UsageCount++;
                SaveMemories();
                return candidates.Memory;
            }

            return null;
        }
    }

    /// <summary>
    /// 根据场景获取记忆（Search 或 CUA）
    /// </summary>
    public List<MemoryEntry> GetMemoriesByScenario(string scenario)
    {
        lock (_lock)
        {
            return _memories
                .Where(m => m.IsApproved && m.Scenario.Equals(scenario, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(m => m.UsageCount)
                .ThenByDescending(m => m.Timestamp)
                .ToList();
        }
    }

    /// <summary>
    /// 获取待审核的记忆
    /// </summary>
    public List<MemoryEntry> GetPendingApproval()
    {
        lock (_lock)
        {
            return _memories
                .Where(m => !m.IsApproved)
                .OrderBy(m => m.Timestamp)
                .ToList();
        }
    }

    /// <summary>
    /// 审核并批准记忆
    /// </summary>
    public bool ApproveMemory(string memoryId)
    {
        lock (_lock)
        {
            var memory = _memories.FirstOrDefault(m => m.Id == memoryId);
            if (memory != null)
            {
                memory.IsApproved = true;
                memory.ApprovedAt = DateTime.UtcNow;
                SaveMemories();
                Console.WriteLine($"[Memory] Approved: {memory.Question}");
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// 删除记忆
    /// </summary>
    public bool DeleteMemory(string memoryId)
    {
        lock (_lock)
        {
            var memory = _memories.FirstOrDefault(m => m.Id == memoryId);
            if (memory != null)
            {
                _memories.Remove(memory);
                SaveMemories();
                Console.WriteLine($"[Memory] Deleted: {memory.Question}");
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// 获取统计信息
    /// </summary>
    public MemoryStats GetStats()
    {
        lock (_lock)
        {
            return new MemoryStats
            {
                TotalMemories = _memories.Count,
                ApprovedMemories = _memories.Count(m => m.IsApproved),
                PendingMemories = _memories.Count(m => !m.IsApproved),
                SearchScenarioCount = _memories.Count(m => m.Scenario == "Search"),
                CuaScenarioCount = _memories.Count(m => m.Scenario == "CUA"),
                MostUsedMemories = _memories
                    .Where(m => m.IsApproved)
                    .OrderByDescending(m => m.UsageCount)
                    .Take(10)
                    .Select(m => new { m.Question, m.UsageCount })
                    .ToList()
            };
        }
    }

    // ========== 私有辅助方法 ==========

    private void LoadMemories()
    {
        try
        {
            if (File.Exists(_memoryFilePath))
            {
                var json = File.ReadAllText(_memoryFilePath);
                var loaded = JsonSerializer.Deserialize<List<MemoryEntry>>(json);
                if (loaded != null)
                {
                    _memories.AddRange(loaded);
                    Console.WriteLine($"[Memory] Loaded {_memories.Count} memories from {_memoryFilePath}");
                }
            }
            else
            {
                Console.WriteLine($"[Memory] No memory file found, starting fresh");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Memory] Failed to load memories: {ex.Message}");
        }
    }

    private void SaveMemories()
    {
        try
        {
            var json = JsonSerializer.Serialize(_memories, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(_memoryFilePath, json);
            Console.WriteLine($"[Memory] Saved {_memories.Count} memories to {_memoryFilePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Memory] Failed to save memories: {ex.Message}");
        }
    }

    /// <summary>
    /// 提取关键词（简单实现）
    /// </summary>
    private List<string> ExtractKeywords(string text)
    {
        // 简单的关键词提取：去除常见停用词
        var stopWords = new HashSet<string>
        {
            "如何", "怎么", "什么", "为什么", "吗", "呢", "的", "了", "是", "在",
            "我", "你", "他", "它", "这", "那", "有", "没有", "可以", "能不能",
            "how", "what", "why", "when", "where", "can", "is", "the", "a", "an"
        };

        return text
            .ToLower()
            .Split(new[] { ' ', '，', '。', '？', '！', ',', '.', '?', '!' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(word => !stopWords.Contains(word) && word.Length > 1)
            .ToList();
    }

    /// <summary>
    /// 计算两个关键词列表的相似度
    /// </summary>
    private double CalculateSimilarity(List<string> keywords1, List<string> keywords2)
    {
        if (keywords1.Count == 0 || keywords2.Count == 0)
            return 0;

        var intersection = keywords1.Intersect(keywords2).Count();
        var union = keywords1.Union(keywords2).Count();

        return (double)intersection / union; // Jaccard 相似度
    }
}

// ========== 数据模型 ==========

/// <summary>
/// 记忆条目
/// </summary>
public class MemoryEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("question")]
    public string Question { get; set; } = "";

    [JsonPropertyName("answer")]
    public string Answer { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = ""; // 入门, 认证, API, CUA, etc.

    [JsonPropertyName("scenario")]
    public string Scenario { get; set; } = ""; // "Search" or "CUA"

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("usageCount")]
    public int UsageCount { get; set; } = 0;

    [JsonPropertyName("isApproved")]
    public bool IsApproved { get; set; } = false;

    [JsonPropertyName("approvedAt")]
    public DateTime? ApprovedAt { get; set; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    [JsonPropertyName("feedback")]
    public string? Feedback { get; set; } // 用户反馈
}

/// <summary>
/// 记忆统计信息
/// </summary>
public class MemoryStats
{
    public int TotalMemories { get; set; }
    public int ApprovedMemories { get; set; }
    public int PendingMemories { get; set; }
    public int SearchScenarioCount { get; set; }
    public int CuaScenarioCount { get; set; }
    public object? MostUsedMemories { get; set; }
}
