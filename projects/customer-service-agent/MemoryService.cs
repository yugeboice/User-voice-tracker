using System.Text;
using System.Text.Json;

namespace MinimalApiCall;

public class MemoryService
{
    private const string ProfilePath = "user_profile.json";

    public async Task<UserProfile?> LoadUserProfileAsync()
    {
        try 
        {
            if (!File.Exists(ProfilePath)) return null;
            var json = await File.ReadAllTextAsync(ProfilePath);
            return JsonSerializer.Deserialize<UserProfile>(json);
        }
        catch 
        {
            return null;
        }
    }

    public string BuildSystemPrompt(UserProfile? profile)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a helpful AI assistant.");
        
        if (profile != null)
        {
            sb.AppendLine("\nUser Profile & Preferences:");
            sb.AppendLine($"- Name: {profile.BasicInfo?.Name}");
            sb.AppendLine($"- Role: {profile.BasicInfo?.Role}");
            sb.AppendLine($"- Language: {profile.Preferences?.Language}");
            sb.AppendLine($"- Style: {profile.Preferences?.SummaryStyle}");
            if (profile.Preferences?.FocusTopics != null)
            {
                sb.AppendLine($"- Focus Topics: {string.Join(", ", profile.Preferences.FocusTopics)}");
            }
            sb.AppendLine("\nPlease adapt your responses according to these preferences.");
        }

        return sb.ToString();
    }
}
