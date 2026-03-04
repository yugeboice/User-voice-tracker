using System.Text.Json;
using System.Text.Json.Serialization;

namespace MinimalApiCall;

/// <summary>
/// 对话历史管理服务 - 持久化存储和查询对话记录
/// </summary>
public class ConversationHistoryService
{
    private static readonly string _historyFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "conversation_history.json");
    private readonly List<ConversationHistoryEntry> _history = new();
    private readonly object _lock = new();
    private readonly int _maxDaysToKeep = 30;

    public ConversationHistoryService()
    {
        LoadHistory();
        CleanupOldEntries();
    }

    /// <summary>
    /// 添加对话记录
    /// </summary>
    public void AddEntry(string conversationId, string role, string message, string? source = null, Dictionary<string, object>? metadata = null)
    {
        lock (_lock)
        {
            var entry = new ConversationHistoryEntry
            {
                ConversationId = conversationId,
                Timestamp = DateTime.UtcNow,
                Role = role,
                Message = message,
                Source = source,
                Metadata = metadata
            };

            _history.Add(entry);
            SaveHistory();

            Console.WriteLine($"[ConversationHistory] Added {role} message for conversation {conversationId.Substring(0, Math.Min(8, conversationId.Length))}...");
        }
    }

    /// <summary>
    /// 获取指定对话的完整历史
    /// </summary>
    public List<ConversationHistoryEntry> GetConversationHistory(string conversationId)
    {
        lock (_lock)
        {
            return _history
                .Where(e => e.ConversationId == conversationId)
                .OrderBy(e => e.Timestamp)
                .ToList();
        }
    }

    /// <summary>
    /// 获取最近的对话列表（去重，只返回 conversationId）
    /// </summary>
    public List<ConversationSummary> GetRecentConversations(int limit = 10)
    {
        lock (_lock)
        {
            return _history
                .GroupBy(e => e.ConversationId)
                .Select(g => new ConversationSummary
                {
                    ConversationId = g.Key,
                    StartTime = g.Min(e => e.Timestamp),
                    LastMessageTime = g.Max(e => e.Timestamp),
                    MessageCount = g.Count(),
                    FirstUserMessage = g.Where(e => e.Role == "User").FirstOrDefault()?.Message
                })
                .OrderByDescending(c => c.LastMessageTime)
                .Take(limit)
                .ToList();
        }
    }

    /// <summary>
    /// 搜索历史对话（按关键词）
    /// </summary>
    public List<ConversationHistoryEntry> SearchHistory(string keyword, int maxResults = 50)
    {
        lock (_lock)
        {
            var lowerKeyword = keyword.ToLower();
            return _history
                .Where(e => e.Message.ToLower().Contains(lowerKeyword))
                .OrderByDescending(e => e.Timestamp)
                .Take(maxResults)
                .ToList();
        }
    }

    /// <summary>
    /// 获取对话统计信息
    /// </summary>
    public ConversationStats GetStats()
    {
        lock (_lock)
        {
            var totalConversations = _history.Select(e => e.ConversationId).Distinct().Count();
            var totalMessages = _history.Count;
            var userMessages = _history.Count(e => e.Role == "User");
            var assistantMessages = _history.Count(e => e.Role == "Assistant");

            var sourceCounts = _history
                .Where(e => !string.IsNullOrEmpty(e.Source))
                .GroupBy(e => e.Source)
                .ToDictionary(g => g.Key!, g => g.Count());

            return new ConversationStats
            {
                TotalConversations = totalConversations,
                TotalMessages = totalMessages,
                UserMessages = userMessages,
                AssistantMessages = assistantMessages,
                SourceCounts = sourceCounts,
                OldestEntry = _history.Any() ? _history.Min(e => e.Timestamp) : null,
                NewestEntry = _history.Any() ? _history.Max(e => e.Timestamp) : null
            };
        }
    }

    /// <summary>
    /// 删除指定对话的所有历史
    /// </summary>
    public bool DeleteConversation(string conversationId)
    {
        lock (_lock)
        {
            var removed = _history.RemoveAll(e => e.ConversationId == conversationId);
            if (removed > 0)
            {
                SaveHistory();
                Console.WriteLine($"[ConversationHistory] Deleted {removed} entries for conversation {conversationId}");
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// 清理旧的对话记录（超过 maxDaysToKeep 天）
    /// </summary>
    public int CleanupOldEntries()
    {
        lock (_lock)
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-_maxDaysToKeep);
            var removed = _history.RemoveAll(e => e.Timestamp < cutoffDate);

            if (removed > 0)
            {
                SaveHistory();
                Console.WriteLine($"[ConversationHistory] Cleaned up {removed} old entries (older than {_maxDaysToKeep} days)");
            }

            return removed;
        }
    }

    // ========== 私有辅助方法 ==========

    private void LoadHistory()
    {
        try
        {
            if (File.Exists(_historyFilePath))
            {
                var json = File.ReadAllText(_historyFilePath);
                var loaded = JsonSerializer.Deserialize<List<ConversationHistoryEntry>>(json);
                if (loaded != null)
                {
                    _history.AddRange(loaded);
                    Console.WriteLine($"[ConversationHistory] Loaded {_history.Count} entries from {_historyFilePath}");
                }
            }
            else
            {
                Console.WriteLine($"[ConversationHistory] No history file found, starting fresh");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ConversationHistory] Failed to load history: {ex.Message}");
        }
    }

    private void SaveHistory()
    {
        try
        {
            var json = JsonSerializer.Serialize(_history, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(_historyFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ConversationHistory] Failed to save history: {ex.Message}");
        }
    }
}

// ========== 数据模型 ==========

/// <summary>
/// 对话历史条目
/// </summary>
public class ConversationHistoryEntry
{
    [JsonPropertyName("conversationId")]
    public string ConversationId { get; set; } = "";

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("role")]
    public string Role { get; set; } = ""; // "User" or "Assistant"

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("source")]
    public string? Source { get; set; } // "knowledge", "ai", "memory", "search"

    [JsonPropertyName("metadata")]
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// 对话摘要
/// </summary>
public class ConversationSummary
{
    public string ConversationId { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime LastMessageTime { get; set; }
    public int MessageCount { get; set; }
    public string? FirstUserMessage { get; set; }
}

/// <summary>
/// 对话统计信息
/// </summary>
public class ConversationStats
{
    public int TotalConversations { get; set; }
    public int TotalMessages { get; set; }
    public int UserMessages { get; set; }
    public int AssistantMessages { get; set; }
    public Dictionary<string, int> SourceCounts { get; set; } = new();
    public DateTime? OldestEntry { get; set; }
    public DateTime? NewestEntry { get; set; }
}
