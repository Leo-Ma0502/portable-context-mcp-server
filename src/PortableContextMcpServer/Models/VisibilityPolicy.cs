namespace PortableContextMcpServer.Models;

public sealed class VisibilityPolicy
{
    public static VisibilityPolicy Default => new() { AllowedCategories = new[] { "preference", "personality", "skill", "experience", "relationship", "memory" } };

    public string[] AllowedCategories { get; init; } = Array.Empty<string>();
    public string[] AllowedToolIds { get; init; } = Array.Empty<string>();

    public bool AllowsCategory(string category)
    {
        return AllowedCategories.Length == 0 || AllowedCategories.Contains(category, StringComparer.OrdinalIgnoreCase);
    }

    public bool AllowsTool(string toolId)
    {
        return AllowedToolIds.Length == 0 || AllowedToolIds.Contains(toolId, StringComparer.OrdinalIgnoreCase);
    }
}
