namespace LuminaSearchConsole.Configuration
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
        /// Default: 5000
        /// </summary>
        public int Port { get; set; } = 5000;

        /// <summary>
        /// Server URLs
        /// Default: http://localhost:5000
        /// </summary>
        public string Urls { get; set; } = "http://localhost:5000";
    }
}
