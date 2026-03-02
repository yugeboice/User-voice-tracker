using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace MinimalApiCall;

/// <summary>
/// Computer Use Agent (CUA) API - Virtual browser automation.
/// Workflow: Initialize → Do (actions) → Get (screenshot) → Release
/// </summary>
public class CuaApi
{
    private readonly string _endpoint;
    private readonly Func<Task<string>> _tokenProvider;
    private readonly HttpClient _httpClient;
    private readonly HashSet<string> _activeComputers = new();

    public CuaApi(string endpoint, Func<Task<string>> tokenProvider)
    {
        _endpoint = endpoint.TrimEnd('/');
        _tokenProvider = tokenProvider;
        _httpClient = new HttpClient();
    }

    /// <summary>Get list of active computer IDs.</summary>
    public IReadOnlyCollection<string> ActiveComputers => _activeComputers;

    /// <summary>Release all active computers (call on app shutdown).</summary>
    public async Task ReleaseAllAsync()
    {
        var computers = _activeComputers.ToList();
        foreach (var computerId in computers)
        {
            try
            {
                Console.WriteLine($"[CUA] Releasing computer {computerId} on shutdown...");
                await ReleaseAsync(computerId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CUA] Failed to release {computerId}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// High-level method: Navigate to URL, optionally perform actions, capture screenshot.
    /// Handles the full Init → Do → Get → Release workflow automatically.
    /// </summary>
    public async Task<string?> CaptureScreenshotAsync(string url, CuaAction[]? actions = null)
    {
        string? computerId = null;
        try
        {
            computerId = await InitializeAsync();
            if (string.IsNullOrEmpty(computerId))
                throw new Exception("Failed to initialize virtual computer");

            // Navigate to URL using keyboard shortcuts
            await PerformActionsAsync(computerId, new[]
            {
                new CuaAction { Action = "keypress", Keys = new[] { "ctrl", "l" } },
                new CuaAction { Action = "type", Text = url },
                new CuaAction { Action = "keypress", Keys = new[] { "enter" } },
                new CuaAction { Action = "wait" }
            });

            if (actions != null)
            {
                await PerformActionsAsync(computerId, actions);
            }

            await Task.Delay(2000); // Wait for page load
            return await GetScreenshotAsync(computerId);
        }
        finally
        {
            if (!string.IsNullOrEmpty(computerId))
                await ReleaseAsync(computerId);
        }
    }

    /// <summary>Step 1: Initialize a virtual computer session.</summary>
    public async Task<string?> InitializeAsync()
    {
        var token = await _tokenProvider();
        _httpClient.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Generate a new computerId (client-side)
        var computerId = Guid.NewGuid().ToString("N");
        var url = $"{_endpoint}/api/agent/computer/initialize";
        Console.WriteLine($"[CUA] POST {url}");
        Console.WriteLine($"[CUA] ComputerId (generated): {computerId}");
        
        var response = await _httpClient.PostAsJsonAsync(url, new { computerId });
        var content = await response.Content.ReadAsStringAsync();
        
        Console.WriteLine($"[CUA] Response: {(int)response.StatusCode} {response.StatusCode}");
        if (content.Length > 200)
            Console.WriteLine($"[CUA] Body: {content[..200]}...");
        else
            Console.WriteLine($"[CUA] Body: {content}");

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"CUA Initialize failed: {response.StatusCode} - {content}");
        }

        _activeComputers.Add(computerId);
        return computerId;
    }

    /// <summary>Step 2: Perform actions (navigate, click, type, keypress, scroll, wait).</summary>
    public async Task<bool> PerformActionsAsync(string computerId, CuaAction[] actions, int actionDelayMs = 800)
    {
        var token = await _tokenProvider();
        _httpClient.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var request = new
        {
            computerId,
            actions = actions.Select(a => new
            {
                action = a.Action,
                text = a.Text,
                keys = a.Keys,
                x = a.X,
                y = a.Y,
                button = a.Button
            }).ToArray(),
            actionDelayMs = actionDelayMs.ToString()
        };

        Console.WriteLine($"[CUA] Do: {actions.Length} action(s) - {string.Join(", ", actions.Select(a => a.Action))}");
        var response = await _httpClient.PostAsJsonAsync(
            $"{_endpoint}/api/agent/computer/do", request);
        var content = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"[CUA] Do Response: {response.StatusCode}");
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"[CUA] Do Error: {content}");
        }

        return response.IsSuccessStatusCode;
    }

    /// <summary>Step 3: Capture the current screen.</summary>
    public async Task<string?> GetScreenshotAsync(string computerId)
    {
        var token = await _tokenProvider();
        _httpClient.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        Console.WriteLine($"[CUA] Get screenshot for {computerId}");
        var response = await _httpClient.PostAsJsonAsync(
            $"{_endpoint}/api/agent/computer/get",
            new { computerId });
        
        var content = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"[CUA] Get Response: {response.StatusCode}, Body length: {content.Length}");
        
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"[CUA] Get Error: {content}");
            return null;
        }

        // Response structure: { content: { screenshot: "base64..." } }
        var result = System.Text.Json.JsonSerializer.Deserialize<CuaGetResponse>(content);
        var screenshot = result?.Content?.Screenshot;
        Console.WriteLine($"[CUA] Screenshot: {(screenshot != null ? $"{screenshot.Length} chars" : "null")}");
        return screenshot;
    }

    /// <summary>Step 4: Release the virtual computer session.</summary>
    public async Task ReleaseAsync(string computerId)
    {
        var token = await _tokenProvider();
        _httpClient.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.PostAsJsonAsync(
            $"{_endpoint}/api/agent/computer/release",
            new { computerId });
        
        Console.WriteLine($"[CUA] Release {computerId}: {response.StatusCode}");
        _activeComputers.Remove(computerId);
    }

    /// <summary>
    /// Batch capture screenshots for multiple URLs.
    /// Returns a dictionary mapping URL to base64 screenshot (or null if failed).
    /// </summary>
    public async Task<Dictionary<string, string?>> BatchCaptureScreenshotsAsync(
        List<string> urls, 
        int maxConcurrent = 2)
    {
        var results = new Dictionary<string, string?>();
        
        // Process URLs in batches to limit concurrent virtual computers
        for (int i = 0; i < urls.Count; i += maxConcurrent)
        {
            var batch = urls.Skip(i).Take(maxConcurrent).ToList();
            var tasks = batch.Select(async url =>
            {
                try
                {
                    var screenshot = await CaptureScreenshotAsync(url);
                    return new { Url = url, Screenshot = screenshot };
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CUA] Failed to capture {url}: {ex.Message}");
                    return new { Url = url, Screenshot = (string?)null };
                }
            });

            var batchResults = await Task.WhenAll(tasks);
            
            foreach (var result in batchResults)
            {
                results[result.Url] = result.Screenshot;
            }
            
            // Delay between batches to avoid overwhelming the service
            if (i + maxConcurrent < urls.Count)
            {
                Console.WriteLine($"[CUA] Completed batch {i / maxConcurrent + 1}, waiting before next batch...");
                await Task.Delay(2000);
            }
        }

        return results;
    }
}

/// <summary>Action to perform on the virtual computer.</summary>
public class CuaAction
{
    public string Action { get; set; } = string.Empty;  // keypress, type, click, wait, scroll
    public string? Text { get; set; }      // For type
    public string[]? Keys { get; set; }    // For keypress (e.g., ["ctrl", "l"], ["enter"])
    public int? X { get; set; }            // For click
    public int? Y { get; set; }            // For click
    public int? Button { get; set; }       // For click (1=left, 2=right)
}

internal class CuaInitResponse
{
    [JsonPropertyName("computerId")]
    public string? ComputerId { get; set; }
}

/// <summary>Response from /api/agent/computer/get</summary>
internal class CuaGetResponse
{
    [JsonPropertyName("content")]
    public CuaScreenshotContent? Content { get; set; }
}

internal class CuaScreenshotContent
{
    [JsonPropertyName("screenshot")]
    public string? Screenshot { get; set; }
}
