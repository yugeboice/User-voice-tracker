using System.Text;
using System.Text.Json;
using Microsoft.Lumina.Client.Models.Sonicberry;

namespace MinimalApiCall;

/// <summary>
/// Integrates Lumina Search with LLM for summarization.
/// Uses Copilot API on localhost:4141 (OpenAI-compatible endpoint).
/// </summary>
public class LlmExample
{
    private readonly SearchApi _searchApi;
    private readonly HttpClient _httpClient;
    private readonly string _llmEndpoint;
    private readonly string _model;

    public LlmExample(SearchApi searchApi, string llmEndpoint = "http://localhost:4141", string model = "gpt-4")
    {
        _searchApi = searchApi;
        _httpClient = new HttpClient();
        _llmEndpoint = llmEndpoint;
        _model = model;
    }

    /// <summary>
    /// Search for information and use LLM to summarize the results.
    /// Falls back to simple summary if LLM is unavailable.
    /// </summary>
    public async Task<string> SearchAndSummarizeAsync(string userQuery, int topN = 5)
    {
        var searchResults = await _searchApi.SearchAsync(userQuery, topN);
        
        if (searchResults.Count == 0)
            return "抱歉，没有找到相关的搜索结果。请尝试使用不同的关键词。";

        var context = BuildContextFromResults(searchResults);
        var llmResponse = await CallLlmAsync(userQuery, context);
        
        // If LLM failed, return a fallback summary
        if (llmResponse.StartsWith("Error connecting to LLM"))
        {
            return BuildFallbackSummary(userQuery, searchResults);
        }
        
        return llmResponse;
    }

    /// <summary>
    /// Build a simple summary when LLM is unavailable.
    /// </summary>
    private string BuildFallbackSummary(string query, List<SearchResultItem> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("⚠️ LLM服务暂时不可用，以下是搜索结果摘要：");
        sb.AppendLine();
        sb.AppendLine($"关于「{query}」，找到 {results.Count} 条相关结果：");
        sb.AppendLine();
        
        for (int i = 0; i < Math.Min(results.Count, 5); i++)
        {
            var result = results[i];
            sb.AppendLine($"【{i + 1}】{result.Title}");
            sb.AppendLine($"🔗 {result.Url}");
            
            if (!string.IsNullOrEmpty(result.SemanticDocument))
            {
                var snippet = result.SemanticDocument.Length > 200 
                    ? result.SemanticDocument.Substring(0, 200) + "..." 
                    : result.SemanticDocument;
                sb.AppendLine($"📄 {snippet}");
            }
            sb.AppendLine();
        }
        
        sb.AppendLine("💡 提示：要获得AI智能分析，请启动 Copilot API 服务（localhost:4141）");
        return sb.ToString();
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

    /// <summary>Call the LLM endpoint with search context.</summary>
    private async Task<string> CallLlmAsync(string userQuery, string context)
    {
        var systemPrompt = @"You are a helpful assistant that summarizes search results. 
Based on the provided search results, give a comprehensive but concise answer to the user's question.
Cite your sources using [Source N] format when referencing information.";

        var userMessage = $@"Question: {userQuery}

Search Results:
{context}

Please provide a summary that answers the question based on these search results.";

        var requestBody = new
        {
            model = _model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage }
            },
            temperature = 0.7,
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
            // Return error message that will trigger fallback
            return $"Error connecting to LLM at {_llmEndpoint}: {ex.Message}";
        }
        catch (Exception ex)
        {
            return $"Error calling LLM: {ex.Message}";
        }
    }
}
