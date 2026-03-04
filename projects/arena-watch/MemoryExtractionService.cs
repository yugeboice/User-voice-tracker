using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MinimalApiCall;

/// <summary>
/// [Teaching] Delegate type for calling LLM.
/// This allows MemoryExtractionService to call LLM without depending on LlmExample class,
/// breaking the circular dependency and making the code easier to understand.
/// </summary>
public delegate Task<string> LlmCaller(object messages, double temperature);

/// <summary>
/// Service for extracting memorable information from user messages using LLM analysis.
/// 
/// [Teaching] Design Decision:
/// This service uses a delegate (LlmCaller) instead of depending on LlmExample directly.
/// This breaks the circular dependency: LlmExample -> MemoryExtractionService -> LlmExample
/// Now it's simply: LlmExample -> MemoryExtractionService (uses delegate)
/// </summary>
public class MemoryExtractionService
{
    private const int MIN_MESSAGE_LENGTH = 5;
    private const double CONFIDENCE_THRESHOLD = 0.7;

    // [Teaching] Instead of depending on LlmExample, we just need a function that can call LLM
    private readonly LlmCaller _callLlm;

    private static readonly string[] GreetingPhrases =
    {
        "你好", "hi", "hello", "ok", "好的", "谢谢", "thanks", "bye", "再见",
        "thank you", "好", "嗯", "哦", "噢"
    };

    /// <summary>
    /// [Teaching] Constructor takes a delegate instead of LlmExample.
    /// This makes the dependency explicit and breaks the circular reference.
    /// </summary>
    public MemoryExtractionService(LlmCaller callLlm)
    {
        _callLlm = callLlm;
    }

    /// <summary>
    /// Quick filter to skip very short messages or common greetings.
    /// Returns true if memory check should be skipped.
    /// </summary>
    public bool ShouldSkipMemoryCheck(string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            return true;

        var trimmed = userMessage.Trim();

        // Skip if too short
        if (trimmed.Length < MIN_MESSAGE_LENGTH)
            return true;

        // Skip if it's just a greeting phrase
        foreach (var greeting in GreetingPhrases)
        {
            if (trimmed.Equals(greeting, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Build the LLM prompt for memory extraction.
    /// Uses Schema Hit technique to get structured JSON output.
    /// </summary>
    private string BuildExtractionPrompt()
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are a memory extraction assistant.");
        sb.AppendLine("Extract key facts about the user that would be useful for future conversations.");
        sb.AppendLine();
        sb.AppendLine("CRITICAL: Return ONLY valid JSON, no extra text.");
        sb.AppendLine();
        sb.AppendLine("Output format:");
        sb.AppendLine("{");
        sb.AppendLine("  \"should_remember\": true/false,");
        sb.AppendLine("  \"reason\": \"判断理由\",");
        sb.AppendLine("  \"memory_item\": {");
        sb.AppendLine("    \"content\": \"用户是/喜欢/偏好...\",");
        sb.AppendLine("    \"type\": \"identity|preference|skill|hobby\",");
        sb.AppendLine("    \"confidence\": 0.0-1.0");
        sb.AppendLine("  }");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("=== Content Format (VERY IMPORTANT) ===");
        sb.AppendLine("Write memory as a complete statement about the user, starting with:");
        sb.AppendLine("- \"用户是...\" for identity (job, role)");
        sb.AppendLine("- \"用户偏好...\" for preferences (tools, styles)");
        sb.AppendLine("- \"用户擅长...\" for skills");
        sb.AppendLine("- \"用户喜欢...\" for hobbies/interests");
        sb.AppendLine();
        sb.AppendLine("This format helps LLM understand the memory clearly in future conversations.");
        sb.AppendLine();
        sb.AppendLine("=== Examples ===");
        sb.AppendLine("User: \"我在互联网公司做后端开发，主要用Go和Python\"");
        sb.AppendLine("→ content: \"用户是后端开发工程师，使用Go和Python\", type: \"identity\"");
        sb.AppendLine();
        sb.AppendLine("User: \"我是产品经理，平时喜欢打羽毛球\"");
        sb.AppendLine("→ content: \"用户是产品经理\", type: \"identity\"");
        sb.AppendLine("(Note: Pick the most important fact. Hobby can be a separate memory.)");
        sb.AppendLine();
        sb.AppendLine("User: \"我喜欢用TypeScript，不太喜欢JavaScript\"");
        sb.AppendLine("→ content: \"用户偏好TypeScript而非JavaScript\", type: \"preference\"");
        sb.AppendLine();
        sb.AppendLine("User: \"我比较喜欢简洁的代码风格\"");
        sb.AppendLine("→ content: \"用户偏好简洁的代码风格\", type: \"preference\"");
        sb.AppendLine();
        sb.AppendLine("User: \"周末喜欢爬山和打羽毛球\"");
        sb.AppendLine("→ content: \"用户喜欢爬山和打羽毛球\", type: \"hobby\"");
        sb.AppendLine();
        sb.AppendLine("=== What NOT to Remember ===");
        sb.AppendLine("- Greetings, weather, chit-chat → should_remember=false");
        sb.AppendLine("- Questions user is asking → should_remember=false");
        sb.AppendLine("- \"不要记住\", \"forget this\" → should_remember=false");

        return sb.ToString();
    }

    /// <summary>
    /// Main method: Extract memory from user message using LLM analysis.
    /// Returns a MemoryItem if extraction succeeds, null otherwise.
    /// </summary>
    public async Task<MemoryItem?> ExtractMemoryAsync(string userMessage)
    {
        try
        {
            // Build extraction prompt
            var systemPrompt = BuildExtractionPrompt();

            // Construct messages for LLM
            var messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage }
            };

            // [Teaching] Call LLM using the delegate - no direct dependency on LlmExample
            var llmResponse = await _callLlm(messages, 0.3);

            // Log raw response for debugging
            Console.WriteLine($"[Memory Extraction] LLM Response: {llmResponse}");

            // Parse JSON response
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            ExtractionResult? result;
            try
            {
                result = JsonSerializer.Deserialize<ExtractionResult>(llmResponse, options);
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"[Memory Extraction] JSON parsing failed: {ex.Message}");
                return null;
            }

            if (result == null)
            {
                Console.WriteLine("[Memory Extraction] Deserialization returned null");
                return null;
            }

            // Validate: Should we remember this?
            if (!result.ShouldRemember)
            {
                Console.WriteLine($"[Memory Extraction] Not remembering: {result.Reason}");
                return null;
            }

            if (result.MemoryItem == null)
            {
                Console.WriteLine("[Memory Extraction] should_remember=true but memory_item is null");
                return null;
            }

            // Validate: Confidence threshold
            if (result.MemoryItem.Confidence < CONFIDENCE_THRESHOLD)
            {
                Console.WriteLine($"[Memory Extraction] Confidence too low: {result.MemoryItem.Confidence:F2} < {CONFIDENCE_THRESHOLD}");
                return null;
            }

            // Convert ExtractedMemoryItem to MemoryItem
            var memoryItem = new MemoryItem
            {
                Content = result.MemoryItem.Content,
                Type = result.MemoryItem.Type,
                // Timestamp and Id will be set by MemoryService.AddMemoryAsync
            };

            Console.WriteLine($"[Memory Extraction] ✓ Extracted: \"{memoryItem.Content}\", type={memoryItem.Type}, confidence={result.MemoryItem.Confidence:F2}");

            return memoryItem;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Memory Extraction] Error: {ex.Message}");
            return null;
        }
    }
}

/// <summary>
/// Data model for LLM extraction result (with snake_case JSON mapping).
/// </summary>
public class ExtractionResult
{
    [JsonPropertyName("should_remember")]
    public bool ShouldRemember { get; set; }

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "";

    [JsonPropertyName("memory_item")]
    public ExtractedMemoryItem? MemoryItem { get; set; }
}

/// <summary>
/// Data model for extracted memory item (before conversion to MemoryItem).
/// </summary>
public class ExtractedMemoryItem
{
    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }
}
