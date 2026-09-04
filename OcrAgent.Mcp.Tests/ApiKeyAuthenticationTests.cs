using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using OcrAgent.Mcp.Api;

namespace OcrAgent.Mcp.Tests;

[TestClass]
public sealed class ApiKeyAuthenticationTests
{
    [TestMethod]
    public async Task AuthenticateAsync_KnownKey_ReturnsNamedIdentity()
    {
        const string rawKey = "hlocr_test-value";
        var context = CreateContext(new Dictionary<string, string>
        {
            ["Professor"] = Hash(rawKey),
            ["Team"] = Hash("different-value"),
        });
        context.Request.Headers[ApiKeyAuthenticationOptions.HeaderName] = rawKey;

        var result = await context.AuthenticateAsync(AuthSchemes.ApiKey);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("Professor", result.Principal.Identity?.Name);
        Assert.IsTrue(result.Principal.HasClaim("auth_method", "api_key"));
    }

    [TestMethod]
    public async Task AuthenticateAsync_UnknownKey_FailsWithoutRevealingConfiguredNames()
    {
        var context = CreateContext(new Dictionary<string, string>
        {
            ["Professor"] = Hash("expected-value"),
        });
        context.Request.Headers[ApiKeyAuthenticationOptions.HeaderName] = "wrong-value";

        var result = await context.AuthenticateAsync(AuthSchemes.ApiKey);

        Assert.IsFalse(result.Succeeded);
        Assert.IsNotNull(result.Failure);
        Assert.IsFalse(result.Failure.Message.Contains("Professor", StringComparison.Ordinal));
    }

    private static DefaultHttpContext CreateContext(IReadOnlyDictionary<string, string> hashes)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services
            .AddAuthentication()
            .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
                AuthSchemes.ApiKey,
                options => options.KeyHashes = hashes);
        return new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
        };
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
