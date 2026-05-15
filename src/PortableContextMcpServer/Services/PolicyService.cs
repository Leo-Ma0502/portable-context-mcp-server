using PortableContextMcpServer.Models;

namespace PortableContextMcpServer.Services;

public sealed class PolicyService : IPolicyService
{
    public bool IsVisible(ContextEntry entry, string toolId)
    {
        var policy = entry.VisibilityPolicy;
        return policy.AllowsTool(toolId) && policy.AllowsCategory(entry.Category);
    }
}
