using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MinimalApiCall.Services;

namespace MinimalApiCall;

/// <summary>
/// Computer Use Agent (CUA) API - Virtual browser automation.
/// Workflow: Initialize → Do (actions) → Get (screenshot) → Release
/// </summary>
public class CuaApi
{
    private readonly string _endpoint;
    private readonly Func<Task<string>> _tokenProvider;
    private readonly PartnerContextConfiguration? _partnerContext;
    private readonly HttpClient _httpClient;
    private readonly HashSet<string> _activeComputers = new();

    public CuaApi(string endpoint, Func<Task<string>> tokenProvider, PartnerContextConfiguration? partnerContext = null)
    {
        _endpoint = endpoint.TrimEnd('/');
        _tokenProvider = tokenProvider;
        _partnerContext = partnerContext;
        _httpClient = new HttpClient();
        
        // Apply Partner Context headers if configured
        ConfigurePartnerContextHeaders();
    }

    /// <summary>
    /// Configure Partner Context HTTP headers on the HttpClient.
    /// Headers: X-Partner, X-ScenarioGroup, X-ScenarioName, X-Application, X-Component
    /// </summary>
    private void ConfigurePartnerContextHeaders()
    {
        if (_partnerContext == null || !_partnerContext.HasPartnerContext)
            return;

        if (!string.IsNullOrWhiteSpace(_partnerContext.Partner))
            _httpClient.DefaultRequestHeaders.Add("X-Partner", _partnerContext.Partner);
        if (!string.IsNullOrWhiteSpace(_partnerContext.ScenarioGroup))
            _httpClient.DefaultRequestHeaders.Add("X-ScenarioGroup", _partnerContext.ScenarioGroup);
        if (!string.IsNullOrWhiteSpace(_partnerContext.ScenarioName))
            _httpClient.DefaultRequestHeaders.Add("X-ScenarioName", _partnerContext.ScenarioName);
        if (!string.IsNullOrWhiteSpace(_partnerContext.Application))
            _httpClient.DefaultRequestHeaders.Add("X-Application", _partnerContext.Application);
        if (!string.IsNullOrWhiteSpace(_partnerContext.Component))
            _httpClient.DefaultRequestHeaders.Add("X-Component", _partnerContext.Component);
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
        
        // Add Partner Context headers
        AddPartnerContextHeaders();

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
    
    /// <summary>Add Partner Context as HTTP headers for tracking.</summary>
    private void AddPartnerContextHeaders()
    {
        if (_partnerContext == null || !_partnerContext.HasPartnerContext) return;
        
        // Remove existing headers to avoid duplicates
        _httpClient.DefaultRequestHeaders.Remove("X-Lumina-Partner");
        _httpClient.DefaultRequestHeaders.Remove("X-Lumina-ScenarioGroup");
        _httpClient.DefaultRequestHeaders.Remove("X-Lumina-ScenarioName");
        _httpClient.DefaultRequestHeaders.Remove("X-Lumina-Application");
        _httpClient.DefaultRequestHeaders.Remove("X-Lumina-Component");
        
        // Add Partner Context headers
        if (!string.IsNullOrEmpty(_partnerContext.Partner))
            _httpClient.DefaultRequestHeaders.Add("X-Lumina-Partner", _partnerContext.Partner);
        if (!string.IsNullOrEmpty(_partnerContext.ScenarioGroup))
            _httpClient.DefaultRequestHeaders.Add("X-Lumina-ScenarioGroup", _partnerContext.ScenarioGroup);
        if (!string.IsNullOrEmpty(_partnerContext.ScenarioName))
            _httpClient.DefaultRequestHeaders.Add("X-Lumina-ScenarioName", _partnerContext.ScenarioName);
        if (!string.IsNullOrEmpty(_partnerContext.Application))
            _httpClient.DefaultRequestHeaders.Add("X-Lumina-Application", _partnerContext.Application);
        if (!string.IsNullOrEmpty(_partnerContext.Component))
            _httpClient.DefaultRequestHeaders.Add("X-Lumina-Component", _partnerContext.Component);
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

        // Response structure: { content: { screenshotUrl: "data:image/png;base64,..." } }
        var result = System.Text.Json.JsonSerializer.Deserialize<CuaGetResponse>(content);
        var screenshot = result?.Content?.Screenshot ?? result?.Content?.ScreenshotUrl;
        
        // Strip "data:image/png;base64," prefix if present
        if (screenshot != null && screenshot.StartsWith("data:image"))
        {
            var commaIndex = screenshot.IndexOf(',');
            if (commaIndex > 0)
                screenshot = screenshot.Substring(commaIndex + 1);
        }
        
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
    
    [JsonPropertyName("screenshotUrl")]
    public string? ScreenshotUrl { get; set; }
}
