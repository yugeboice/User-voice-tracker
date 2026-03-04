namespace MinimalApiCall;

/// <summary>
/// Partner Context Configuration
/// Represents a hierarchical organization structure for API telemetry and monitoring.
/// Hierarchy: Partner > ScenarioGroup > ScenarioName > Application > Component
/// </summary>
public class PartnerContextConfiguration
{
    /// <summary>Level 1: Partner name (e.g., "PM playground")</summary>
    public string Partner { get; set; } = string.Empty;

    /// <summary>Level 2: Scenario group (e.g., "APIDemo")</summary>
    public string ScenarioGroup { get; set; } = string.Empty;

    /// <summary>Level 3: Specific scenario name (optional)</summary>
    public string ScenarioName { get; set; } = string.Empty;

    /// <summary>Level 4: Application name (optional)</summary>
    public string Application { get; set; } = string.Empty;

    /// <summary>Level 5: Component name (optional)</summary>
    public string Component { get; set; } = string.Empty;

    /// <summary>Check if any Partner Context is configured</summary>
    public bool HasPartnerContext => 
        !string.IsNullOrWhiteSpace(Partner) || 
        !string.IsNullOrWhiteSpace(ScenarioGroup);

    /// <summary>
    /// Validate the hierarchy: parent levels must be set before child levels
    /// </summary>
    public bool IsValid()
    {
        // If Component is set, Application must be set
        if (!string.IsNullOrWhiteSpace(Component) && string.IsNullOrWhiteSpace(Application))
            return false;

        // If Application is set, ScenarioName must be set
        if (!string.IsNullOrWhiteSpace(Application) && string.IsNullOrWhiteSpace(ScenarioName))
            return false;

        // If ScenarioName is set, ScenarioGroup must be set
        if (!string.IsNullOrWhiteSpace(ScenarioName) && string.IsNullOrWhiteSpace(ScenarioGroup))
            return false;

        // If ScenarioGroup is set, Partner must be set
        if (!string.IsNullOrWhiteSpace(ScenarioGroup) && string.IsNullOrWhiteSpace(Partner))
            return false;

        return true;
    }

    /// <summary>Get a human-readable summary of the configuration</summary>
    public string GetSummary()
    {
        var parts = new List<string>();
        
        if (!string.IsNullOrWhiteSpace(Partner))
            parts.Add($"Partner={Partner}");
        if (!string.IsNullOrWhiteSpace(ScenarioGroup))
            parts.Add($"ScenarioGroup={ScenarioGroup}");
        if (!string.IsNullOrWhiteSpace(ScenarioName))
            parts.Add($"ScenarioName={ScenarioName}");
        if (!string.IsNullOrWhiteSpace(Application))
            parts.Add($"Application={Application}");
        if (!string.IsNullOrWhiteSpace(Component))
            parts.Add($"Component={Component}");

        return parts.Count > 0 
            ? $"Partner Context: {string.Join(", ", parts)}"
            : "Partner Context: Not configured";
    }
}
