/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Launcher.Application.Accounts;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Accounts;
using Launcher.Tests.Helpers;

namespace Launcher.Tests.Infrastructure.Accounts;

public sealed class OfflineSkinYggdrasilServiceTests : TestTempDirectory
{
    [Theory]
    [InlineData(MinecraftSkinModel.Classic, false)]
    [InlineData(MinecraftSkinModel.Slim, true)]
    public async Task ServesSignedProfileAndExactTexture(
        MinecraftSkinModel model,
        bool expectsSlimMetadata)
    {
        Directory.CreateDirectory(TempRoot);
        var skinPath = Path.Combine(TempRoot, $"{model}.png");
        var skinBytes = CreateSkinPng();
        await File.WriteAllBytesAsync(skinPath, skinBytes);
        using var service = new OfflineSkinYggdrasilService(new MinecraftSkinFileValidator());
        var account = CreateAccount(skinPath, model);

        var context = await service.PrepareAsync(
            account,
            "00112233445566778899aabbccddeeff");

        Assert.NotNull(context);
        using var client = new HttpClient { BaseAddress = new Uri(context.AuthenticationServerUrl) };
        var metadata = JsonDocument.Parse(await client.GetByteArrayAsync("/"));
        var publicKey = metadata.RootElement.GetProperty("signaturePublickey").GetString();
        Assert.Contains("127.0.0.1", metadata.RootElement.GetProperty("skinDomains")
            .EnumerateArray().Select(value => value.GetString()));

        var profileResponse = await client.GetAsync(
            "/sessionserver/session/minecraft/profile/00112233445566778899aabbccddeeff?unsigned=false");
        profileResponse.EnsureSuccessStatusCode();
        var profile = JsonDocument.Parse(await profileResponse.Content.ReadAsByteArrayAsync());
        Assert.Equal("00112233445566778899aabbccddeeff", profile.RootElement.GetProperty("id").GetString());
        Assert.Equal("OfflinePlayer", profile.RootElement.GetProperty("name").GetString());
        var property = profile.RootElement.GetProperty("properties")[0];
        var value = property.GetProperty("value").GetString()!;
        var signature = Convert.FromBase64String(property.GetProperty("signature").GetString()!);
        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKey);
        Assert.True(rsa.VerifyData(
            Encoding.UTF8.GetBytes(value),
            signature,
            HashAlgorithmName.SHA1,
            RSASignaturePadding.Pkcs1));

        var texturePayload = JsonDocument.Parse(Convert.FromBase64String(value));
        var skin = texturePayload.RootElement.GetProperty("textures").GetProperty("SKIN");
        Assert.Equal(
            "00112233445566778899aabbccddeeff",
            texturePayload.RootElement.GetProperty("profileId").GetString());
        Assert.Equal(expectsSlimMetadata, skin.TryGetProperty("metadata", out var metadataElement));
        if (expectsSlimMetadata)
            Assert.Equal("slim", metadataElement.GetProperty("model").GetString());

        var textureResponse = await client.GetAsync(skin.GetProperty("url").GetString());
        textureResponse.EnsureSuccessStatusCode();
        Assert.Equal("image/png", textureResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(skinBytes, await textureResponse.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task SupportsNameLookup()
    {
        Directory.CreateDirectory(TempRoot);
        var skinPath = Path.Combine(TempRoot, "skin.png");
        await File.WriteAllBytesAsync(skinPath, CreateSkinPng());
        using var service = new OfflineSkinYggdrasilService(new MinecraftSkinFileValidator());
        var context = await service.PrepareAsync(
            CreateAccount(skinPath, MinecraftSkinModel.Classic),
            "00112233-4455-6677-8899-aabbccddeeff");
        Assert.NotNull(context);
        using var client = new HttpClient { BaseAddress = new Uri(context.AuthenticationServerUrl) };

        var batch = await client.PostAsync(
            "/api/profiles/minecraft",
            new StringContent("[\"OfflinePlayer\",\"Missing\"]", Encoding.UTF8, "application/json"));
        batch.EnsureSuccessStatusCode();
        var batchJson = JsonDocument.Parse(await batch.Content.ReadAsByteArrayAsync());
        var profile = Assert.Single(batchJson.RootElement.EnumerateArray());
        Assert.Equal("00112233445566778899aabbccddeeff", profile.GetProperty("id").GetString());
        Assert.Equal("OfflinePlayer", profile.GetProperty("name").GetString());
    }

    [Fact]
    public async Task RejectsInvalidSkinAndMalformedBatchRequest()
    {
        Directory.CreateDirectory(TempRoot);
        var invalidSkinPath = Path.Combine(TempRoot, "invalid.png");
        await File.WriteAllBytesAsync(invalidSkinPath, [1, 2, 3, 4]);
        using var service = new OfflineSkinYggdrasilService(new MinecraftSkinFileValidator());

        await Assert.ThrowsAsync<InvalidDataException>(() => service.PrepareAsync(
            CreateAccount(invalidSkinPath, MinecraftSkinModel.Classic),
            "00112233445566778899aabbccddeeff"));

        var nonPngSkinPath = Path.Combine(TempRoot, "skin.jpg");
        await File.WriteAllBytesAsync(nonPngSkinPath, CreateSkinJpeg());
        await Assert.ThrowsAsync<InvalidDataException>(() => service.PrepareAsync(
            CreateAccount(nonPngSkinPath, MinecraftSkinModel.Classic),
            "00112233445566778899aabbccddeeff"));

        var validSkinPath = Path.Combine(TempRoot, "valid.png");
        await File.WriteAllBytesAsync(validSkinPath, CreateSkinPng());
        var context = await service.PrepareAsync(
            CreateAccount(validSkinPath, MinecraftSkinModel.Classic),
            "00112233445566778899aabbccddeeff");
        using var client = new HttpClient { BaseAddress = new Uri(context!.AuthenticationServerUrl) };
        var malformed = await client.PostAsync(
            "/api/profiles/minecraft",
            new StringContent("{", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);

        var root = new Uri(context.AuthenticationServerUrl);
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(IPAddress.Loopback, root.Port);
        await using var stream = tcpClient.GetStream();
        var oversizedHeader = Encoding.ASCII.GetBytes(
            "POST /api/profiles/minecraft HTTP/1.1\r\n"
            + "Host: 127.0.0.1\r\n"
            + "Content-Length: 16385\r\n\r\n");
        await stream.WriteAsync(oversizedHeader);
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        Assert.StartsWith("HTTP/1.1 400", await reader.ReadLineAsync());
    }

    private static LauncherAccount CreateAccount(
        string skinPath,
        MinecraftSkinModel model)
    {
        var skin = new LauncherSkinRecord
        {
            Id = "skin",
            Source = new Uri(skinPath).AbsoluteUri,
            SkinModel = model,
            ContentHash = "hash"
        };
        return new LauncherAccount
        {
            Id = "offline",
            DisplayName = "OfflinePlayer",
            Uuid = "00112233-4455-6677-8899-aabbccddeeff",
            Kind = LauncherAccountKind.Offline,
            SkinSource = skin.Source,
            SkinModel = model,
            SkinLibrary = [skin],
            ActiveSkinId = skin.Id
        };
    }

    private static byte[] CreateSkinPng()
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(CreateSkinBitmap()));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static byte[] CreateSkinJpeg()
    {
        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(CreateSkinBitmap()));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static BitmapSource CreateSkinBitmap()
    {
        const int size = 64;
        var pixels = new byte[size * size * 4];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = 0x33;
            pixels[index + 1] = 0x77;
            pixels[index + 2] = 0xBB;
            pixels[index + 3] = byte.MaxValue;
        }
        return BitmapSource.Create(
            size,
            size,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            size * 4);
    }
}
