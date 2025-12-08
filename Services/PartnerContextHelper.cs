using Microsoft.Lumina.Client.ApiProxy;

namespace LuminaSearchConsole.Services
{
    /// <summary>
    /// Helper class for creating LuminaApiOptions with Partner Context configuration.
    /// 
    /// This class centralizes the Partner Context configuration logic, making it easy for
    /// partners to:
    /// - Configure Partner Context fields in appsettings.json
    /// - Apply Partner Context consistently across all Lumina services
    /// - Modify Partner Context values without changing service code
    /// 
    /// Partner Context fields (in hierarchical order):
    /// 1. Partner - Top-level partner identifier (required)
    /// 2. ScenarioGroup - Group of related scenarios
    /// 3. ScenarioName - Specific scenario name
    /// 4. Application - Application identifier
    /// 5. Component - Component within the application
    /// 
    /// Usage:
    /// <code>
    /// var options = PartnerContextHelper.CreateLuminaApiOptions(
    ///     endpoint: "https://luminaserviceapi-prod-westus.copilotlumina.com",
    ///     tokenProvider: async () => await GetAccessTokenAsync(),
    ///     partnerContext: partnerContextConfig);
    /// </code>
    /// </summary>
    public static class PartnerContextHelper
    {
        /// <summary>
        /// Create LuminaApiOptions with Partner Context configuration.
        /// 
        /// The Partner Context fields help Lumina:
        /// - Track usage per partner/scenario
        /// - Apply appropriate throttling and sandbox settings
        /// - Enable better debugging when issues occur
        /// </summary>
        /// <param name="endpoint">Lumina API endpoint URL</param>
        /// <param name="tokenProvider">Function that provides access tokens for authentication</param>
        /// <param name="partnerContext">Partner Context configuration (can be null)</param>
        /// <returns>Configured LuminaApiOptions instance</returns>
        public static LuminaApiOptions CreateLuminaApiOptions(
            string endpoint,
            Func<Task<string>> tokenProvider,
            PartnerContextConfiguration? partnerContext = null)
        {
            var options = new LuminaApiOptions
            {
                Endpoint = endpoint,
                LuminaApiTokenProvider = tokenProvider
            };

            // Apply Partner Context if configured
            if (partnerContext != null && partnerContext.HasPartnerContext)
            {
                // Validate hierarchical rules before applying
                if (!partnerContext.IsValid())
                {
                    throw new InvalidOperationException(
                        "Invalid Partner Context configuration. Fields must be provided in hierarchical order " +
                        "(Partner > ScenarioGroup > ScenarioName > Application > Component). " +
                        "You cannot skip levels. " +
                        $"Current configuration: {partnerContext.GetSummary()}");
                }

                // Apply Partner Context fields to LuminaApiOptions
                // Fields are only set if they have non-empty values
                options.Partner = partnerContext.Partner;

                if (!string.IsNullOrEmpty(partnerContext.ScenarioGroup))
                    options.ScenarioGroup = partnerContext.ScenarioGroup;

                if (!string.IsNullOrEmpty(partnerContext.ScenarioName))
                    options.ScenarioName = partnerContext.ScenarioName;

                if (!string.IsNullOrEmpty(partnerContext.Application))
                    options.Application = partnerContext.Application;

                if (!string.IsNullOrEmpty(partnerContext.Component))
                    options.Component = partnerContext.Component;
            }

            return options;
        }

        /// <summary>
        /// Create LuminaApiOptions with Partner Context using a synchronous access token.
        /// Convenience overload for cases where the token is already available.
        /// </summary>
        /// <param name="endpoint">Lumina API endpoint URL</param>
        /// <param name="accessToken">Access token for authentication</param>
        /// <param name="partnerContext">Partner Context configuration (can be null)</param>
        /// <returns>Configured LuminaApiOptions instance</returns>
        public static LuminaApiOptions CreateLuminaApiOptions(
            string endpoint,
            string accessToken,
            PartnerContextConfiguration? partnerContext = null)
        {
            return CreateLuminaApiOptions(
                endpoint,
                async () => await Task.FromResult(accessToken),
                partnerContext);
        }
    }
}
