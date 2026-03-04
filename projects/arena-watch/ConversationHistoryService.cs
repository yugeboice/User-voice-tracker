using System.Text.Json;
using System.Text.Json.Serialization;

namespace MinimalApiCall;

/// <summary>
/// Service for storing and retrieving conversation history.
/// Stores conversations in conversation_history.json with thread-safe operations.
/// </summary>
public class ConversationHistoryService
{
    private const string HistoryPath = "conversation_history.json";
    private static readonly SemaphoreSlim _historyLock = new(1, 1);

    /// <summary>
    /// Save a conversation turn (user message + assistant response) to history.
    /// </summary>
    public async Task<bool> SaveConversationAsync(string userMessage, string assistantResponse)
    {
        await _historyLock.WaitAsync();
        try
        {
            var history = await LoadHistoryStoreAsync();

            var conversation = new ConversationTurn
            {
                Id = $"conv_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                UserMessage = userMessage,
                AssistantResponse = assistantResponse,
                Timestamp = DateTime.UtcNow
            };

            history.Conversations.Insert(0, conversation); // Most recent first

            // Keep only last 100 conversations to prevent file from growing too large
            if (history.Conversations.Count > 100)
            {
                history.Conversations = history.Conversations.Take(100).ToList();
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            var json = JsonSerializer.Serialize(history, options);
            await File.WriteAllTextAsync(HistoryPath, json);

            Console.WriteLine($"[Conversation History] ✓ Saved conversation ID: {conversation.Id}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Conversation History] Error saving: {ex.Message}");
            return false;
        }
        finally
        {
            _historyLock.Release();
        }
    }

    /// <summary>
    /// Get all conversation history (most recent first).
    /// </summary>
    public async Task<List<ConversationTurn>> GetConversationsAsync(int limit = 50)
    {
        await _historyLock.WaitAsync();
        try
        {
            var history = await LoadHistoryStoreAsync();
            return history.Conversations.Take(limit).ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Conversation History] Error loading: {ex.Message}");
            return new List<ConversationTurn>();
        }
        finally
        {
            _historyLock.Release();
        }
    }

    /// <summary>
    /// Delete a specific conversation by ID.
    /// </summary>
    public async Task<bool> DeleteConversationAsync(string id)
    {
        await _historyLock.WaitAsync();
        try
        {
            var history = await LoadHistoryStoreAsync();
            var conversation = history.Conversations.FirstOrDefault(c => c.Id == id);

            if (conversation == null)
                return false;

            history.Conversations.Remove(conversation);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = JsonSerializer.Serialize(history, options);
            await File.WriteAllTextAsync(HistoryPath, json);

            Console.WriteLine($"[Conversation History] ✓ Deleted conversation ID: {id}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Conversation History] Error deleting: {ex.Message}");
            return false;
        }
        finally
        {
            _historyLock.Release();
        }
    }

    /// <summary>
    /// Clear all conversation history.
    /// </summary>
    public async Task<bool> ClearAllConversationsAsync()
    {
        await _historyLock.WaitAsync();
        try
        {
            var history = new ConversationHistoryStore { Conversations = new List<ConversationTurn>() };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = JsonSerializer.Serialize(history, options);
            await File.WriteAllTextAsync(HistoryPath, json);

            Console.WriteLine("[Conversation History] ✓ Cleared all conversations");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Conversation History] Error clearing: {ex.Message}");
            return false;
        }
        finally
        {
            _historyLock.Release();
        }
    }

    /// <summary>
    /// Load conversation history from file.
    /// </summary>
    private async Task<ConversationHistoryStore> LoadHistoryStoreAsync()
    {
        if (!File.Exists(HistoryPath))
        {
            return new ConversationHistoryStore { Conversations = new List<ConversationTurn>() };
        }

        try
        {
            var json = await File.ReadAllTextAsync(HistoryPath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var history = JsonSerializer.Deserialize<ConversationHistoryStore>(json, options);
            return history ?? new ConversationHistoryStore { Conversations = new List<ConversationTurn>() };
        }
        catch
        {
            return new ConversationHistoryStore { Conversations = new List<ConversationTurn>() };
        }
    }
}

/// <summary>
/// Data model for conversation history store.
/// </summary>
public class ConversationHistoryStore
{
    public List<ConversationTurn> Conversations { get; set; } = new();
}

/// <summary>
/// Data model for a single conversation turn (user + assistant).
/// </summary>
public class ConversationTurn
{
    public string Id { get; set; } = "";
    public string UserMessage { get; set; } = "";
    public string AssistantResponse { get; set; } = "";
    public DateTime Timestamp { get; set; }
}
