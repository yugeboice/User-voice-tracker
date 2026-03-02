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
            return $"Error connecting to LLM at {_llmEndpoint}: {ex.Message}\n" +
                   "Make sure Copilot API is running on localhost:4141.";
        }
        catch (Exception ex)
        {
            return $"Error calling LLM: {ex.Message}";
        }
    }

    /// <summary>
    /// Generate search queries for a competitor name.
    /// Expands a single competitor name into multiple targeted search queries.
    /// </summary>
    public async Task<List<string>> GenerateSearchQueriesAsync(string competitorName)
    {
        var systemPrompt = @"You are an expert at generating search queries for competitive intelligence research.
Given a competitor name, generate 3-4 specific search queries that will help find:
1. Recent product launches or feature updates
2. Business news (funding, partnerships, expansions)
3. User reviews and market reception
4. Strategic moves and competitive positioning

Output ONLY the search queries, one per line, without numbering or bullets.";

        var userMessage = $"Generate search queries for competitor: {competitorName}";

        var requestBody = new
        {
            model = _model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage }
            },
            temperature = 0.7,
            max_tokens = 200
        };

        try
        {
            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{_llmEndpoint}/v1/chat/completions", content);
            
            if (!response.IsSuccessStatusCode)
                return new List<string> { $"{competitorName} latest news", $"{competitorName} product updates" };

            var responseJson = await response.Content.ReadAsStringAsync();
            var responseDoc = JsonDocument.Parse(responseJson);
            var messageContent = responseDoc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            var queries = messageContent?
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(q => q.Trim())
                .Where(q => !string.IsNullOrEmpty(q))
                .ToList() ?? new List<string>();

            return queries.Any() ? queries : new List<string> { $"{competitorName} latest news" };
        }
        catch
        {
            // Fallback to basic queries if LLM fails
            return new List<string> { $"{competitorName} latest news", $"{competitorName} product updates" };
        }
    }

    /// <summary>
    /// Generate a comprehensive competitor analysis report from collected data.
    /// </summary>
    public async Task<string> GenerateCompetitorReportAsync(
        string competitorName, 
        List<SearchResultItem> searchResults, 
        List<string> fullArticles)
    {
        var systemPrompt = @"You are a competitive intelligence analyst. Generate a comprehensive analysis report for a competitor.

Analysis Structure:
1. **Executive Summary** (2-3 sentences overview)
2. **Product & Features** (new releases, updates, capabilities)
3. **Business Strategy** (pricing, partnerships, market expansion)
4. **Market Reception** (user feedback, media coverage, sentiment)
5. **Competitive Insights** (what this means for us, opportunities/threats)

Requirements:
- Write in professional Chinese (中文)
- Use markdown formatting with headers (##) and lists
- Cite sources using [Source N] notation
- Include a summary table of key findings
- Keep it concise but informative (800-1200 words)
- End with 3-5 actionable insights";

        // Build context from search results and full articles
        var contextBuilder = new StringBuilder();
        contextBuilder.AppendLine("=== Search Results Overview ===");
        for (int i = 0; i < searchResults.Count; i++)
        {
            var result = searchResults[i];
            contextBuilder.AppendLine($"[Source {i + 1}]: {result.Title}");
            contextBuilder.AppendLine($"URL: {result.Url}");
            if (!string.IsNullOrEmpty(result.SemanticDocument))
            {
                var preview = result.SemanticDocument.Length > 1000 
                    ? result.SemanticDocument.Substring(0, 1000) + "..." 
                    : result.SemanticDocument;
                contextBuilder.AppendLine($"Summary: {preview}");
            }
            contextBuilder.AppendLine();
        }

        if (fullArticles.Any())
        {
            contextBuilder.AppendLine("\n=== Full Article Contents ===");
            for (int i = 0; i < fullArticles.Count; i++)
            {
                contextBuilder.AppendLine($"[Article {i + 1}]:");
                var content = fullArticles[i].Length > 3000 
                    ? fullArticles[i].Substring(0, 3000) + "..." 
                    : fullArticles[i];
                contextBuilder.AppendLine(content);
                contextBuilder.AppendLine();
            }
        }

        var userMessage = $@"Competitor: {competitorName}

Research Data:
{contextBuilder}

Generate a comprehensive competitor analysis report following the specified structure.";

        var requestBody = new
        {
            model = _model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage }
            },
            temperature = 0.7,
            max_tokens = 3000
        };

        try
        {
            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{_llmEndpoint}/v1/chat/completions", content);
            
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                return $"# {competitorName} 竞品分析报告\n\n生成报告时出错: {response.StatusCode}";
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            var responseDoc = JsonDocument.Parse(responseJson);
            var messageContent = responseDoc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return messageContent ?? $"# {competitorName} 竞品分析报告\n\n无法生成报告内容。";
        }
        catch (Exception ex)
        {
            return $"# {competitorName} 竞品分析报告\n\n生成报告时出错: {ex.Message}";
        }
    }
}
