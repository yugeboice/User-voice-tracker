using Microsoft.Lumina.Client.ApiProxy;
using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace LuminaSearchConsole.Services
{
    /// <summary>
    /// Lumina Computer Use Agent (CUA) Service
    /// 
    /// Handles all CUA-related operations:
    /// - Initialize virtual computers
    /// - Navigate to URLs
    /// - Perform actions (click, type, keypress)
    /// - Capture screenshots
    /// - Release computer resources
    /// 
    /// API Endpoint configured in appsettings.json (LuminaConfiguration:ApiEndpoint)
    /// </summary>
    public class LuminaCuaService
    {
        #region Configuration

        private readonly string _luminaEndpoint;
        private readonly string _accessToken;

        #endregion

        #region Constructor

        public LuminaCuaService(string accessToken, LuminaConfiguration luminaConfig)
        {
            _accessToken = accessToken;
            _luminaEndpoint = luminaConfig.ApiEndpoint;
        }

        #endregion

        #region CUA Operations

        /// <summary>
        /// Initialize a virtual computer for CUA operations
        /// </summary>
        /// <param name="computerId">Unique identifier for the virtual computer</param>
        /// <param name="userId">User ID for the session</param>
        /// <param name="tenantId">Tenant ID for the organization</param>
        /// <returns>The computer ID</returns>
        public async Task<string> InitializeComputerAsync(string computerId, string userId, string tenantId)
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);

            var body = new
            {
                computerId = computerId,
                userId = userId,
                tenantId = tenantId
            };

            var response = await httpClient.PostAsJsonAsync(
                $"{_luminaEndpoint}/api/agent/computer/initialize", 
                body);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                var statusCode = (int)response.StatusCode;
                var errorMessage = statusCode switch
                {
                    507 => "CUA service is at capacity. Virtual computers are currently unavailable. Please try again later.",
                    401 => "Authentication failed. Please login again.",
                    403 => "Access denied. You may not have permission to use CUA service.",
                    _ => $"CUA initialization failed with status {statusCode}: {response.ReasonPhrase}"
                };
                throw new HttpRequestException($"{errorMessage}\n\nDetails: {errorContent}", null, response.StatusCode);
            }

                        return computerId;
        }

        /// <summary>
        /// Get screenshot from virtual computer
        /// </summary>
        /// <param name="computerId">The computer ID to capture screenshot from</param>
        /// <returns>Screenshot response with base64 image data</returns>
        public async Task<CuaScreenshotResponse> GetComputerScreenshotAsync(string computerId)
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);

            var body = new { computerId = computerId };

            var response = await httpClient.PostAsJsonAsync(
                $"{_luminaEndpoint}/api/agent/computer/get", 
                body);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"Failed to get screenshot: {response.ReasonPhrase}\n{errorContent}", null, response.StatusCode);
            }

            var result = await response.Content.ReadFromJsonAsync<CuaScreenshotResponse>();
                        return result!;
        }

        /// <summary>
        /// Perform actions on virtual computer (navigate, click, type, etc.)
        /// </summary>
        /// <param name="computerId">The computer ID to perform actions on</param>
        /// <param name="actions">List of actions to perform</param>
        /// <param name="actionDelayMs">Delay between actions in milliseconds</param>
        /// <returns>Action response with status</returns>
        public async Task<CuaActionResponse> PerformComputerActionsAsync(string computerId, List<CuaAction> actions, int actionDelayMs = 800)
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);

            var body = new
            {
                computerId = computerId,
                actions = actions,
                actionDelayMs = actionDelayMs.ToString()
            };

            var response = await httpClient.PostAsJsonAsync(
                $"{_luminaEndpoint}/api/agent/computer/do", 
                body);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"Failed to perform actions: {response.ReasonPhrase}\n{errorContent}", null, response.StatusCode);
            }

            var result = await response.Content.ReadFromJsonAsync<CuaActionResponse>();
                        return result!;
        }

        /// <summary>
        /// Navigate to a URL in the virtual computer
        /// </summary>
        /// <param name="computerId">The computer ID to navigate</param>
        /// <param name="url">The URL to navigate to</param>
        public async Task NavigateToUrlAsync(string computerId, string url)
        {
            
            var actions = new List<CuaAction>
            {
                new CuaAction { Action = "keypress", Keys = new[] { "ctrl", "l" } },
                new CuaAction { Action = "type", Text = url },
                new CuaAction { Action = "keypress", Keys = new[] { "enter" } },
                new CuaAction { Action = "wait" }
            };

            await PerformComputerActionsAsync(computerId, actions);
                    }

        /// <summary>
        /// Navigate to MSN Money and search for a company
        /// </summary>
        /// <param name="computerId">The computer ID to navigate</param>
        /// <param name="companyName">Company name to search for</param>
        public async Task SearchCompanyOnMsnMoneyAsync(string computerId, string companyName)
        {
            
            // Step 1: Navigate to MSN Money
            await NavigateToUrlAsync(computerId, "https://www.msn.com/en-us/money/");
            
            // Wait for page to load
            await Task.Delay(2000);
            
            // Step 2: Click on search box and search for company
            var actions = new List<CuaAction>
            {
                new CuaAction { Action = "click", X = 1203, Y = 43, Button = 1 },
                new CuaAction { Action = "type", Text = companyName },
                new CuaAction { Action = "keypress", Keys = new[] { "enter" } },
                new CuaAction { Action = "wait" }
            };

            await PerformComputerActionsAsync(computerId, actions, actionDelayMs: 800);
                    }

        /// <summary>
        /// Release virtual computer resources
        /// </summary>
        /// <param name="computerId">The computer ID to release</param>
        public async Task ReleaseComputerAsync(string computerId)
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);

            var body = new { computerId = computerId };

            var response = await httpClient.PostAsJsonAsync(
                $"{_luminaEndpoint}/api/agent/computer/release", 
                body);
            response.EnsureSuccessStatusCode();

                    }

        #endregion
    }

    #region CUA Model Classes

    /// <summary>
    /// Represents an action to perform on a virtual computer
    /// </summary>
    public class CuaAction
    {
        [JsonProperty("action")]
        public string Action { get; set; } = string.Empty;

        [JsonProperty("keys", NullValueHandling = NullValueHandling.Ignore)]
        public string[]? Keys { get; set; }

        [JsonProperty("text", NullValueHandling = NullValueHandling.Ignore)]
        public string? Text { get; set; }

        [JsonProperty("x", NullValueHandling = NullValueHandling.Ignore)]
        public int? X { get; set; }

        [JsonProperty("y", NullValueHandling = NullValueHandling.Ignore)]
        public int? Y { get; set; }

        [JsonProperty("button", NullValueHandling = NullValueHandling.Ignore)]
        public int? Button { get; set; }
    }

    /// <summary>
    /// Response from screenshot capture operation
    /// </summary>
    public class CuaScreenshotResponse
    {
        [JsonPropertyName("messageId")]
        public string? MessageId { get; set; }

        [JsonPropertyName("createTime")]
        public double CreateTime { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("content")]
        public CuaScreenshotContent? Content { get; set; }
    }

    /// <summary>
    /// Screenshot content with base64 image data
    /// </summary>
    public class CuaScreenshotContent
    {
        [JsonPropertyName("screenshot")]
        public string? Screenshot { get; set; }

        [JsonPropertyName("width")]
        public int Width { get; set; }

        [JsonPropertyName("height")]
        public int Height { get; set; }

        [JsonPropertyName("success")]
        public bool Success { get; set; }
    }

    /// <summary>
    /// Response from action execution
    /// </summary>
    public class CuaActionResponse
    {
        [JsonPropertyName("messageId")]
        public string? MessageId { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }
    }

    #endregion
}
