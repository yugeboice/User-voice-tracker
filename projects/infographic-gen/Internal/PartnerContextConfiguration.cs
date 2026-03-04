namespace MinimalApiCall;

/// <summary>
/// Partner Context configuration for Lumina API calls.
/// Used for telemetry, quota management, and debugging purposes.
/// 
/// Fields are hierarchical - you cannot skip levels:
/// Partner → ScenarioGroup → ScenarioName → Application → Component
/// </summary>
public class PartnerContextConfiguration
{
    /// <summary>
    /// Partner identifier (Required). e.g., "PM playground"
    /// </summary>
    public string Partner { get; set; } = string.Empty;

    /// <summary>
    /// Scenario group name (Optional). e.g., "APIDemo"
    /// </summary>
    public string ScenarioGroup { get; set; } = string.Empty;

    /// <summary>
    /// Specific scenario name (Optional).
    /// Requires ScenarioGroup to be set.
    /// </summary>
    public string ScenarioName { get; set; } = string.Empty;

    /// <summary>
    /// Application identifier (Optional).
    /// Requires ScenarioName to be set.
    /// </summary>
    public string Application { get; set; } = string.Empty;

    /// <summary>
    /// Component identifier (Optional).
    /// Requires Application to be set.
    /// </summary>
    public string Component { get; set; } = string.Empty;

    /// <summary>
    /// Check if the configuration has valid Partner Context.
    /// At minimum, Partner must be set.
    /// </summary>
    public bool HasPartnerContext => !string.IsNullOrWhiteSpace(Partner);

    /// <summary>
    /// Validates the hierarchical structure of Partner Context fields.
    /// Returns true if all non-empty fields follow the correct hierarchy.
    /// </summary>
    public bool IsValid()
    {
        // Partner is required if any field is set
        if (!HasPartnerContext)
        {
            return string.IsNullOrWhiteSpace(ScenarioGroup) &&
                   string.IsNullOrWhiteSpace(ScenarioName) &&
                   string.IsNullOrWhiteSpace(Application) &&
                   string.IsNullOrWhiteSpace(Component);
        }

        // Hierarchical validation: cannot skip levels
        if (!string.IsNullOrWhiteSpace(ScenarioName) && string.IsNullOrWhiteSpace(ScenarioGroup))
            return false;
        if (!string.IsNullOrWhiteSpace(Application) && string.IsNullOrWhiteSpace(ScenarioName))
            return false;
        if (!string.IsNullOrWhiteSpace(Component) && string.IsNullOrWhiteSpace(Application))
            return false;

        return true;
    }

    /// <summary>
    /// Get a summary string for logging purposes.
    /// </summary>
    public string GetSummary()
    {
        if (!HasPartnerContext)
            return "Partner Context: Not configured";

        var parts = new List<string> { $"Partner={Partner}" };
        if (!string.IsNullOrWhiteSpace(ScenarioGroup))
            parts.Add($"ScenarioGroup={ScenarioGroup}");
        if (!string.IsNullOrWhiteSpace(ScenarioName))
            parts.Add($"ScenarioName={ScenarioName}");
        if (!string.IsNullOrWhiteSpace(Application))
            parts.Add($"Application={Application}");
        if (!string.IsNullOrWhiteSpace(Component))
            parts.Add($"Component={Component}");

        return $"Partner Context: {string.Join(", ", parts)}";
    }
}
