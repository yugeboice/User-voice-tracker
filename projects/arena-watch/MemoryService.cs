using System.Text;
using System.Text.Json;

namespace MinimalApiCall;

public class MemoryService
{
    private const string ProfilePath = "user_profile.json";
    private const string MemoriesPath = "memories.json";
    private static readonly SemaphoreSlim _memoriesLock = new(1, 1);

    public async Task<UserProfile?> LoadUserProfileAsync()
    {
        try
        {
            if (!File.Exists(ProfilePath)) return null;
            var json = await File.ReadAllTextAsync(ProfilePath);
            return JsonSerializer.Deserialize<UserProfile>(json);
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> SaveUserProfileAsync(UserProfile profile)
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(profile, options);
            await File.WriteAllTextAsync(ProfilePath, json);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> BuildSystemPromptAsync(UserProfile? profile)
    {
        // [Teaching] This is the HARD MEMORY injection - always includes all memories
        // For CONDITIONAL MEMORY injection, use BuildConditionalSystemPromptAsync instead
        return await BuildSystemPromptWithMemoriesAsync(profile, null);
    }

    /// <summary>
    /// [Teaching] Build system prompt with conditional memory injection.
    /// Only includes memories that are relevant to the current task type.
    /// </summary>
    public async Task<string> BuildConditionalSystemPromptAsync(UserProfile? profile, ConditionalMemoryResult? conditionalResult)
    {
        return await BuildSystemPromptWithMemoriesAsync(profile, conditionalResult);
    }

    /// <summary>
    /// [Teaching] Internal method that builds the system prompt.
    /// If conditionalResult is provided, only includes relevant memories (Conditional Memory).
    /// If conditionalResult is null, includes all memories (Hard Memory).
    /// </summary>
    private async Task<string> BuildSystemPromptWithMemoriesAsync(UserProfile? profile, ConditionalMemoryResult? conditionalResult)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a helpful AI assistant.");

        // Part 1: User Profile (static preferences configured by user)
        if (profile != null)
        {
            sb.AppendLine("\n=== User Profile & Preferences ===");
            sb.AppendLine($"- Name: {profile.BasicInfo?.Name}");
            sb.AppendLine($"- Role: {profile.BasicInfo?.Role}");
            sb.AppendLine($"- Preferred Language: {profile.Preferences?.Language}");
            sb.AppendLine($"- Summary Style: {profile.Preferences?.SummaryStyle}");
            if (profile.Preferences?.FocusTopics != null && profile.Preferences.FocusTopics.Any())
            {
                sb.AppendLine($"- Focus Topics: {string.Join(", ", profile.Preferences.FocusTopics)}");
            }
        }

        // Part 2: AI Memories
        List<MemoryItem> memoriesToInject;

        if (conditionalResult != null)
        {
            // [Teaching] CONDITIONAL MEMORY: Only inject relevant memories
            memoriesToInject = conditionalResult.RelevantMemories;
            if (memoriesToInject.Any())
            {
                sb.AppendLine($"\n=== Relevant Memories for [{conditionalResult.TaskType}] Task ===");
                foreach (var memory in memoriesToInject)
                {
                    sb.AppendLine($"- [{memory.Type}] {memory.Content}");
                }
            }
        }
        else
        {
            // [Teaching] HARD MEMORY: Inject all memories
            memoriesToInject = await GetMemoriesAsync();
            if (memoriesToInject.Any())
            {
                sb.AppendLine("\n=== What I Remember About You ===");
                foreach (var memory in memoriesToInject.Take(10)) // Limit to most recent 10
                {
                    sb.AppendLine($"- [{memory.Type}] {memory.Content}");
                }
            }
        }

        sb.AppendLine("\nPlease adapt your responses according to the above context.");
        return sb.ToString();
    }

    /// <summary>
    /// [Teaching] Select relevant memories based on user message using LLM.
    /// This is the core of Conditional Memory injection - LLM decides which memories are relevant.
    /// </summary>
    /// <param name="userMessage">The current user message</param>
    /// <param name="llmCaller">A delegate to call LLM (to avoid circular dependency)</param>
    /// <returns>ConditionalMemoryResult with task type and relevant memories</returns>
    public async Task<ConditionalMemoryResult?> SelectRelevantMemoriesAsync(
        string userMessage,
        Func<object, double, Task<string>> llmCaller)
    {
        var allMemories = await GetMemoriesAsync();
        
        // If no memories exist, skip the LLM call
        if (!allMemories.Any())
        {
            Console.WriteLine("[Conditional Memory] No memories to filter");
            return null;
        }

        // Build the memory selection prompt
        var memoriesJson = string.Join("\n", allMemories.Select((m, i) => 
            $"  {i + 1}. [{m.Type}] {m.Content}"));

        var selectionPrompt = $@"Analyze the user's message and determine which memories are relevant.

User Message: ""{userMessage}""

Available Memories:
{memoriesJson}

Instructions:
1. Identify the task type (e.g., coding, teaching, question, casual chat, etc.)
2. Select memory numbers that are relevant to this specific task
3. Return ONLY a JSON object in this exact format:

{{
  ""taskType"": ""<task type>"",
  ""relevantMemoryNumbers"": [<list of relevant memory numbers, 1-indexed>]
}}

If no memories are relevant, return an empty array for relevantMemoryNumbers.
Return ONLY the JSON object, no other text.";

        var messages = new[]
        {
            new { role = "system", content = "You are a memory relevance analyzer. Return only valid JSON." },
            new { role = "user", content = selectionPrompt }
        };

        try
        {
            var response = await llmCaller(messages, 0.3); // Low temperature for consistent results
            
            Console.WriteLine($"[Conditional Memory] LLM Response: {response}");

            // Parse the JSON response
            // Try to extract JSON from the response (in case LLM adds extra text)
            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonStr = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var parsed = JsonSerializer.Deserialize<MemorySelectionResponse>(jsonStr, options);

                if (parsed != null)
                {
                    var result = new ConditionalMemoryResult
                    {
                        TaskType = parsed.TaskType ?? "general"
                    };

                    // Map memory numbers back to actual MemoryItems
                    foreach (var num in parsed.RelevantMemoryNumbers ?? new List<int>())
                    {
                        if (num >= 1 && num <= allMemories.Count)
                        {
                            result.RelevantMemories.Add(allMemories[num - 1]);
                        }
                    }

                    Console.WriteLine($"[Conditional Memory] Task: {result.TaskType}, Selected {result.RelevantMemories.Count}/{allMemories.Count} memories");
                    return result;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Conditional Memory] Error: {ex.Message}");
        }

        return null;
    }

    public async Task<List<MemoryItem>> GetMemoriesAsync()
    {
        await _memoriesLock.WaitAsync();
        try
        {
            if (!File.Exists(MemoriesPath))
                return new List<MemoryItem>();

            var json = await File.ReadAllTextAsync(MemoriesPath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var memoryStore = JsonSerializer.Deserialize<MemoryStore>(json, options);
            return memoryStore?.Memories?.OrderByDescending(m => m.Timestamp).ToList() ?? new List<MemoryItem>();
        }
        catch
        {
            return new List<MemoryItem>();
        }
        finally
        {
            _memoriesLock.Release();
        }
    }

    public async Task<bool> AddMemoryAsync(MemoryItem memory)
    {
        await _memoriesLock.WaitAsync();
        try
        {
            if (!File.Exists(MemoriesPath))
            {
                // Create initial memory store if file doesn't exist
                var newStore = new MemoryStore { Memories = new List<MemoryItem>() };
                memory.Id ??= $"mem_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
                memory.Timestamp = DateTime.UtcNow;
                newStore.Memories.Add(memory);

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                var json = JsonSerializer.Serialize(newStore, options);
                await File.WriteAllTextAsync(MemoriesPath, json);
                return true;
            }

            var fileJson = await File.ReadAllTextAsync(MemoriesPath);
            var readOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var memoryStore = JsonSerializer.Deserialize<MemoryStore>(fileJson, readOptions);

            if (memoryStore == null)
                memoryStore = new MemoryStore { Memories = new List<MemoryItem>() };

            var memories = memoryStore.Memories ?? new List<MemoryItem>();

            // Generate ID if not provided
            memory.Id ??= $"mem_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            memory.Timestamp = DateTime.UtcNow;

            memories.Insert(0, memory); // Add to beginning (most recent first)

            memoryStore.Memories = memories;
            var writeOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            var outputJson = JsonSerializer.Serialize(memoryStore, writeOptions);
            await File.WriteAllTextAsync(MemoriesPath, outputJson);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            _memoriesLock.Release();
        }
    }

    public async Task<bool> DeleteMemoryAsync(string id)
    {
        await _memoriesLock.WaitAsync();
        try
        {
            if (!File.Exists(MemoriesPath))
                return false;

            var fileJson = await File.ReadAllTextAsync(MemoriesPath);
            var readOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var memoryStore = JsonSerializer.Deserialize<MemoryStore>(fileJson, readOptions);

            if (memoryStore?.Memories == null)
                return false;

            var memory = memoryStore.Memories.FirstOrDefault(m => m.Id == id);
            if (memory == null)
                return false;

            memoryStore.Memories.Remove(memory);

            var writeOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            var outputJson = JsonSerializer.Serialize(memoryStore, writeOptions);
            await File.WriteAllTextAsync(MemoriesPath, outputJson);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            _memoriesLock.Release();
        }
    }

    public async Task<bool> ClearAllMemoriesAsync()
    {
        await _memoriesLock.WaitAsync();
        try
        {
            var memoryStore = new MemoryStore { Memories = new List<MemoryItem>() };
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            var json = JsonSerializer.Serialize(memoryStore, options);
            await File.WriteAllTextAsync(MemoriesPath, json);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            _memoriesLock.Release();
        }
    }
}

public class MemoryStore
{
    public List<MemoryItem> Memories { get; set; } = new();
}

public class MemoryItem
{
    public string? Id { get; set; }
    public string? Content { get; set; }
    public DateTime Timestamp { get; set; }
    public string? Type { get; set; } // preference, fact, interest, conversation, etc.
}

/// <summary>
/// [Teaching] Result of conditional memory selection.
/// Contains the task type and which memories are relevant for the current context.
/// </summary>
public class ConditionalMemoryResult
{
    public string TaskType { get; set; } = "";
    public List<MemoryItem> RelevantMemories { get; set; } = new();
}

/// <summary>
/// [Teaching] Internal class for parsing LLM's memory selection response.
/// </summary>
internal class MemorySelectionResponse
{
    public string? TaskType { get; set; }
    public List<int>? RelevantMemoryNumbers { get; set; }
}
