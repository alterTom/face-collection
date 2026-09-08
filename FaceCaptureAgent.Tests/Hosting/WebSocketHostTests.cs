using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FaceCaptureAgent.Camera;
using FaceCaptureAgent.Tests.Camera;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using FaceCaptureAgent.Desktop;
using FaceCaptureAgent.Diagnostics;

namespace FaceCaptureAgent.Tests.Hosting;

public sealed class WebSocketHostTests
{
    [Fact]
    public async Task AutoOpen_RespondsBeforeRealModelEvents_WithoutPreview()
    {
        using var blank = new OpenCvSharp.Mat(320, 320, OpenCvSharp.MatType.CV_8UC3, OpenCvSharp.Scalar.All(0));
        OpenCvSharp.Cv2.ImEncode(".jpg", blank, out var jpeg);
        await using var factory = CreateFactory(new FakeCameraService { NextFrame = new(jpeg, 320, 320, DateTimeOffset.Now) });
        using var socket = await ConnectAsync(factory);
        await SendAsync(socket, """{"type":"camera.open","requestId":"auto-open","captureMode":"auto","stableDurationMs":500}""");
        var opened = await ReceiveJsonAsync(socket).WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Equal("camera.open.result", opened.GetProperty("type").GetString());
        var status = await ReceiveJsonAsync(socket).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(status.GetProperty("event").GetBoolean());
        Assert.False(status.TryGetProperty("requestId", out _));
        Assert.Equal("auto.status", status.GetProperty("type").GetString());
        Assert.Equal("no-face", status.GetProperty("data").GetProperty("status").GetString());
        Assert.Equal(opened.GetProperty("data").GetProperty("roundId").GetString(), status.GetProperty("data").GetProperty("roundId").GetString());
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", TestContext.Current.CancellationToken);
    }
    [Fact]
    public async Task CaptureFlow_LogsSuccessFailureAndDisconnectWithoutPhotoData()
    {
        await using var factory = CreateFactory(new FakeCameraService());
        using var socket = await ConnectAsync(factory);
        var log = factory.Services.GetRequiredService<ActivityLog>();
        await SendAsync(socket, """{"type":"capture","requestId":"too-early"}""");
        await ReceiveJsonAsync(socket);
        await SendAsync(socket, """{"type":"camera.open","requestId":"open"}""");
        await ReceiveJsonAsync(socket);
        await SendAsync(socket, """{"type":"capture","requestId":"photo"}""");
        var photo = await ReceiveJsonAsync(socket);
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", TestContext.Current.CancellationToken);

        var history = string.Join("\n", log.Snapshot().Select(entry => entry.Text));
        Assert.Contains("客户端已连接", history);
        Assert.Contains("抓拍照片失败：INVALID_STATE", history);
        Assert.Contains("打开摄像头成功", history);
        Assert.Contains("抓拍照片成功", history);
        Assert.Contains("客户端已断开连接", history);
        Assert.DoesNotContain(photo.GetProperty("data").GetProperty("base64").GetString()!, history);
    }

    [Fact]
    public async Task ApplicationStopping_WithOpenCamera_ReleasesCamera()
    {
        var camera = new FakeCameraService();
        await using var factory = CreateFactory(camera);
        using var socket = await ConnectAsync(factory);
        await SendAsync(socket, """{"type":"camera.open","requestId":"open"}""");
        await ReceiveJsonAsync(socket);

        factory.Services.GetRequiredService<Microsoft.Extensions.Hosting.IHostApplicationLifetime>().StopApplication();

        await camera.Closed.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.False(camera.IsOpen);
        Assert.Equal(1, camera.CloseCount);
    }

    [Fact]
    public async Task NormalHttpGetToFace_ReturnsBadRequest()
    {
        await using var factory = CreateFactory(new FakeCameraService());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/face", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task WebSocketWithoutRequiredSubprotocol_IsRejected()
    {
        await using var factory = CreateFactory(new FakeCameraService());
        _ = factory.CreateClient();
        var client = factory.Server.CreateWebSocketClient();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            client.ConnectAsync(new Uri("ws://localhost/face"), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("http://one.example")]
    [InlineData("http://two.example")]
    public async Task WebSocketsFromArbitraryOrigins_AreAccepted(string origin)
    {
        await using var factory = CreateFactory(new FakeCameraService());
        using var socket = await ConnectAsync(factory, origin);

        await SendAsync(socket, """{"type":"ping","requestId":"origin"}""");
        var response = await ReceiveJsonAsync(socket);

        Assert.Equal("pong", response.GetProperty("type").GetString());
    }

    [Fact]
    public async Task SystemInfo_PreservesRequestId()
    {
        await using var factory = CreateFactory(new FakeCameraService());
        using var socket = await ConnectAsync(factory);

        await SendAsync(socket, """{"type":"system.info","requestId":"system-42"}""");
        var response = await ReceiveJsonAsync(socket);

        Assert.Equal("system.info.result", response.GetProperty("type").GetString());
        Assert.Equal("system-42", response.GetProperty("requestId").GetString());
    }

    [Fact]
    public async Task Ping_ReturnsPongWithSameRequestId()
    {
        await using var factory = CreateFactory(new FakeCameraService());
        using var socket = await ConnectAsync(factory);

        await SendAsync(socket, """{"type":"ping","requestId":"ping-42"}""");
        var response = await ReceiveJsonAsync(socket);

        Assert.Equal("pong", response.GetProperty("type").GetString());
        Assert.Equal("ping-42", response.GetProperty("requestId").GetString());
    }

    [Fact]
    public async Task DisconnectAfterCameraOpen_ClosesCameraOnce()
    {
        var camera = new FakeCameraService();
        await using var factory = CreateFactory(camera);
        var socket = await ConnectAsync(factory);
        await SendAsync(socket, """{"type":"camera.open","requestId":"open"}""");
        Assert.Equal("camera.open.result", (await ReceiveJsonAsync(socket)).GetProperty("type").GetString());

        await socket.CloseAsync(
            WebSocketCloseStatus.NormalClosure,
            "test complete",
            TestContext.Current.CancellationToken);
        socket.Dispose();
        await camera.Closed.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, camera.CloseCount);
    }

    private static WebApplicationFactory<Program> CreateFactory(FakeCameraService camera) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                var tray = services.Single(service => service.ImplementationType == typeof(TrayService));
                services.Remove(tray);
                services.RemoveAll<ICameraService>();
                services.AddSingleton<ICameraService>(camera);
            }));

    private static async Task<WebSocket> ConnectAsync(
        WebApplicationFactory<Program> factory,
        string? origin = null)
    {
        _ = factory.CreateClient();
        var client = factory.Server.CreateWebSocketClient();
        client.SubProtocols.Add("face-capture.v1");
        if (origin is not null)
        {
            client.ConfigureRequest = request => request.Headers.Origin = origin;
        }

        return await client.ConnectAsync(
            new Uri("ws://localhost/face"),
            TestContext.Current.CancellationToken);
    }

    private static Task SendAsync(WebSocket socket, string json) =>
        socket.SendAsync(
            Encoding.UTF8.GetBytes(json),
            WebSocketMessageType.Text,
            true,
            TestContext.Current.CancellationToken);

    private static async Task<JsonElement> ReceiveJsonAsync(WebSocket socket)
    {
        using var stream = new MemoryStream();
        var buffer = new byte[4096];
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, TestContext.Current.CancellationToken);
            Assert.Equal(WebSocketMessageType.Text, result.MessageType);
            stream.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        return JsonDocument.Parse(stream.ToArray()).RootElement.Clone();
    }
}
