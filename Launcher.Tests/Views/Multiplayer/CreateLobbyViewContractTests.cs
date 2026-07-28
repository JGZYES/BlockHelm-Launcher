/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Xml.Linq;

namespace Launcher.Tests.Views.Multiplayer;

public sealed class CreateLobbyViewContractTests
{
    [Fact]
    public void SetupKeepsThreeOrderedInstructionsAndOnlyCreateAction()
    {
        var path = Path.Combine(
            FindRepositoryRoot().FullName,
            "Launcher.App",
            "Views",
            "Multiplayer",
            "CreateLobbyView.xaml");
        var source = File.ReadAllText(path);
        var document = XDocument.Load(path);

        var firstStep = source.IndexOf(
            "Multiplayer_Create_StepOpenToLan",
            StringComparison.Ordinal);
        var secondStep = source.IndexOf(
            "Multiplayer_Create_StepSelectInstance",
            StringComparison.Ordinal);
        var thirdStep = source.IndexOf(
            "Multiplayer_Create_StepCreateLobby",
            StringComparison.Ordinal);

        Assert.True(firstStep >= 0);
        Assert.True(secondStep > firstStep);
        Assert.True(thirdStep > secondStep);
        Assert.Equal(1, Count(source, "Command=\"{Binding CreateLobbyCommand}\""));
        Assert.DoesNotContain("LanWorlds", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshLanWorldsCommand", source, StringComparison.Ordinal);
        var setupLayer = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "StackPanel"
            && element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name"
                && attribute.Value == "CreateLobbySetupLayer")));
        Assert.DoesNotContain(
            setupLayer.Descendants(),
            element => element.Name.LocalName == "Border");
    }

    [Fact]
    public void MainWindowHostsCancelableLanWorldDetectionDialog()
    {
        var document = XDocument.Load(Path.Combine(
            FindRepositoryRoot().FullName,
            "Launcher.App",
            "Views",
            "Shell",
            "MainWindow.xaml"));
        var dialog = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "DialogHost"
            && element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name"
                && attribute.Value == "MultiplayerLanWorldDetectionDialogHost")));

        Assert.Equal(
            "{Binding IsLanWorldDetectionDialogOpen}",
            dialog.Attribute("IsOpen")?.Value);
        Assert.Equal(
            "{Binding MultiplayerPage}",
            dialog.Attribute("DataContext")?.Value);
        Assert.Contains(dialog.Descendants(), element =>
            element.Attribute("Text")?.Value ==
            "{x:Static res:Strings.Dialog_MultiplayerLanWorldDetectionTitle}");
        Assert.Contains(dialog.Descendants(), element =>
            element.Attribute("Text")?.Value ==
            "{x:Static res:Strings.Dialog_MultiplayerLanWorldDetectionMessage}");
        var cancelButton = Assert.Single(dialog.Descendants().Where(element =>
            element.Name.LocalName == "Button"));
        Assert.Equal(
            "{Binding CancelLobbyDetectionCommand}",
            cancelButton.Attribute("Command")?.Value);
    }

    [Fact]
    public void DefaultResourcesContainRequestedCreationInstructionsAndDialogCopy()
    {
        var resources = LoadResources("Strings.resx");

        Assert.Equal(
            "第一步：进入要联机的游戏世界。",
            resources["Multiplayer_Create_StepOpenToLan"]);
        Assert.Equal(
            "第二步：在游戏菜单中点击“创建局域网世界”。",
            resources["Multiplayer_Create_StepSelectInstance"]);
        Assert.Equal(
            "第三步：返回启动器，点击“创建房间”。",
            resources["Multiplayer_Create_StepCreateLobby"]);
        Assert.Equal(
            "正在检测局域网世界",
            resources["Dialog_MultiplayerLanWorldDetectionTitle"]);
        Assert.Equal(
            "正在检测局域网世界，请在游戏菜单中点击创建局域网世界。",
            resources["Dialog_MultiplayerLanWorldDetectionMessage"]);
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        var startIndex = 0;
        while ((startIndex = source.IndexOf(value, startIndex, StringComparison.Ordinal)) >= 0)
        {
            count++;
            startIndex += value.Length;
        }

        return count;
    }

    private static IReadOnlyDictionary<string, string> LoadResources(string fileName)
    {
        var path = Path.Combine(
            FindRepositoryRoot().FullName,
            "Launcher.App",
            "Resources",
            fileName);
        return XDocument.Load(path).Root!.Elements("data").ToDictionary(
            element => element.Attribute("name")!.Value,
            element => element.Element("value")?.Value ?? string.Empty,
            StringComparer.Ordinal);
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root.GetFiles("Launcher.sln").Length == 0)
            root = root.Parent ?? throw new DirectoryNotFoundException("Could not locate repository root.");
        return root;
    }
}
