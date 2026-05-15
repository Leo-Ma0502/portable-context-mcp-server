using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using PortableContextMcpServer.Configuration;

namespace PortableContextMcpServer.Services;

public sealed class BearerTokenValidator
{
    private readonly AuthSettings _settings;

    public BearerTokenValidator(IOptions<AuthSettings> settings)
    {
        _settings = settings.Value;
    }

    public bool IsEnabled => _settings.IsBearerTokenEnabled;
    public bool IsDisabled => _settings.IsDisabled;
    public bool HasTokens => _settings.Tokens.Any(token => !string.IsNullOrWhiteSpace(token));

    public bool IsValid(string? authorizationHeader)
    {
        if (IsDisabled)
        {
            return true;
        }

        if (!IsEnabled || string.IsNullOrWhiteSpace(authorizationHeader))
        {
            return false;
        }

        const string prefix = "Bearer ";
        if (!authorizationHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var providedToken = authorizationHeader[prefix.Length..].Trim();
        if (providedToken.Length == 0)
        {
            return false;
        }

        return _settings.Tokens
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Any(token => FixedTimeEquals(providedToken, token));
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
