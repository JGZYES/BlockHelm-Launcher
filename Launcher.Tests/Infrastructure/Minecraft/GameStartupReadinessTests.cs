/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Infrastructure.Minecraft;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class GameStartupReadinessTests
{
    [Theory]
    [InlineData("[Render thread/INFO]: OpenAL initialized on device OpenAL Soft")]
    [InlineData("<log4j:Message><![CDATA[OpenAL initialized on device OpenAL Soft]]></log4j:Message>")]
    [InlineData("[Render thread/INFO]: Sound engine started")]
    [InlineData("[Render thread/INFO]: Created: 512x256x0 minecraft:textures/atlas/particles.png-atlas")]
    [InlineData("<log4j:Message><![CDATA[Found animation info]]></log4j:Message>")]
    public void RecognizesStrongMinecraftStartupMilestones(string line)
    {
        Assert.True(GameStartupLogReadinessDetector.IsReadyLine(line));
    }

    [Theory]
    [InlineData("[Download-2/ERROR]: Failed to fetch Realms feature flags")]
    [InlineData("Could not authorize you against Realms server: HTTP 401 Unauthorized")]
    [InlineData("[main/INFO]: Loading Minecraft with Fabric Loader")]
    public void IgnoresOutputThatDoesNotProveGameReadiness(string line)
    {
        Assert.False(GameStartupLogReadinessDetector.IsReadyLine(line));
    }
}
