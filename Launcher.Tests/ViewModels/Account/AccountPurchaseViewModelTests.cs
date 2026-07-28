/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.ViewModels.Account;
using Launcher.Application;

namespace Launcher.Tests.ViewModels.Account;

public sealed class AccountPurchaseViewModelTests
{
    [Fact]
    public void OpenMinecraftPurchasePageUsesOfficialPurchaseUrl()
    {
        var links = new RecordingExternalLinkService { OpenResult = true };
        var messages = new RecordingMessageService();
        var viewModel = new AccountPurchaseViewModel(links, messages, messages);

        viewModel.OpenMinecraftPurchasePageCommand.Execute(null);

        Assert.Equal(LauncherProjectLinks.MinecraftPurchaseUrl, links.LastUrl);
        Assert.Null(messages.StatusMessage);
        Assert.Null(messages.FloatingMessage);
    }

    [Fact]
    public void OpenMinecraftPurchasePageFailureReportsFriendlyMessage()
    {
        var links = new RecordingExternalLinkService();
        var messages = new RecordingMessageService();
        var viewModel = new AccountPurchaseViewModel(links, messages, messages);

        viewModel.OpenMinecraftPurchasePageCommand.Execute(null);

        Assert.Equal(Strings.Status_OpenMinecraftPurchasePageFailed, messages.StatusMessage);
        Assert.Equal(Strings.Status_OpenMinecraftPurchasePageFailed, messages.FloatingMessage);
    }

    private sealed class RecordingExternalLinkService : IExternalLinkService
    {
        public bool OpenResult { get; init; }

        public string? LastUrl { get; private set; }

        public bool TryOpen(string url)
        {
            LastUrl = url;
            return OpenResult;
        }
    }

    private sealed class RecordingMessageService : IStatusService, IFloatingMessageService
    {
        public event Action<string>? MessageReported;
        public event Action<string>? MessageRequested;

        public string? StatusMessage { get; private set; }

        public string? FloatingMessage { get; private set; }

        public void Report(string message)
        {
            StatusMessage = message;
            MessageReported?.Invoke(message);
        }

        public void Show(string message)
        {
            FloatingMessage = message;
            MessageRequested?.Invoke(message);
        }
    }
}
