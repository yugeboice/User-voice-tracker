using Microsoft.Lumina.Client.ApiProxy;
using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace LuminaSearchConsole.Services
{
    /// <summary>
    /// Computer Use Agent (CUA) API Demo - Shows how to use:
    /// - POST /api/agent/computer/init - Initialize virtual computer
    /// - POST /api/agent/computer/actions - Perform actions (navigate, click, type, keypress)
    /// - GET /api/agent/computer/screenshot - Capture screenshots
    /// 
    /// WHAT THESE APIs DO:
    /// Control a virtual computer to automate web browsing tasks.
    /// Can navigate to URLs, interact with pages, and capture visual results.
    /// 
    /// HOW TO TEST IN THIS DEMO:
    /// 1. Enter company name and click "Search Company & Capture Screenshot"
    /// 2. The demo will:
    ///    - Initialize a virtual computer
    ///    - Search the company on MSN Money
    ///    - Capture a screenshot of the stock price page
    /// 3. Watch real-time progress updates in the streaming response
    /// 4. Check "API Calls Log" to see all CUA API calls
    /// 
    /// CODE EXAMPLE:
    /// <code>
    /// var cua = new LuminaCuaService(token, config);
    /// await cua.InitializeComputerAsync(computerId, userId, tenantId);
    /// await cua.SearchCompanyOnMsnMoneyAsync(computerId, "Microsoft");
    /// var screenshot = await cua.GetComputerScreenshotAsync(computerId);
    /// </code>
    /// 
    /// API Endpoint configured in appsettings.json (LuminaConfiguration:ApiEndpoint)
    /// </summary>
    public class LuminaCuaService
    {
        #region Configuration

        private readonly string _luminaEndpoint;
        private readonly string _accessToken;
        private readonly PartnerContextConfiguration? _partnerContext;

        #endregion

        #region Constructor

        public LuminaCuaService(string accessToken, LuminaConfiguration luminaConfig, PartnerContextConfiguration? partnerContext = null)
        {
            _accessToken = accessToken;
            _luminaEndpoint = luminaConfig.ApiEndpoint;
            _partnerContext = partnerContext;
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Configure HttpClient with authentication and Partner Context headers
        /// </summary>
        private void ConfigureHttpClient(HttpClient httpClient)
        {
            httpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);

            // Add Partner Context headers if configured
            // These headers identify the caller and enable customized Lumina settings
            // Header names follow Lumina API standard: x-ms-lumina-{field}
            if (_partnerContext != null && _partnerContext.HasPartnerContext)
            {
                httpClient.DefaultRequestHeaders.Add("x-ms-lumina-partner", _partnerContext.Partner);

                if (!string.IsNullOrEmpty(_partnerContext.ScenarioGroup))
                    httpClient.DefaultRequestHeaders.Add("x-ms-lumina-scenariogroup", _partnerContext.ScenarioGroup);

                if (!string.IsNullOrEmpty(_partnerContext.ScenarioName))
                    httpClient.DefaultRequestHeaders.Add("x-ms-lumina-scenario", _partnerContext.ScenarioName);

                if (!string.IsNullOrEmpty(_partnerContext.Application))
                    httpClient.DefaultRequestHeaders.Add("x-ms-lumina-application", _partnerContext.Application);

                if (!string.IsNullOrEmpty(_partnerContext.Component))
                    httpClient.DefaultRequestHeaders.Add("x-ms-lumina-component", _partnerContext.Component);
            }
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
            ConfigureHttpClient(httpClient);

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
            ConfigureHttpClient(httpClient);

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
            ConfigureHttpClient(httpClient);

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
            ConfigureHttpClient(httpClient);

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
