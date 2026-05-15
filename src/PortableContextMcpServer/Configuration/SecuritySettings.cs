namespace PortableContextMcpServer.Configuration;

public sealed class SecuritySettings
{
    public string[] AllowedTools { get; init; } = Array.Empty<string>();
}
