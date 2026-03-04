using System.Text.Json.Serialization;

namespace MinimalApiCall;

/// <summary>
/// Represents the user's long-term memory/profile.
/// Mapped from user_profile.json
/// </summary>
public record UserProfile(
    [property: JsonPropertyName("basic_info")] BasicInfo BasicInfo,
    [property: JsonPropertyName("preferences")] Preferences Preferences
);

public record BasicInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("role")] string Role
);

public record Preferences(
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("summary_style")] string SummaryStyle,
    [property: JsonPropertyName("focus_topics")] string[] FocusTopics
);
