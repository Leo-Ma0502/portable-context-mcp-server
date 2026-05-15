using PortableContextMcpServer.Models;

namespace PortableContextMcpServer.Services;

public interface IPolicyService
{
    bool IsVisible(ContextEntry entry, string toolId);
}
