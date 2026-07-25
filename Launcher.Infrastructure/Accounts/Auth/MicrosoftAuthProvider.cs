/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using CmlLib.Core.Auth.Microsoft;
using CmlLib.Core.Auth.Microsoft.Sessions;
using Launcher.Infrastructure.Accounts.Credentials;
using Launcher.Application.Accounts;
using Launcher.Infrastructure;
using Microsoft.Identity.Client;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using XboxAuthNet.Game.Accounts;
using XboxAuthNet.Game.Msal;
using XboxAuthNet.Game.Msal.OAuth;
using XboxAuthNet.Game.SessionStorages;
using XboxAuthNet.OAuth;

namespace Launcher.Infrastructure.Accounts;

internal sealed class MicrosoftAuthProvider
{
    private readonly DpapiMicrosoftJsonStorage credentialStorage;
    private readonly MicrosoftClientIdProvider clientIdProvider;
    private readonly SemaphoreSlim loginHandlerGate = new(1, 1);
    private readonly Lazy<Task<IPublicClientApplication>> msalApplication;
    private JsonXboxGameAccountManager accountManager;
    private JELoginHandler? loginHandler;

    public MicrosoftAuthProvider(LauncherPathProvider pathProvider)
        : this(pathProvider, new MicrosoftClientIdProvider())
    {
    }

    internal MicrosoftAuthProvider(
        LauncherPathProvider pathProvider,
        MicrosoftClientIdProvider clientIdProvider)
    {
        credentialStorage = new DpapiMicrosoftJsonStorage(pathProvider);
        this.clientIdProvider = clientIdProvider;
        accountManager = CreatePersistentAccountManager();
        msalApplication = new Lazy<Task<IPublicClientApplication>>(
            async () =>
            {
                var clientId = clientIdProvider.GetRequiredClientId();
                MicrosoftCredentialSessionMigration.EnsureClientIdentity(
                    credentialStorage,
                    pathProvider,
                    clientId);
                accountManager = CreatePersistentAccountManager();
                loginHandler = null;
                return await MsalClientHelper.BuildApplicationWithCache(clientId).ConfigureAwait(false);
            },
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public IEnumerable<JEGameAccount> GetSavedAccounts()
    {
        return accountManager.GetAccounts().OfType<JEGameAccount>().ToArray();
    }

    public async Task<MicrosoftLoginResult> LoginInteractivelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var (account, session) = await AuthenticateInMemoryAsync(cancellationToken);
            CommitSession(account.SessionStorage);
            var refreshedAccount = JEGameAccount.FromSessionStorage(account.SessionStorage);
            var profile = refreshedAccount.Profile;
            var accessToken = refreshedAccount.Token?.AccessToken;
            return new MicrosoftLoginResult(profile, session.Username, session.UUID, accessToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (MicrosoftAccountAuthenticationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw TranslateAuthenticationException(exception);
        }
    }

    public async Task<MicrosoftLoginResult> ReauthenticateInteractivelyAsync(
        LauncherAccount existingAccount,
        CancellationToken cancellationToken)
    {
        try
        {
            var (account, session) = await AuthenticateInMemoryAsync(cancellationToken);
            var refreshedAccount = JEGameAccount.FromSessionStorage(account.SessionStorage);
            var refreshedUuid = MinecraftAccountHelpers.NormalizeUuid(
                refreshedAccount.Profile?.UUID ?? session.UUID);
            if (string.IsNullOrWhiteSpace(existingAccount.Uuid)
                || !string.Equals(refreshedUuid, existingAccount.Uuid, StringComparison.OrdinalIgnoreCase))
            {
                throw new MicrosoftAccountAuthenticationException(
                    LaunchAccountSessionFailureReason.ReauthenticationRequired,
                    "The signed-in Microsoft account does not match the selected launcher account.");
            }

            CommitSession(account.SessionStorage);
            return new MicrosoftLoginResult(
                refreshedAccount.Profile,
                session.Username,
                session.UUID,
                refreshedAccount.Token?.AccessToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (MicrosoftAccountAuthenticationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw TranslateAuthenticationException(exception);
        }
    }

    public async Task<bool> DeleteAccountAsync(LauncherAccount account, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(account.Uuid))
            return false;

        var handler = await GetPersistentLoginHandlerAsync(cancellationToken);
        foreach (var savedAccount in GetSavedAccounts())
        {
            var savedUuid = MinecraftAccountHelpers.NormalizeUuid(savedAccount.Profile?.UUID);
            if (!string.Equals(savedUuid, account.Uuid, StringComparison.OrdinalIgnoreCase))
                continue;

            await handler.Signout(savedAccount, cancellationToken);
            handler.AccountManager.SaveAccounts();
            return true;
        }

        return false;
    }

    public async Task<string> GetAccessTokenAsync(LauncherAccount account, CancellationToken cancellationToken)
    {
        if (!account.IsMicrosoft || string.IsNullOrWhiteSpace(account.Uuid))
            throw new InvalidOperationException("\u53ea\u6709\u6b63\u7248\u8d26\u6237\u652f\u6301\u6b64\u64cd\u4f5c");

        try
        {
            var handler = await GetPersistentLoginHandlerAsync(cancellationToken);
            var savedAccount = FindSavedAccount(account);
            if (savedAccount is null)
            {
                throw new MicrosoftAccountAuthenticationException(
                    LaunchAccountSessionFailureReason.ReauthenticationRequired,
                    "Microsoft account credentials are missing.");
            }

            await handler.AuthenticateSilently(savedAccount, cancellationToken);
            handler.AccountManager.SaveAccounts();

            var refreshedAccount = JEGameAccount.FromSessionStorage(savedAccount.SessionStorage);
            var accessToken = refreshedAccount.Token?.AccessToken ?? savedAccount.Token?.AccessToken;
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new MicrosoftAccountAuthenticationException(
                    LaunchAccountSessionFailureReason.ReauthenticationRequired,
                    "Microsoft account access token is missing.");
            }

            return accessToken;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (MicrosoftAccountAuthenticationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw TranslateAuthenticationException(exception);
        }
    }

    public void UpdateSavedProfile(LauncherAccount account, string displayName, string uuid)
    {
        if (string.IsNullOrWhiteSpace(uuid) || string.IsNullOrWhiteSpace(displayName))
            return;

        var savedAccount = FindSavedAccount(account);
        if (savedAccount?.Profile is null)
            return;

        savedAccount.Profile.Username = displayName;
        savedAccount.Profile.UUID = uuid;
        accountManager.SaveAccounts();
    }

    private JEGameAccount? FindSavedAccount(LauncherAccount account)
    {
        var targetUuid = MinecraftAccountHelpers.NormalizeUuid(account.Uuid);
        return GetSavedAccounts()
            .FirstOrDefault(savedAccount =>
            {
                var savedUuid = MinecraftAccountHelpers.NormalizeUuid(savedAccount.Profile?.UUID);
                if (string.IsNullOrWhiteSpace(savedUuid))
                {
                    savedUuid = MinecraftAccountHelpers.NormalizeUuid(
                        JEGameAccount.FromSessionStorage(savedAccount.SessionStorage).Profile?.UUID);
                }

                return string.Equals(savedUuid, targetUuid, StringComparison.OrdinalIgnoreCase);
            });
    }

    private JsonXboxGameAccountManager CreatePersistentAccountManager()
    {
        return new JsonXboxGameAccountManager(
            credentialStorage,
            JEGameAccount.FromSessionStorage,
            JsonXboxGameAccountManager.DefaultSerializerOption);
    }

    private async Task<JELoginHandler> GetPersistentLoginHandlerAsync(CancellationToken cancellationToken)
    {
        if (loginHandler is not null)
            return loginHandler;

        await loginHandlerGate.WaitAsync(cancellationToken);
        try
        {
            if (loginHandler is not null)
                return loginHandler;

            var app = await msalApplication.Value.WaitAsync(cancellationToken);
            loginHandler = CreateLoginHandler(accountManager, app);
            return loginHandler;
        }
        finally
        {
            loginHandlerGate.Release();
        }
    }

    private async Task<(JEGameAccount Account, CmlLib.Core.Auth.MSession Session)> AuthenticateInMemoryAsync(
        CancellationToken cancellationToken)
    {
        var app = await msalApplication.Value.WaitAsync(cancellationToken);
        var accountManager = new InMemoryXboxGameAccountManager(JEGameAccount.FromSessionStorage);
        var handler = CreateLoginHandler(accountManager, app);
        var account = (JEGameAccount)accountManager.NewAccount();
        var session = await handler.AuthenticateInteractively(account, cancellationToken);
        return (account, session);
    }

    private void CommitSession(ISessionStorage source)
    {
        try
        {
            var sessionStorage = JsonSessionStorage.CreateEmpty(
                JsonXboxGameAccountManager.DefaultSerializerOption);
            foreach (var key in source.Keys.ToArray())
            {
                var value = source.Get<object>(key);
                sessionStorage.Set(key, value);
                sessionStorage.SetKeyMode(key, source.GetKeyMode(key));
            }

            var account = JEGameAccount.FromSessionStorage(sessionStorage);
            if (string.IsNullOrWhiteSpace(account.Identifier))
                throw new InvalidDataException("Microsoft account identifier is missing.");

            var root = credentialStorage.ReadAsJsonNode() as JsonObject ?? new JsonObject();
            root[account.Identifier] = sessionStorage.ToJsonObjectForStoring();
            credentialStorage.Write(root, JsonXboxGameAccountManager.DefaultSerializerOption);
            accountManager = CreatePersistentAccountManager();
            loginHandler = null;
        }
        catch (MicrosoftCredentialStorageException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new MicrosoftCredentialStorageException(
                "Microsoft account credentials could not be saved.",
                exception);
        }
    }

    private static bool RequiresInteractiveLogin(MicrosoftOAuthException exception)
    {
        return exception.StatusCode is 0 or (int)HttpStatusCode.BadRequest or (int)HttpStatusCode.Unauthorized
            || string.Equals(exception.Error, "invalid_grant", StringComparison.OrdinalIgnoreCase)
            || string.Equals(exception.Error, "interaction_required", StringComparison.OrdinalIgnoreCase)
            || string.Equals(exception.Error, "login_required", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("Interactive Microsoft authentication is required", StringComparison.OrdinalIgnoreCase);
    }

    private static bool RequiresInteractiveLogin(MsalException exception)
    {
        return exception is MsalUiRequiredException
            || ContainsInteractiveLoginRequirement(exception.ErrorCode)
            || ContainsInteractiveLoginRequirement(exception.Message);
    }

    private static bool ContainsInteractiveLoginRequirement(string? value)
    {
        return value?.Contains("loginHint was empty", StringComparison.OrdinalIgnoreCase) == true
            || value?.Contains("MsalInteractiveOAuth", StringComparison.OrdinalIgnoreCase) == true
            || value?.Contains("Interactive Microsoft OAuth", StringComparison.OrdinalIgnoreCase) == true;
    }

    internal static JELoginHandler CreateLoginHandler(
        IXboxGameAccountManager accountManager,
        IPublicClientApplication msalApplication)
    {
        ArgumentNullException.ThrowIfNull(accountManager);
        ArgumentNullException.ThrowIfNull(msalApplication);

        return new JELoginHandlerBuilder()
            .WithAccountManager(accountManager)
            .WithOAuthProvider(new MsalCodeFlowProvider(msalApplication))
            .Build();
    }

    internal static MicrosoftAccountAuthenticationException TranslateAuthenticationException(Exception exception)
    {
        return exception switch
        {
            MicrosoftAuthenticationConfigurationException => new MicrosoftAccountAuthenticationException(
                LaunchAccountSessionFailureReason.AuthenticationNotConfigured,
                "Microsoft account login is not configured.",
                exception),
            MicrosoftCredentialStorageException => new MicrosoftAccountAuthenticationException(
                LaunchAccountSessionFailureReason.CredentialStorageFailed,
                "Microsoft account credentials could not be accessed.",
                exception),
            MsalUiRequiredException => new MicrosoftAccountAuthenticationException(
                LaunchAccountSessionFailureReason.ReauthenticationRequired,
                "Interactive Microsoft authentication is required.",
                exception),
            MsalException msalException when RequiresInteractiveLogin(msalException)
                => new MicrosoftAccountAuthenticationException(
                    LaunchAccountSessionFailureReason.ReauthenticationRequired,
                    "Interactive Microsoft authentication is required.",
                    exception),
            MsalServiceException msalException when msalException.StatusCode >= 500
                => new MicrosoftAccountAuthenticationException(
                    LaunchAccountSessionFailureReason.AuthenticationServerUnavailable,
                    "Microsoft authentication services are unavailable.",
                    exception),
            MsalException => new MicrosoftAccountAuthenticationException(
                LaunchAccountSessionFailureReason.InvalidAuthenticationResponse,
                "Microsoft authentication failed.",
                exception),
            MicrosoftOAuthException oauthException when RequiresInteractiveLogin(oauthException)
                => new MicrosoftAccountAuthenticationException(
                    LaunchAccountSessionFailureReason.ReauthenticationRequired,
                    "Microsoft account credentials have expired.",
                    exception),
            MicrosoftOAuthException oauthException when oauthException.StatusCode >= 500
                => new MicrosoftAccountAuthenticationException(
                    LaunchAccountSessionFailureReason.AuthenticationServerUnavailable,
                    "Microsoft authentication services are unavailable.",
                    exception),
            MicrosoftOAuthException => new MicrosoftAccountAuthenticationException(
                LaunchAccountSessionFailureReason.InvalidAuthenticationResponse,
                "Microsoft authentication failed.",
                exception),
            JEAuthException jeException when jeException.StatusCode == (int)HttpStatusCode.Forbidden
                => new MicrosoftAccountAuthenticationException(
                    LaunchAccountSessionFailureReason.AuthenticationApplicationNotAuthorized,
                    "The Microsoft application is not authorized by Minecraft services.",
                    exception),
            JEAuthException jeException when jeException.StatusCode == (int)HttpStatusCode.Unauthorized
                => new MicrosoftAccountAuthenticationException(
                    LaunchAccountSessionFailureReason.ReauthenticationRequired,
                    "Microsoft account credentials were rejected.",
                    exception),
            JEAuthException jeException when jeException.StatusCode >= 500
                => new MicrosoftAccountAuthenticationException(
                    LaunchAccountSessionFailureReason.AuthenticationServerUnavailable,
                    "Minecraft authentication services are unavailable.",
                    exception),
            JEAuthException => new MicrosoftAccountAuthenticationException(
                LaunchAccountSessionFailureReason.InvalidAuthenticationResponse,
                "Minecraft authentication failed.",
                exception),
            HttpRequestException => new MicrosoftAccountAuthenticationException(
                LaunchAccountSessionFailureReason.AuthenticationServerUnavailable,
                "Microsoft authentication services are unavailable.",
                exception),
            JsonException => new MicrosoftAccountAuthenticationException(
                LaunchAccountSessionFailureReason.InvalidAuthenticationResponse,
                "Microsoft authentication returned an invalid response.",
                exception),
            _ => new MicrosoftAccountAuthenticationException(
                LaunchAccountSessionFailureReason.Unknown,
                "Microsoft authentication failed.",
                exception)
        };
    }
}
