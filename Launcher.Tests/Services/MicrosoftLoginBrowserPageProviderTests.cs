/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Globalization;
using Launcher.App.Services;

namespace Launcher.Tests.Services;

public sealed class MicrosoftLoginBrowserPageProviderTests
{
    [Theory]
    [InlineData(
        "zh-Hans",
        "身份验证已完成。",
        "你可以返回 BlockHelm Launcher。请关闭此浏览器标签页。")]
    [InlineData(
        "zh-Hant",
        "身分驗證已完成。",
        "你可以返回 BlockHelm Launcher。請關閉此瀏覽器分頁。")]
    [InlineData(
        "en",
        "Authentication complete.",
        "You can return to BlockHelm Launcher. Please close this browser tab.")]
    [InlineData(
        "ja-JP",
        "認証が完了しました。",
        "BlockHelm Launcher に戻れます。このブラウザー タブを閉じてください。")]
    public void CompletionPageUsesCurrentLauncherLanguage(
        string cultureName,
        string expectedHeading,
        string expectedMessage)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;

            var html = new MicrosoftLoginBrowserPageProvider()
                .GetAuthorizationCompletedHtml();

            Assert.Contains($"lang=\"{cultureName}\"", html);
            Assert.Contains($"<title>{expectedHeading}</title>", html);
            Assert.Contains($"<h2>{expectedHeading}</h2>", html);
            Assert.Contains($"<p>{expectedMessage}</p>", html);
            Assert.DoesNotContain("For your security", html, StringComparison.Ordinal);
            Assert.DoesNotContain("Do not share", html, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }
}
