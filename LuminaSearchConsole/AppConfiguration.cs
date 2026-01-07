namespace LuminaSearchConsole
{
    /// <summary>
    /// Configuration for Lumina API services.
    /// Partners should update these values in appsettings.json to match their environment.
    /// </summary>
    public class LuminaConfiguration
    {
        /// <summary>
        /// Lumina API endpoint URL
        /// Default: https://luminaserviceapi-test-westus.copilotlumina.com
        /// </summary>
        public string ApiEndpoint { get; set; } = string.Empty;

        /// <summary>
        /// OAuth scopes required for Lumina API access
        /// Format: api://{resource-id}/.default
        /// </summary>
        public string ApiScopes { get; set; } = string.Empty;
    }

    /// <summary>
    /// Partner Context configuration for identifying callers and enabling customized settings.
    /// 
    /// Partner Context is a hierarchical configuration system that:
    /// - Identifies your service to Lumina for usage tracking
    /// - Enables partner-specific settings for throttling, sandbox, and API access
    /// - Improves debugging capabilities when issues occur
    /// 
    /// IMPORTANT: Fields must be provided in hierarchical order from top to bottom.
    /// You can use a subset of fields, but you cannot skip levels.
    /// 
    /// Valid configurations:
    /// - Partner only
    /// - Partner + ScenarioGroup
    /// - Partner + ScenarioGroup + ScenarioName
    /// - Partner + ScenarioGroup + ScenarioName + Application
    /// - All 5 fields
    /// 
    /// Invalid configurations:
    /// - Partner + ScenarioName (skipping ScenarioGroup) - NOT ALLOWED
    /// - ScenarioGroup only (missing Partner) - NOT ALLOWED
    /// 
    /// Partners should update these values in appsettings.json to match their environment.
    /// Contact Lumina team to set up your partner configuration before going to production.
    /// </summary>
    public class PartnerContextConfiguration
    {
        /// <summary>
        /// Level 1: Top-level partner identifier
        /// Required field - always provide this to identify your service.
        /// Example: "M365Copilot", "PM playground"
        /// </summary>
        public string Partner { get; set; } = string.Empty;

        /// <summary>
        /// Level 2: Group of related scenarios
        /// Example: "WebSearch", "APIDemo"
        /// </summary>
        public string ScenarioGroup { get; set; } = string.Empty;

        /// <summary>
        /// Level 3: Specific scenario name
        /// Example: "DocumentGrounding", "CompanySearch"
        /// </summary>
        public string ScenarioName { get; set; } = string.Empty;

        /// <summary>
        /// Level 4: Application identifier
        /// Example: "Word", "Excel", "DemoConsole"
        /// </summary>
        public string Application { get; set; } = string.Empty;

        /// <summary>
        /// Level 5: Component within the application
        /// Example: "InsertPane", "SearchPanel"
        /// </summary>
        public string Component { get; set; } = string.Empty;

        /// <summary>
        /// Check if any Partner Context fields are configured
        /// </summary>
        public bool HasPartnerContext => !string.IsNullOrEmpty(Partner);

        /// <summary>
        /// Validate that Partner Context fields follow the hierarchical rules.
        /// Fields must be provided in order - you cannot skip levels.
        /// </summary>
        /// <returns>True if valid, false otherwise</returns>
        public bool IsValid()
        {
            // If no Partner Context is configured, that's valid (optional feature)
            if (string.IsNullOrEmpty(Partner))
            {
                // But if any other field is set without Partner, that's invalid
                return string.IsNullOrEmpty(ScenarioGroup) && 
                       string.IsNullOrEmpty(ScenarioName) && 
                       string.IsNullOrEmpty(Application) && 
                       string.IsNullOrEmpty(Component);
            }

            // Partner is set - check hierarchical order
            // ScenarioGroup can be empty, but if ScenarioName is set, ScenarioGroup must be set
            if (!string.IsNullOrEmpty(ScenarioName) && string.IsNullOrEmpty(ScenarioGroup))
                return false;

            // If Application is set, ScenarioGroup and ScenarioName must be set
            if (!string.IsNullOrEmpty(Application) && 
                (string.IsNullOrEmpty(ScenarioGroup) || string.IsNullOrEmpty(ScenarioName)))
                return false;

            // If Component is set, all previous levels must be set
            if (!string.IsNullOrEmpty(Component) && 
                (string.IsNullOrEmpty(ScenarioGroup) || string.IsNullOrEmpty(ScenarioName) || string.IsNullOrEmpty(Application)))
                return false;

            return true;
        }

        /// <summary>
        /// Get a summary of the configured Partner Context for logging
        /// </summary>
        public string GetSummary()
        {
            if (!HasPartnerContext)
                return "Not configured";

            var parts = new List<string> { Partner };
            if (!string.IsNullOrEmpty(ScenarioGroup)) parts.Add(ScenarioGroup);
            if (!string.IsNullOrEmpty(ScenarioName)) parts.Add(ScenarioName);
            if (!string.IsNullOrEmpty(Application)) parts.Add(Application);
            if (!string.IsNullOrEmpty(Component)) parts.Add(Component);

            return string.Join(" > ", parts);
        }
    }

    /// <summary>
    /// Azure AD (Microsoft Entra ID) authentication configuration.
    /// Partners must register their app in Azure AD and update these values.
    /// 
    /// Setup Guide:
    /// 1. Go to Azure Portal > Azure Active Directory > App registrations
    /// 2. Create a new app registration or use existing one
    /// 3. Copy the Application (client) ID and Directory (tenant) ID
    /// 4. Add redirect URI: http://localhost:8400
    /// 5. Update appsettings.json with your values
    /// </summary>
    public class AzureAdConfiguration
    {
        /// <summary>
        /// Azure AD Tenant ID (Directory ID)
        /// Find in: Azure Portal > Azure AD > Properties
        /// </summary>
        public string TenantId { get; set; } = string.Empty;

        /// <summary>
        /// Application (Client) ID
        /// Find in: Azure Portal > App registrations > Your app > Overview
        /// </summary>
        public string ClientId { get; set; } = string.Empty;

        /// <summary>
        /// OAuth redirect URI for authentication callback
        /// Must match exactly with Azure AD app registration
        /// Default: http://localhost:8400
        /// </summary>
        public string RedirectUri { get; set; } = string.Empty;

        /// <summary>
        /// File name for MSAL token cache
        /// Cache stored in: %LocalAppData%\LuminaSearchConsole\{CacheFileName}
        /// </summary>
        public string CacheFileName { get; set; } = "msalcache.bin";

        /// <summary>
        /// Full authority URL for Azure AD authentication
        /// Format: https://login.microsoftonline.com/{TenantId}
        /// </summary>
        public string Authority => $"https://login.microsoftonline.com/{TenantId}";
    }

    /// <summary>
    /// Web server configuration
    /// </summary>
    public class WebServerConfiguration
    {
        /// <summary>
        /// Port number for web server
        /// Default: 8400
        /// </summary>
        public int Port { get; set; } = 8400;

        /// <summary>
        /// Server URLs
        /// Default: http://localhost:8400
        /// </summary>
        public string Urls { get; set; } = "http://localhost:8400";
    }
}
