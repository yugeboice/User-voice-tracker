using System.Text;
using System.Text.Json;
using Microsoft.Lumina.Client.Models.Sonicberry;

namespace MinimalApiCall;

/// <summary>
/// Chat API with RAG (Retrieval-Augmented Generation) support.
/// Uses sources as context for more accurate responses.
/// Supports web search enhancement when enabled.
/// </summary>
public class ChatApi
{
    private readonly NotebookApi _notebookApi;
    private readonly NotebookStorage _storage;
    private readonly SearchApi? _searchApi;
    private readonly string _llmEndpoint;
    private readonly string _llmModel;
    
    // Per-notebook chat history cache
    private readonly Dictionary<string, List<ChatMessage>> _chatHistories = new();

    public ChatApi(NotebookApi notebookApi, NotebookStorage storage, string llmEndpoint, string llmModel, SearchApi? searchApi = null)
    {
        _notebookApi = notebookApi;
        _storage = storage;
        _llmEndpoint = llmEndpoint;
        _llmModel = llmModel;
        _searchApi = searchApi;
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
    public async Task<ChatResponse> SendMessageAsync(string notebookId, string userMessage, bool includeHistory = true, bool enableSearch = false)
    {
        // Get all sources
        var sources = _notebookApi.GetAllSources(notebookId).ToList();
        
        // Determine if we should use RAG
        bool shouldUseRag = sources.Any() && await ShouldUseRagAsync(userMessage, sources);

        // Get conversation history once at the beginning
        var chatHistory = GetChatHistory(notebookId);
        
        // Search enhancement: check if we need to search
        List<SearchResultItem> searchResults = new();
        bool usedSearch = false;
        string? rewrittenQuery = null;
        
        if (enableSearch && _searchApi != null)
        {
            // Step 1: Ask LLM if the question can be answered with existing sources
            bool canAnswerWithSources = await CanAnswerWithSourcesAsync(userMessage, sources);
            
            if (!canAnswerWithSources)
            {
                // Step 2: Rewrite query for better search results
                try
                {
                    rewrittenQuery = await RewriteQueryForSearchAsync(userMessage, chatHistory, sources);
                    Console.WriteLine($"[ChatApi] Original query: {userMessage}");
                    Console.WriteLine($"[ChatApi] Rewritten query: {rewrittenQuery}");
                    
                    // Step 3: Call Lumina Search API with rewritten query
                    searchResults = await _searchApi.SearchAsync(rewrittenQuery, 10);
                    usedSearch = searchResults.Any();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ChatApi] Search failed: {ex.Message}");
                    // Continue without search results
                }
            }
        }
        
        List<object> messages = new List<object>();

        if (shouldUseRag || usedSearch)
        {
            // RAG mode: Use sources (and search results if available) as context
            var context = BuildContextFromSources(sources);
            
            // Add search results to context if available
            string searchContext = "";
            if (usedSearch && searchResults.Any())
            {
                searchContext = BuildContextFromSearchResults(searchResults);
            }

            var systemPrompt = usedSearch 
                ? @"You are a helpful AI assistant with access to a knowledge base and web search results.
Answer questions based on the provided sources and search results when relevant.
Always cite your sources using [Source N] format for knowledge base sources.
For web search results, cite them using [Web N] format.
Be concise but comprehensive."
                : @"You are a helpful AI assistant with access to a knowledge base. 
Answer questions based on the provided sources when relevant. 
Always cite your sources using [Source N] format when referencing information from them.
If the question is about the sources, use them. If it's general conversation, respond naturally.
Be concise but comprehensive.";

            messages.Add(new
            {
                role = "system",
                content = systemPrompt
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
            var userMessageWithContext = usedSearch
                ? $@"Available Sources:
{context}

Web Search Results:
{searchContext}

User Question: {userMessage}

Please answer based on the sources and search results above. Cite sources using [Source N] and search results using [Web N]."
                : $@"Available Sources:
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
            Sources = citedSources,
            SearchResults = usedSearch ? searchResults.Select(r => new SearchResultDto
            {
                Title = r.Title ?? "Untitled",
                Url = r.Url ?? "",
                Snippet = r.SemanticDocument ?? ""
            }).ToList() : new List<SearchResultDto>()
        };
    }

    /// <summary>
    /// Ask LLM if the question can be answered with existing sources.
    /// Returns true if sources are sufficient, false if search is needed.
    /// </summary>
    private async Task<bool> CanAnswerWithSourcesAsync(string userMessage, List<Source> sources)
    {
        if (!sources.Any())
            return false;

        var sourceSummary = string.Join("\n", sources.Select((s, i) => $"Source {i + 1}: {s.Title} - {s.Content.Substring(0, Math.Min(200, s.Content.Length))}..."));

        var messages = new List<object>
        {
            new
            {
                role = "system",
                content = "You are a helpful assistant that determines if a question can be answered using the given sources. Respond with only 'yes' or 'no'."
            },
            new
            {
                role = "user",
                content = $@"Given these sources:
{sourceSummary}

Can the following question be fully answered using ONLY the information in these sources?
Question: {userMessage}

Respond with only 'yes' or 'no'."
            }
        };

        try
        {
            var response = await CallLlmAsync(messages);
            return response.Trim().ToLower().StartsWith("yes");
        }
        catch
        {
            // If LLM call fails, assume we can answer with sources (don't trigger search)
            return true;
        }
    }

    /// <summary>
    /// Rewrite user query into an optimized search query.
    /// Extracts the core search intent and removes conversational elements.
    /// </summary>
    private async Task<string> RewriteQueryForSearchAsync(string userMessage, List<ChatMessage> chatHistory, List<Source> sources)
    {
        // Build context from recent chat history for reference resolution
        var recentHistory = chatHistory.TakeLast(6).ToList();
        var historyContext = recentHistory.Any() 
            ? string.Join("\n", recentHistory.Select(m => $"{m.Role}: {m.Content}"))
            : "No previous conversation.";
        
        // Build source context summary
        var sourceContext = sources.Any()
            ? string.Join(", ", sources.Select(s => s.Title))
            : "No sources available.";

        var messages = new List<object>
        {
            new
            {
                role = "system",
                content = @"You are a search query optimizer. Your task is to rewrite user questions into effective web search queries.

Rules:
1. Extract the core information need from the question
2. Resolve pronouns and references using conversation history (e.g., 'it', 'that', 'this' → actual subject)
3. Remove conversational filler words (e.g., 'can you tell me', 'I want to know')
4. Keep the query concise (ideally 3-8 words)
5. Use keywords that are likely to appear in relevant web pages
6. If the question asks for comparison, include both items
7. Output ONLY the rewritten search query, nothing else"
            },
            new
            {
                role = "user",
                content = $@"Recent conversation:
{historyContext}

User's current sources are about: {sourceContext}

User's question: {userMessage}

Rewrite this into an optimized search query:"
            }
        };

        try
        {
            var response = await CallLlmAsync(messages);
            var rewrittenQuery = response.Trim();
            
            // Fallback to original if rewrite is empty or too long
            if (string.IsNullOrWhiteSpace(rewrittenQuery) || rewrittenQuery.Length > 200)
            {
                return userMessage;
            }
            
            return rewrittenQuery;
        }
        catch
        {
            // If LLM call fails, use original query
            return userMessage;
        }
    }

    /// <summary>
    /// Build context string from search results.
    /// </summary>
    private string BuildContextFromSearchResults(List<SearchResultItem> results)
    {
        var sb = new StringBuilder();
        
        for (int i = 0; i < results.Count; i++)
        {
            var result = results[i];
            sb.AppendLine($"[Web {i + 1}]: {result.Title}");
            sb.AppendLine($"URL: {result.Url}");
            if (!string.IsNullOrEmpty(result.SemanticDocument))
            {
                // Truncate if too long
                var content = result.SemanticDocument.Length > 500 
                    ? result.SemanticDocument.Substring(0, 500) + "..." 
                    : result.SemanticDocument;
                sb.AppendLine($"Content: {content}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
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
    /// Call the LLM endpoint (supports Anthropic API format used by egress-llm).
    /// </summary>
    private async Task<string> CallLlmAsync(List<object> messages)
    {
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(60);

        // Extract system message and user/assistant messages separately for Anthropic format
        string? systemPrompt = null;
        var anthropicMessages = new List<object>();

        foreach (var msg in messages)
        {
            var jsonElement = JsonSerializer.SerializeToElement(msg);
            var role = jsonElement.GetProperty("role").GetString();
            var msgContent = jsonElement.GetProperty("content").GetString();

            if (role == "system")
            {
                systemPrompt = msgContent;
            }
            else
            {
                anthropicMessages.Add(new { role, content = msgContent });
            }
        }

        // Build Anthropic API request body
        var requestBody = new Dictionary<string, object>
        {
            ["model"] = _llmModel,
            ["messages"] = anthropicMessages,
            ["max_tokens"] = 1000
        };

        if (!string.IsNullOrEmpty(systemPrompt))
        {
            requestBody["system"] = systemPrompt;
        }

        var json = JsonSerializer.Serialize(requestBody);
        var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            // Use Anthropic API endpoint format
            var response = await client.PostAsync($"{_llmEndpoint}/v1/messages", httpContent);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                // Check if it's a connection issue
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    throw new Exception($"LLM API endpoint not found. Please ensure:\n" +
                        $"1. egress-llm is running on {_llmEndpoint}\n" +
                        $"2. The endpoint path '/v1/messages' is correct\n" +
                        $"Response: {responseText}");
                }
                throw new Exception($"LLM API error: {response.StatusCode} - {responseText}");
            }

            var result = JsonSerializer.Deserialize<JsonElement>(responseText);
            
            // Parse Anthropic API response format
            var contentArray = result.GetProperty("content");
            if (contentArray.GetArrayLength() > 0)
            {
                var firstContent = contentArray[0];
                if (firstContent.TryGetProperty("text", out var textElement))
                {
                    return textElement.GetString() ?? "No response from LLM";
                }
            }

            return "No response from LLM";
        }
        catch (HttpRequestException ex)
        {
            throw new Exception($"Cannot connect to LLM API at {_llmEndpoint}. " +
                $"Please ensure egress-llm is running on localhost:4141. Error: {ex.Message}");
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
    public List<SearchResultDto> SearchResults { get; init; } = new();
}

public record SearchResultDto
{
    public required string Title { get; init; }
    public required string Url { get; init; }
    public string Snippet { get; init; } = "";
}

public record ChatRequest(string NotebookId, string Message, bool IncludeHistory = true, bool EnableSearch = false);
