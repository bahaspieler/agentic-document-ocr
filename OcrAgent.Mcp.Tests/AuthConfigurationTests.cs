using OcrAgent.Mcp.Api;

namespace OcrAgent.Mcp.Tests;

[TestClass]
public sealed class AuthConfigurationTests
{
    [TestMethod]
    public void Validate_DisabledConfiguration_DoesNotRequireAuth0Values()
    {
        new AuthConfiguration().Validate();
    }

    [TestMethod]
    public void Validate_EnabledConfiguration_RejectsDomainWithScheme()
    {
        var configuration = ValidConfiguration();
        configuration.Domain = "https://agentic-document-ocr.eu.auth0.com";

        var error = Assert.ThrowsExactly<InvalidOperationException>(configuration.Validate);

        StringAssert.Contains(error.Message, "without an https:// prefix");
    }

    [TestMethod]
    public void Validate_EnabledConfiguration_RequiresAbsoluteResourceUri()
    {
        var configuration = ValidConfiguration();
        configuration.ResourceUri = "not-a-uri";

        var error = Assert.ThrowsExactly<InvalidOperationException>(configuration.Validate);

        StringAssert.Contains(error.Message, "absolute HTTP or HTTPS URI");
    }

    [TestMethod]
    public void Validate_EnabledConfiguration_RequiresAudienceToEqualResource()
    {
        var configuration = ValidConfiguration();
        configuration.Audience = "https://different.example.test/";

        var error = Assert.ThrowsExactly<InvalidOperationException>(configuration.Validate);

        StringAssert.Contains(error.Message, "must equal");
    }

    [TestMethod]
    public void Validate_EnabledConfiguration_RejectsMalformedApiKeyHash()
    {
        var configuration = ValidConfiguration();
        configuration.ApiKeys["Professor"] = "not-a-hash";

        var error = Assert.ThrowsExactly<InvalidOperationException>(configuration.Validate);

        StringAssert.Contains(error.Message, "64-character SHA-256");
    }

    [TestMethod]
    public void AuthorizationServer_UsesConfiguredAuth0Domain()
    {
        var configuration = ValidConfiguration();

        configuration.Validate();

        Assert.AreEqual(
            "https://agentic-document-ocr.eu.auth0.com/",
            configuration.AuthorizationServer);
    }

    private static AuthConfiguration ValidConfiguration() => new()
    {
        Enabled = true,
        Domain = "agentic-document-ocr.eu.auth0.com",
        ClientId = "public-spa-client-id",
        Audience = "https://ocr.example.test/",
        ScopeName = "access:ocr",
        ResourceUri = "https://ocr.example.test/",
    };
}
