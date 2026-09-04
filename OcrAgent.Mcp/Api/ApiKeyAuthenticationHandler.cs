using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace OcrAgent.Mcp.Api;

/// <summary>Authenticates a request by comparing a supplied API key with configured hashes.</summary>
internal sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<ApiKeyAuthenticationOptions>(options, logger, encoder)
{
    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyAuthenticationOptions.HeaderName, out var values))
            return Task.FromResult(AuthenticateResult.NoResult());

        if (values.Count != 1 || string.IsNullOrWhiteSpace(values[0]))
            return Task.FromResult(AuthenticateResult.Fail("A single non-empty API key is required."));

        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(values[0]!));
        foreach (var (name, configuredHash) in Options.KeyHashes)
        {
            var expectedHash = Convert.FromHexString(configuredHash);
            if (!CryptographicOperations.FixedTimeEquals(suppliedHash, expectedHash))
                continue;

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, $"api-key:{name}"),
                new Claim(ClaimTypes.Name, name),
                new Claim("sub", $"api-key:{name}"),
                new Claim("auth_method", "api_key"),
            };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
            return Task.FromResult(
                AuthenticateResult.Success(
                    new AuthenticationTicket(principal, Scheme.Name)));
        }

        return Task.FromResult(AuthenticateResult.Fail("The API key is invalid."));
    }
}
