using System.Text;
using System.Text.Json;
using Microsoft.Lumina.Client.Models.Sonicberry;

namespace MinimalApiCall;

/// <summary>
/// [Teaching] Result of a chat interaction, includes both the AI response
/// and any memory that was extracted and saved from the user's message.
/// </summary>
public class ChatResult
{
    /// <summary>The AI's response to the user</summary>
    public string Response { get; set; } = "";
    
    /// <summary>If we saved a memory from this message, this contains what was saved</summary>
    public string? SavedMemory { get; set; }
}

/// <summary>
/// Integrates Lumina Search with LLM for summarization.
/// Uses Copilot API on localhost:4141 (OpenAI-compatible endpoint).
/// </summary>
public class LlmExample
{
    private readonly SearchApi _searchApi;
    private readonly MemoryService _memoryService;
    private MemoryExtractionService? _memoryExtractionService;  // [Teaching] Now settable after construction
    private readonly ConversationHistoryService? _conversationHistoryService;
    private readonly HttpClient _httpClient;
    private readonly string _llmEndpoint;
    private readonly string _model;

    public LlmExample(SearchApi searchApi, MemoryService memoryService,
        MemoryExtractionService? memoryExtractionService = null,
        ConversationHistoryService? conversationHistoryService = null,
        string llmEndpoint = "http://localhost:4141", string model = "gpt-4")
    {
        _searchApi = searchApi;
        _memoryService = memoryService;
        _memoryExtractionService = memoryExtractionService;
        _conversationHistoryService = conversationHistoryService;
        _httpClient = new HttpClient();
        _llmEndpoint = llmEndpoint;
        _model = model;
    }

    /// <summary>
    /// [Teaching] Allows setting MemoryExtractionService after construction.
    /// This breaks the circular dependency by allowing a two-phase initialization:
    /// 1. Create LlmExample without MemoryExtractionService
    /// 2. Create MemoryExtractionService with LlmExample's method delegate
    /// 3. Set the MemoryExtractionService on LlmExample
    /// </summary>
    public void SetMemoryExtractionService(MemoryExtractionService service)
    {
        _memoryExtractionService = service;
    }

    /// <summary>
    /// Search for information and use LLM to summarize the results.
    /// </summary>
    public async Task<string> SearchAndSummarizeAsync(string userQuery, int topN = 5)
    {
        var searchResults = await _searchApi.SearchAsync(userQuery, topN);

        if (searchResults.Count == 0)
            return "No search results found for your query.";

        var context = BuildContextFromResults(searchResults);
        return await CallLlmAsync(userQuery, context);
    }

    /// <summary>Build context string from search results for LLM prompt.</summary>
    private string BuildContextFromResults(List<SearchResultItem> results)
    {
        var sb = new StringBuilder();

        for (int i = 0; i < results.Count; i++)
        {
            var result = results[i];
            sb.AppendLine($"[Source {i + 1}]: {result.Title}");
            sb.AppendLine($"URL: {result.Url}");

            // Use SemanticDocument for content (contains extracted/summarized page content)
            if (!string.IsNullOrEmpty(result.SemanticDocument))
            {
                // Truncate if too long
                var content = result.SemanticDocument.Length > 2000
                    ? result.SemanticDocument.Substring(0, 2000) + "..."
                    : result.SemanticDocument;
                sb.AppendLine($"Content: {content}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Chat with the LLM using the user's profile for context.
    /// Returns a ChatResult containing the response and any saved memory info.
    /// </summary>
    public async Task<ChatResult> ChatAsync(string userMessage)
    {
        return await ChatAsync(userMessage, useConditionalMemory: false);
    }

    /// <summary>
    /// Chat with the LLM with optional conditional memory injection.
    /// When useConditionalMemory is true, LLM decides which memories are relevant.
    /// </summary>
    public async Task<ChatResult> ChatAsync(string userMessage, bool useConditionalMemory)
    {
        // [Teaching] This is the main chat flow with memory integration
        // Step 1-4: Normal LLM chat (with optional conditional memory selection)
        // Step 5: Extract and save memories from user message
        // Step 6: Save conversation history

        var result = new ChatResult();

        // 1. Load Profile (Memory)
        var profile = await _memoryService.LoadUserProfileAsync();

        // 2. Build System Prompt with either Hard Memory or Conditional Memory
        string systemPrompt;
        ConditionalMemoryResult? conditionalResult = null;

        if (useConditionalMemory)
        {
            // [Teaching] CONDITIONAL MEMORY: Let LLM decide which memories are relevant
            Console.WriteLine("--- [Conditional Memory Selection] ---");
            conditionalResult = await _memoryService.SelectRelevantMemoriesAsync(
                userMessage,
                ExecuteLlmRequestAsync  // Pass our LLM caller as delegate
            );
            
            systemPrompt = await _memoryService.BuildConditionalSystemPromptAsync(profile, conditionalResult);
        }
        else
        {
            // [Teaching] HARD MEMORY: Inject all memories
            systemPrompt = await _memoryService.BuildSystemPromptAsync(profile);
        }

        // [Teaching] Log the System Prompt to console so we can see the injection
        Console.WriteLine("--- [Prompt Construction] ---");
        Console.WriteLine($"[Memory Mode]: {(useConditionalMemory ? "Conditional" : "Hard")}");
        if (conditionalResult != null)
        {
            Console.WriteLine($"[Task Type]: {conditionalResult.TaskType}");
            Console.WriteLine($"[Relevant Memories]: {conditionalResult.RelevantMemories.Count}");
        }
        Console.WriteLine($"[System]: {systemPrompt}");
        Console.WriteLine($"[User]:   {userMessage}");
        Console.WriteLine("-----------------------------");

        // 3. Combine everything into the Message Chain
        // This is where "System Prompt", "Memory", and "User Query" come together
        var messages = new[]
        {
            new { role = "system", content = systemPrompt },
            new { role = "user", content = userMessage }
        };

        // 4. Call LLM and get response
        result.Response = await ExecuteLlmRequestAsync(messages);

        // 5. [Teaching] Memory Extraction - Analyze user message for memorable info
        // This runs AFTER we get the LLM response, so it doesn't slow down the chat
        if (_memoryExtractionService != null)
        {
            try
            {
                Console.WriteLine($"\n[Memory Check] User message: {userMessage}");

                // Quick filter: Skip very short or greeting messages
                if (_memoryExtractionService.ShouldSkipMemoryCheck(userMessage))
                {
                    Console.WriteLine("[Memory Check] Skipped (message too short or is greeting)");
                }
                else
                {
                    // Extract memory (LLM determines if worth remembering)
                    var extractedMemory = await _memoryExtractionService.ExtractMemoryAsync(userMessage);

                    if (extractedMemory != null)
                    {
                        // Save to memories.json
                        var saved = await _memoryService.AddMemoryAsync(extractedMemory);
                        if (saved)
                        {
                            Console.WriteLine($"[Memory Storage] ✓ Saved: {extractedMemory.Content} (ID: {extractedMemory.Id})");
                            // [Teaching] Tell the frontend what we remembered!
                            result.SavedMemory = extractedMemory.Content;
                        }
                        else
                        {
                            Console.WriteLine("[Memory Storage] ✗ Failed to save");
                        }
                    }
                    else
                    {
                        Console.WriteLine("[Memory Extraction] No memory to save");
                    }
                }
            }
            catch (Exception ex)
            {
                // Memory extraction should never break the chat
                Console.WriteLine($"[Memory Extraction] Error: {ex.Message}");
            }
        }

        // 6. Save Conversation History
        if (_conversationHistoryService != null)
        {
            try
            {
                await _conversationHistoryService.SaveConversationAsync(userMessage, result.Response);
            }
            catch (Exception ex)
            {
                // Conversation logging should never break the chat
                Console.WriteLine($"[Conversation History] Error: {ex.Message}");
            }
        }

        return result;
    }

    /// <summary>Call the LLM endpoint with search context.</summary>
    private async Task<string> CallLlmAsync(string userQuery, string context)
    {
        // 1. Load Profile
        var profile = await _memoryService.LoadUserProfileAsync();

        // 2. Build System Prompt (Reuse the same logic) - now includes both profile and memories
        var systemPrompt = await _memoryService.BuildSystemPromptAsync(profile);

        // Append specific instruction for search summarization
        systemPrompt += "\n\nTask: Summarize the provided search results comprehensively but concisely. Cite sources using [Source N] format.";

        var userMessage = $@"Question: {userQuery}

Search Results:
{context}

Please provide a summary that answers the question based on these search results.";

        var messages = new[]
        {
            new { role = "system", content = systemPrompt },
            new { role = "user", content = userMessage }
        };

        return await ExecuteLlmRequestAsync(messages);
    }

    public async Task<string> ExecuteLlmRequestAsync(object messages, double temperature = 0.7)
    {
        var requestBody = new
        {
            model = _model,
            messages = messages,
            temperature = temperature,
            max_tokens = 1000
        };

        try
        {
            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_llmEndpoint}/v1/chat/completions", content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                return $"LLM API error: {response.StatusCode} - {error}";
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            var responseDoc = JsonDocument.Parse(responseJson);

            var messageContent = responseDoc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return messageContent ?? "No response from LLM.";
        }
        catch (HttpRequestException ex)
        {
            return $"Error connecting to LLM at {_llmEndpoint}: {ex.Message}\n" +
                   "Make sure Copilot API is running on localhost:4141.";
        }
        catch (Exception ex)
        {
            return $"Error calling LLM: {ex.Message}";
        }
    }
}
