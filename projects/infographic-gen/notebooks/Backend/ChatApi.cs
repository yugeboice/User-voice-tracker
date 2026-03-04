using System.Text;
using System.Text.Json;

namespace MinimalApiCall;

/// <summary>
/// Chat API with RAG (Retrieval-Augmented Generation) support.
/// Uses sources as context for more accurate responses.
/// </summary>
public class ChatApi
{
    private readonly NotebookApi _notebookApi;
    private readonly NotebookStorage _storage;
    private readonly string _llmEndpoint;
    private readonly string _llmModel;
    
    // Per-notebook chat history cache
    private readonly Dictionary<string, List<ChatMessage>> _chatHistories = new();

    public ChatApi(NotebookApi notebookApi, NotebookStorage storage, string llmEndpoint, string llmModel)
    {
        _notebookApi = notebookApi;
        _storage = storage;
        _llmEndpoint = llmEndpoint;
        _llmModel = llmModel;
    }

    private List<ChatMessage> GetChatHistory(string notebookId)
    {
        if (!_chatHistories.ContainsKey(notebookId))
        {
            // Load from disk
            _chatHistories[notebookId] = _storage.LoadChatHistory(notebookId);
        }
        return _chatHistories[notebookId];
    }

    private void SaveChatHistory(string notebookId)
    {
        if (_chatHistories.ContainsKey(notebookId))
        {
            _storage.SaveChatHistory(notebookId, _chatHistories[notebookId]);
        }
    }

    /// <summary>
    /// Send a chat message with intelligent RAG usage.
    /// Only uses sources when the question is related to them.
    /// </summary>
    public async Task<ChatResponse> SendMessageAsync(string notebookId, string userMessage, bool includeHistory = true)
    {
        // Get all sources
        var sources = _notebookApi.GetAllSources(notebookId).ToList();
        
        // Determine if we should use RAG
        bool shouldUseRag = sources.Any() && await ShouldUseRagAsync(userMessage, sources);

        // Get conversation history once at the beginning
        var chatHistory = GetChatHistory(notebookId);
        
        List<object> messages = new List<object>();

        if (shouldUseRag)
        {
            // RAG mode: Use sources as context
            var context = BuildContextFromSources(sources);

            messages.Add(new
            {
                role = "system",
                content = @"You are a helpful AI assistant with access to a knowledge base. 
Answer questions based on the provided sources when relevant. 
Always cite your sources using [Source N] format when referencing information from them.
If the question is about the sources, use them. If it's general conversation, respond naturally.
Be concise but comprehensive."
            });

            // Add conversation history if requested
            if (includeHistory && chatHistory.Any())
            {
                foreach (var msg in chatHistory.TakeLast(10))
                {
                    messages.Add(new
                    {
                        role = msg.Role,
                        content = msg.Content
                    });
                }
            }

            // Add current user message with context
            var userMessageWithContext = $@"Available Sources:
{context}

User Question: {userMessage}

Please answer based on the sources above if relevant to the question.";

            messages.Add(new
            {
                role = "user",
                content = userMessageWithContext
            });
        }
        else
        {
            // Chat mode: Direct conversation without RAG
            messages.Add(new
            {
                role = "system",
                content = @"You are a helpful and friendly AI assistant. 
Have natural conversations with users. Be concise, warm, and engaging."
            });

            // Add conversation history if requested
            if (includeHistory && chatHistory.Any())
            {
                foreach (var msg in chatHistory.TakeLast(10))
                {
                    messages.Add(new
                    {
                        role = msg.Role,
                        content = msg.Content
                    });
                }
            }

            // Add current user message
            messages.Add(new
            {
                role = "user",
                content = userMessage
            });
        }

        // Call LLM
        var response = await CallLlmAsync(messages);

        // Add to conversation history
        chatHistory.Add(new ChatMessage { Role = "user", Content = userMessage });
        chatHistory.Add(new ChatMessage { Role = "assistant", Content = response });
        SaveChatHistory(notebookId);

        // Extract source citations only if using RAG
        var citedSources = shouldUseRag ? ExtractSourceCitations(response, sources) : new List<string>();

        return new ChatResponse
        {
            Message = response,
            Sources = citedSources
        };
    }

    /// <summary>
    /// Determine if the question requires RAG based on sources.
    /// Uses simple heuristics to detect if the question is about the sources.
    /// </summary>
    private Task<bool> ShouldUseRagAsync(string userMessage, List<Source> sources)
    {
        // Simple heuristic: Check for keywords that indicate questions about content
        var ragKeywords = new[]
        {
            "what", "who", "when", "where", "why", "how",
            "explain", "describe", "tell me about", "summarize",
            "according to", "based on", "from the", "in the",
            "什么", "谁", "哪里", "为什么", "怎么", "如何",
            "解释", "描述", "告诉我", "总结", "根据", "基于"
        };

        var messageLower = userMessage.ToLower();

        // Check if message contains question keywords
        bool hasQuestionKeywords = ragKeywords.Any(keyword => messageLower.Contains(keyword));

        // Check if message length suggests it's a question (not just "hi" or "hello")
        bool isSubstantialQuestion = userMessage.Length > 10;

        // Check if any source titles/topics appear in the message
        bool mentionsSourceTopics = sources.Any(s => 
            messageLower.Contains(s.Title.ToLower().Substring(0, Math.Min(20, s.Title.Length))));

        // Use RAG if it's a substantial question or mentions source topics
        return Task.FromResult((hasQuestionKeywords && isSubstantialQuestion) || mentionsSourceTopics);
    }

    /// <summary>
    /// Clear conversation history.
    /// </summary>
    public void ClearHistory(string notebookId)
    {
        var history = GetChatHistory(notebookId);
        history.Clear();
        SaveChatHistory(notebookId);
    }

    /// <summary>
    /// Build context string from all sources.
    /// </summary>
    private string BuildContextFromSources(List<Source> sources)
    {
        var sb = new StringBuilder();
        
        for (int i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            sb.AppendLine($"[Source {i + 1}]: {source.Title}");
            sb.AppendLine($"Type: {source.Type}");
            if (!string.IsNullOrEmpty(source.Url))
            {
                sb.AppendLine($"URL: {source.Url}");
            }
            
            // Truncate content if too long (max 1000 chars per source)
            var content = source.Content.Length > 1000 
                ? source.Content.Substring(0, 1000) + "..." 
                : source.Content;
            sb.AppendLine($"Content: {content}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Extract which sources were cited in the response.
    /// </summary>
    private List<string> ExtractSourceCitations(string response, List<Source> sources)
    {
        var cited = new List<string>();
        
        for (int i = 0; i < sources.Count; i++)
        {
            if (response.Contains($"[Source {i + 1}]"))
            {
                cited.Add($"Source {i + 1}: {sources[i].Title}");
            }
        }

        return cited;
    }

    /// <summary>
    /// Call the LLM endpoint.
    /// </summary>
    private async Task<string> CallLlmAsync(List<object> messages)
    {
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(60);

        var requestBody = new
        {
            model = _llmModel,
            messages = messages,
            temperature = 0.7,
            max_tokens = 1000
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await client.PostAsync($"{_llmEndpoint}/v1/chat/completions", content);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                // Check if it's a connection issue
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    throw new Exception($"Copilot API endpoint not found. Please ensure:\n" +
                        $"1. Copilot API is running on {_llmEndpoint}\n" +
                        $"2. The endpoint path '/v1/chat/completions' is correct\n" +
                        $"Response: {responseText}");
                }
                throw new Exception($"LLM API error: {response.StatusCode} - {responseText}");
            }

            var result = JsonSerializer.Deserialize<JsonElement>(responseText);
            var assistantMessage = result
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return assistantMessage ?? "No response from LLM";
        }
        catch (HttpRequestException ex)
        {
            throw new Exception($"Cannot connect to Copilot API at {_llmEndpoint}. " +
                $"Please ensure the Copilot API service is running on localhost:4242. Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Get conversation history.
    /// </summary>
    public List<ChatMessage> GetHistory(string notebookId)
    {
        return GetChatHistory(notebookId).ToList();
    }
}

public record ChatMessage
{
    public required string Role { get; init; }
    public required string Content { get; init; }
}

public record ChatResponse
{
    public required string Message { get; init; }
    public required List<string> Sources { get; init; }
}

public record ChatRequest(string NotebookId, string Message, bool IncludeHistory = true);
