/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Reflection;
using System.Text.Json.Nodes;
using CmlLib.Core.Auth.Microsoft;
using CmlLib.Core.Auth.Microsoft.Sessions;
using Launcher.Application.Accounts;
using Launcher.Infrastructure;
using Launcher.Infrastructure.Accounts;
using Launcher.Infrastructure.Accounts.Credentials;
using Microsoft.Identity.Client;
using XboxAuthNet.Game.Accounts;
using XboxAuthNet.Game.Msal.OAuth;

namespace Launcher.Tests.Infrastructure.Accounts;

public sealed class MicrosoftAuthenticationConfigurationTests
{
    private const string ClientId = "11111111-2222-3333-4444-555555555555";

    [Fact]
    public void ClientIdProviderNormalizesConfiguredApplicationIdentity()
    {
        var provider = new MicrosoftClientIdProvider(
            embeddedValueProvider: () => $"  {ClientId.ToUpperInvariant()}  ",
            environmentValueProvider: () => null,
            localConfigurationPathProvider: () => []);

        var result = provider.GetRequiredClientId();

        Assert.Equal(ClientId, result);
    }

    [Fact]
    public void ClientIdProviderDoesNotFallBackToAnUpstreamApplicationIdentity()
    {
        var provider = new MicrosoftClientIdProvider(
            embeddedValueProvider: () => null,
            environmentValueProvider: () => null,
            localConfigurationPathProvider: () => []);

        Assert.Throws<MicrosoftAuthenticationConfigurationException>(
            provider.GetRequiredClientId);
    }

    [Fact]
    public void LoginHandlerUsesMsalOAuthProvider()
    {
        var accountManager = new InMemoryXboxGameAccountManager(JEGameAccount.FromSessionStorage);
        var msalApplication = PublicClientApplicationBuilder.Create(ClientId).Build();

        var handler = MicrosoftAuthProvider.CreateLoginHandler(accountManager, msalApplication);

        var providerField = typeof(JELoginHandler).GetField(
            "_defaultOAuthProvider",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var provider = providerField?.GetValue(handler);
        Assert.IsType<MsalCodeFlowProvider>(provider);
        Assert.Equal(ClientId, msalApplication.AppConfig.ClientId);
    }

    [Fact]
    public void LoginHandlerUsesLocalizedBrowserCompletionProviderWhenAvailable()
    {
        var accountManager = new InMemoryXboxGameAccountManager(JEGameAccount.FromSessionStorage);
        var msalApplication = PublicClientApplicationBuilder.Create(ClientId).Build();
        var pageProvider = new StubBrowserPageProvider("<html><body>Complete</body></html>");

        var handler = MicrosoftAuthProvider.CreateLoginHandler(
            accountManager,
            msalApplication,
            pageProvider);

        var providerField = typeof(JELoginHandler).GetField(
            "_defaultOAuthProvider",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var provider = Assert.IsType<BrowserCompletionMsalCodeFlowProvider>(
            providerField?.GetValue(handler));
        Assert.Equal(
            "<html><body>Complete</body></html>",
            provider.CreateSystemWebViewOptions().HtmlMessageSuccess);
    }

    [Fact]
    public void ClientIdentityMigrationRemovesLegacyTokensButPreservesAccountProfile()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"launcher-microsoft-client-migration-{Guid.NewGuid():N}");
        try
        {
            var pathProvider = new LauncherPathProvider(
                applicationBaseDirectory: directory,
                applicationDataDirectory: directory);
            var credentialPath = Path.Combine(
                pathProvider.DefaultAccountDataDirectory,
                "microsoft",
                "credentials.dat");
            var credentialStorage = new DpapiMicrosoftJsonStorage(credentialPath);
            credentialStorage.Write(
                new JsonObject
                {
                    ["account"] = new JsonObject
                    {
                        ["MicrosoftOAuth"] = new JsonObject { ["refreshToken"] = "legacy-refresh-token" },
                        ["XboxTokens"] = new JsonObject { ["xstsToken"] = "legacy-xsts-token" },
                        ["JEToken"] = new JsonObject { ["accessToken"] = "legacy-minecraft-token" },
                        ["JEProfile"] = new JsonObject
                        {
                            ["id"] = "0123456789abcdef0123456789abcdef",
                            ["name"] = "Player"
                        },
                        ["MicrosoftOAuthLoginHint"] = "player@example.com"
                    }
                },
                null);

            MicrosoftCredentialSessionMigration.EnsureClientIdentity(
                credentialStorage,
                pathProvider,
                ClientId);

            var migratedRoot = Assert.IsType<JsonObject>(credentialStorage.ReadAsJsonNode());
            var account = Assert.IsType<JsonObject>(migratedRoot["account"]);
            Assert.False(account.ContainsKey("MicrosoftOAuth"));
            Assert.False(account.ContainsKey("XboxTokens"));
            Assert.False(account.ContainsKey("JEToken"));
            Assert.NotNull(account["JEProfile"]);
            Assert.Equal("player@example.com", account["MicrosoftOAuthLoginHint"]?.GetValue<string>());

            var markerPath = Path.Combine(
                pathProvider.DefaultAccountDataDirectory,
                "microsoft",
                "authentication-client-id");
            Assert.Equal(ClientId, File.ReadAllText(markerPath));

            var credentialBytes = File.ReadAllBytes(credentialPath);
            MicrosoftCredentialSessionMigration.EnsureClientIdentity(
                credentialStorage,
                pathProvider,
                ClientId);
            Assert.Equal(credentialBytes, File.ReadAllBytes(credentialPath));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ForbiddenMinecraftAuthenticationIsReportedAsApplicationAuthorizationFailure()
    {
        var source = new JEAuthException(
            "Minecraft authentication failed.",
            "Invalid app registration",
            "The application is not authorized.",
            403);

        var result = MicrosoftAuthProvider.TranslateAuthenticationException(source);

        Assert.Equal(
            LaunchAccountSessionFailureReason.AuthenticationApplicationNotAuthorized,
            result.Reason);
    }

    [Fact]
    public void MissingMsalLoginHintRequiresInteractiveReauthentication()
    {
        var source = new MsalException(
            "loginHint was empty. Interactive Microsoft OAuth with IdToken is required. (ex: MsalInteractiveOAuth)");

        var result = MicrosoftAuthProvider.TranslateAuthenticationException(source);

        Assert.Equal(
            LaunchAccountSessionFailureReason.ReauthenticationRequired,
            result.Reason);
    }

    private sealed class StubBrowserPageProvider(string html) : IMicrosoftLoginBrowserPageProvider
    {
        public string GetAuthorizationCompletedHtml() => html;
    }
}
