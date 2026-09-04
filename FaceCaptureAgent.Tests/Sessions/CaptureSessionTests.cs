using System.Text.Json;
using FaceCaptureAgent.Camera;
using FaceCaptureAgent.Configuration;
using FaceCaptureAgent.Protocol;
using FaceCaptureAgent.Sessions;
using FaceCaptureAgent.Tests.Camera;
using Xunit;

namespace FaceCaptureAgent.Tests.Sessions;

public sealed class CaptureSessionTests
{
    [Fact]
    public async Task CaptureBeforeOpen_ReturnsInvalidState()
    {
        await using var fixture = new SessionFixture();

        var response = await fixture.SendAsync("capture");

        AssertError(response, ErrorCodes.InvalidState);
    }

    [Fact]
    public async Task SecondSession_ReceivesCameraBusy_AndLeaseIsReleasedOnDisconnect()
    {
        var leases = new CameraLeaseManager();
        var firstCamera = new FakeCameraService();
        var secondCamera = new FakeCameraService();
        await using var first = SessionFixture.Create(firstCamera, leases);
        await using var second = SessionFixture.Create(secondCamera, leases);

        AssertResult(await first.SendAsync("camera.open"), "camera.open.result");
        AssertError(await second.SendAsync("camera.open"), ErrorCodes.CameraBusy);

        await first.DisposeSessionAsync();

        Assert.Equal(1, firstCamera.CloseCount);
        AssertResult(await second.SendAsync("camera.open"), "camera.open.result");
    }

    [Fact]
    public async Task DisposeAfterOpen_ClosesCameraAndReleasesLeaseExactlyOnce()
    {
        var leases = new CameraLeaseManager();
        var camera = new FakeCameraService();
        var fixture = SessionFixture.Create(camera, leases);
        AssertResult(await fixture.SendAsync("camera.open"), "camera.open.result");

        await fixture.DisposeSessionAsync();
        await fixture.DisposeSessionAsync();

        Assert.Equal(1, camera.CloseCount);
        using var releasedLease = leases.TryAcquire(Guid.NewGuid());
        Assert.NotNull(releasedLease);
    }

    [Fact]
    public async Task Capture_ReturnsRawBase64AndMetadata()
    {
        await using var fixture = new SessionFixture();
        await fixture.SendAsync("camera.open");

        var response = await fixture.SendAsync("capture");
        var data = response.GetProperty("data");

        Assert.Equal(Convert.ToBase64String(fixture.Camera.NextFrame.Bytes), data.GetProperty("base64").GetString());
        Assert.DoesNotContain("data:", data.GetProperty("base64").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("image/jpeg", data.GetProperty("mimeType").GetString());
        Assert.Equal(fixture.Camera.NextFrame.Bytes.Length, data.GetProperty("size").GetInt32());
        Assert.Equal(2, data.GetProperty("width").GetInt32());
        Assert.Equal(2, data.GetProperty("height").GetInt32());
    }

    [Fact]
    public async Task Capture_RejectsFrameOverConfiguredLimit()
    {
        await using var fixture = new SessionFixture(options: new AgentOptions { MaxImageBytes = 3 });
        await fixture.SendAsync("camera.open");

        var response = await fixture.SendAsync("capture");

        AssertError(response, ErrorCodes.ImageTooLarge);
    }

    [Fact]
    public async Task PreviewStop_CancelsPreviewWithoutClosingCamera()
    {
        var firstFrame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fixture = new SessionFixture(binarySender: (_, _) =>
        {
            firstFrame.TrySetResult();
            return Task.CompletedTask;
        });
        await fixture.SendAsync("camera.open");

        AssertResult(await fixture.SendAsync("preview.start"), "preview.start.result");
        await firstFrame.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        AssertResult(await fixture.SendAsync("preview.stop"), "preview.stop.result");

        Assert.Equal(0, fixture.Camera.CloseCount);
        Assert.Equal(SessionState.CameraOpen, fixture.Session.State);
    }

    [Fact]
    public async Task DisconnectAfterPreviewReadFailure_StillClosesCameraAndReleasesLease()
    {
        var leases = new CameraLeaseManager();
        var camera = new FakeCameraService
        {
            ReadException = new CameraException(
                ErrorCodes.CameraDisconnected,
                "camera disconnected",
                true)
        };
        var fixture = SessionFixture.Create(camera, leases);
        await fixture.SendAsync("camera.open");
        await fixture.SendAsync("preview.start");
        await camera.ReadAttempted.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        var disposeError = await Record.ExceptionAsync(async () => await fixture.DisposeSessionAsync());

        Assert.Null(disposeError);
        Assert.Equal(1, camera.CloseCount);
        using var releasedLease = leases.TryAcquire(Guid.NewGuid());
        Assert.NotNull(releasedLease);
    }

    [Fact]
    public async Task CameraClose_CompletesCleanupIfRequestIsCanceledDuringClose()
    {
        using var cancellation = new CancellationTokenSource();
        var camera = new FakeCameraService { BeforeClose = cancellation.Cancel };
        await using var fixture = SessionFixture.Create(camera, new CameraLeaseManager());
        await fixture.SendAsync("camera.open");

        var response = await fixture.SendWithCancellationAsync("camera.close", cancellation.Token);

        AssertResult(response, "camera.close.result");
        Assert.Equal(1, camera.CloseCount);
        Assert.Equal(SessionState.Connected, fixture.Session.State);
    }

    [Fact]
    public async Task Ping_ReturnsPongWithSameRequestId_FromEveryLiveState()
    {
        await using var fixture = new SessionFixture();

        AssertResult(await fixture.SendAsync("ping", "connected"), "pong", "connected");
        await fixture.SendAsync("camera.open");
        AssertResult(await fixture.SendAsync("ping", "open"), "pong", "open");
        await fixture.SendAsync("preview.start");
        AssertResult(await fixture.SendAsync("ping", "preview"), "pong", "preview");

        await fixture.DisposeSessionAsync();
        AssertError(await fixture.SendAsync("ping", "closed"), ErrorCodes.InvalidState, "closed");
    }

    private static void AssertResult(JsonElement response, string type, string? requestId = null)
    {
        Assert.Equal(type, response.GetProperty("type").GetString());
        if (requestId is not null)
        {
            Assert.Equal(requestId, response.GetProperty("requestId").GetString());
        }
    }

    private static void AssertError(JsonElement response, string code, string? requestId = null)
    {
        Assert.Equal("error", response.GetProperty("type").GetString());
        Assert.Equal(code, response.GetProperty("error").GetProperty("code").GetString());
        if (requestId is not null)
        {
            Assert.Equal(requestId, response.GetProperty("requestId").GetString());
        }
    }

    private sealed class SessionFixture : IAsyncDisposable
    {
        private bool _disposed;

        public SessionFixture(
            AgentOptions? options = null,
            Func<ReadOnlyMemory<byte>, CancellationToken, Task>? binarySender = null)
            : this(new FakeCameraService(), new CameraLeaseManager(), options, binarySender)
        {
        }

        private SessionFixture(
            FakeCameraService camera,
            CameraLeaseManager leases,
            AgentOptions? options = null,
            Func<ReadOnlyMemory<byte>, CancellationToken, Task>? binarySender = null)
        {
            Camera = camera;
            Session = new CaptureSession(
                Guid.NewGuid(),
                Camera,
                leases,
                options ?? new AgentOptions(),
                binarySender ?? ((_, _) => Task.CompletedTask));
        }

        public FakeCameraService Camera { get; }

        public CaptureSession Session { get; }

        public static SessionFixture Create(FakeCameraService camera, CameraLeaseManager leases) =>
            new(camera, leases);

        public Task<JsonElement> SendAsync(string type, string requestId = "r1") =>
            SendWithCancellationAsync(type, CancellationToken.None, requestId);

        public async Task<JsonElement> SendWithCancellationAsync(
            string type,
            CancellationToken cancellationToken,
            string requestId = "r1")
        {
            var message = ProtocolParser.Parse(
                System.Text.Encoding.UTF8.GetBytes($$"""{"type":"{{type}}","requestId":"{{requestId}}"}"""));
            var response = await Session.HandleAsync(message, cancellationToken);
            return JsonDocument.Parse(response).RootElement.Clone();
        }

        public async Task DisposeSessionAsync()
        {
            if (_disposed)
            {
                await Session.DisposeAsync();
                return;
            }

            _disposed = true;
            await Session.DisposeAsync();
        }

        public ValueTask DisposeAsync() => Session.DisposeAsync();
    }
}
