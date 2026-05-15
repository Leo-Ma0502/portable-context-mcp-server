namespace PortableContextMcpServer.Configuration;

public sealed class AuthSettings
{
    public string Mode { get; init; } = "BearerToken";
    public string[] Tokens { get; init; } = Array.Empty<string>();

    public bool IsBearerTokenEnabled => string.Equals(Mode, "BearerToken", StringComparison.OrdinalIgnoreCase);
    public bool IsDisabled => string.Equals(Mode, "Disabled", StringComparison.OrdinalIgnoreCase);
}
