using System.Collections.Concurrent;
using System.Text.Json;
using FaceCaptureAgent.Camera;
using FaceCaptureAgent.Configuration;
using FaceCaptureAgent.Protocol;
using FaceCaptureAgent.Sessions;
using FaceCaptureAgent.Tests.Camera;
using Xunit;
using FaceCaptureAgent.AutoCapture;

namespace FaceCaptureAgent.Tests.Sessions;

public sealed class AutoCaptureTests
{
    [Fact]
    public async Task Auto_CapturesOnceWithoutPreview_RearmCreatesNewRound_ManualStops()
    {
        var camera = new FakeCameraService();
        var events = new ConcurrentQueue<JsonElement>();
        await using var session = new CaptureSession(Guid.NewGuid(), camera, new(), new(), (_, _) => Task.CompletedTask,
            (bytes, _) => { events.Enqueue(JsonDocument.Parse(bytes).RootElement.Clone()); return Task.CompletedTask; }, () => new Detector());
        var opened = await Send(session, "camera.open", "\"captureMode\":\"auto\",\"stableDurationMs\":500");
        session.StartPendingAutoCapture();
        await WaitFor(() => events.Count(e => e.GetProperty("type").GetString() == "auto.capture") == 1);
        await Task.Delay(700, TestContext.Current.CancellationToken);
        Assert.Single(events, e => e.GetProperty("type").GetString() == "auto.capture");
        var rearmed = await Send(session, "capture.rearm");
        Assert.NotEqual(opened.GetProperty("data").GetProperty("roundId").GetString(), rearmed.GetProperty("data").GetProperty("roundId").GetString());
        session.StartPendingAutoCapture();
        await WaitFor(() => events.Count(e => e.GetProperty("type").GetString() == "auto.capture") == 2);
        await Send(session, "camera.setCaptureMode", "\"captureMode\":\"manual\"");
        session.StartPendingAutoCapture();
        var count = events.Count;
        await Task.Delay(700, TestContext.Current.CancellationToken);
        Assert.Equal(count, events.Count);
        Assert.Equal("capture.result", (await Send(session, "capture")).GetProperty("type").GetString());
    }

    [Fact]
    public async Task DetectionFailure_EmitsSafeError_ManualStillWorks()
    {
        var events = new ConcurrentQueue<JsonElement>();
        await using var session = new CaptureSession(Guid.NewGuid(), new FakeCameraService(), new(), new(), (_, _) => Task.CompletedTask,
            (bytes, _) => { events.Enqueue(JsonDocument.Parse(bytes).RootElement.Clone()); return Task.CompletedTask; },
            () => new Detector { Failure = new Exception("private path") });
        await Send(session, "camera.open", "\"captureMode\":\"auto\"");
        session.StartPendingAutoCapture();
        await WaitFor(() => events.Any(e => e.GetProperty("type").GetString() == "auto.error"));
        var error = events.Last().GetProperty("data");
        Assert.Equal("FACE_DETECTION_FAILED", error.GetProperty("code").GetString());
        Assert.False(error.GetProperty("cameraClosed").GetBoolean());
        Assert.DoesNotContain("private path", error.ToString());
        await Send(session, "camera.setCaptureMode", "\"captureMode\":\"manual\"");
        Assert.Equal("capture.result", (await Send(session, "capture")).GetProperty("type").GetString());
    }

    [Fact]
    public async Task AutoCameraFailure_ReleasesLease_AndCloseCancelsPendingRound()
    {
        var leases = new CameraLeaseManager();
        var camera = new FakeCameraService { ReadException = new CameraException(ErrorCodes.CameraDisconnected, "unplugged", true) };
        var events = new ConcurrentQueue<JsonElement>();
        await using var session = new CaptureSession(Guid.NewGuid(), camera, leases, new(), (_, _) => Task.CompletedTask,
            (bytes, _) => { events.Enqueue(JsonDocument.Parse(bytes).RootElement.Clone()); return Task.CompletedTask; }, () => new Detector());
        await Send(session, "camera.open", "\"captureMode\":\"auto\"");
        session.StartPendingAutoCapture();
        await WaitFor(() => events.Any(e => e.GetProperty("type").GetString() == "auto.error"));
        Assert.True(events.Last().GetProperty("data").GetProperty("cameraClosed").GetBoolean());
        Assert.Equal(SessionState.Connected, session.State);
        using (var lease = leases.TryAcquire(Guid.NewGuid())) Assert.NotNull(lease);
        camera.ReadException = null;
        await Send(session, "camera.open", "\"captureMode\":\"auto\"");
        await Send(session, "camera.close");
        session.StartPendingAutoCapture();
        var count = events.Count;
        await Task.Delay(700, TestContext.Current.CancellationToken);
        Assert.Equal(count, events.Count);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(20, timeout.Token);
    }

    [Theory]
    [InlineData("camera.close")]
    [InlineData("camera.setCaptureMode")]
    [InlineData("capture.rearm")]
    [InlineData("dispose")]
    public async Task Control_CancelsBlockedDetection(string command)
    {
        var detector = new BlockingDetector();
        var session = new CaptureSession(Guid.NewGuid(), new FakeCameraService(), new(), new(), (_, _) => Task.CompletedTask,
            detectorFactory: () => detector);
        await Send(session, "camera.open", "\"captureMode\":\"auto\"");
        session.StartPendingAutoCapture();
        await detector.Started.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        var control = command == "dispose" ? session.DisposeAsync().AsTask()
            : Send(session, command, command == "camera.setCaptureMode" ? "\"captureMode\":\"manual\"" : "");
        try { await control.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken); }
        finally
        {
            detector.Release.TrySetResult();
            await control;
            await session.DisposeAsync();
        }
        Assert.True(detector.Cancelled);
    }

    [Fact]
    public async Task CameraFailure_CloseThrows_StillNotifiesAndReleases()
    {
        var camera = new FakeCameraService { ReadException = new CameraException(ErrorCodes.CameraDisconnected, "failed"), BeforeClose = () => throw new Exception("close failed") };
        var events = new ConcurrentQueue<JsonElement>();
        var leases = new CameraLeaseManager();
        await using var session = new CaptureSession(Guid.NewGuid(), camera, leases, new(), (_, _) => Task.CompletedTask,
            (bytes, _) => { events.Enqueue(JsonDocument.Parse(bytes).RootElement.Clone()); return Task.CompletedTask; }, () => new Detector());
        await Send(session, "camera.open", "\"captureMode\":\"auto\"");
        session.StartPendingAutoCapture();
        await WaitFor(() => events.Any(e => e.GetProperty("type").GetString() == "auto.error"));
        Assert.Equal(SessionState.Connected, session.State);
        using var lease = leases.TryAcquire(Guid.NewGuid());
        Assert.NotNull(lease);
    }

    [Fact]
    public async Task PreviewFailure_AfterAutoCapture_NotifiesAndReleases()
    {
        var camera = new FakeCameraService();
        var events = new ConcurrentQueue<JsonElement>();
        var leases = new CameraLeaseManager();
        await using var session = new CaptureSession(Guid.NewGuid(), camera, leases, new(), (_, _) => Task.CompletedTask,
            (bytes, _) => { events.Enqueue(JsonDocument.Parse(bytes).RootElement.Clone()); return Task.CompletedTask; }, () => new Detector());
        await Send(session, "camera.open", "\"captureMode\":\"auto\",\"stableDurationMs\":500");
        session.StartPendingAutoCapture();
        await Send(session, "preview.start");
        await WaitFor(() => events.Any(e => e.GetProperty("type").GetString() == "auto.capture"));
        camera.ReadException = new CameraException(ErrorCodes.CameraDisconnected, "unplugged");
        await WaitFor(() => events.Any(e => e.GetProperty("type").GetString() == "auto.error"));
        Assert.Equal(SessionState.Connected, session.State);
        using var lease = leases.TryAcquire(Guid.NewGuid());
        Assert.NotNull(lease);
    }

    private sealed class BlockingDetector : IFaceDetector
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Cancelled { get; private set; }
        public async Task<IReadOnlyList<FaceBox>> DetectAsync(JpegFrame frame, CancellationToken token)
        {
            Started.TrySetResult();
            try { await Release.Task.WaitAsync(token); }
            catch (OperationCanceledException) { Cancelled = true; throw; }
            return [];
        }
        public void Dispose() { }
    }

    [Fact]
    public async Task Dispose_CancelsBlockedPreviewFailureNotification()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var camera = new FakeCameraService { ReadException = new CameraException(ErrorCodes.CameraDisconnected, "unplugged") };
        var session = new CaptureSession(Guid.NewGuid(), camera, new(), new(), (_, _) => Task.CompletedTask,
            async (_, token) => { entered.TrySetResult(); await release.Task.WaitAsync(token); });
        await Send(session, "camera.open");
        await Send(session, "preview.start");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        var disposing = session.DisposeAsync().AsTask();
        try { await disposing.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken); }
        finally { release.TrySetResult(); await disposing; }
        Assert.Equal(SessionState.Closed, session.State);
    }

    private sealed class Detector : IFaceDetector
    {
        public Exception? Failure { get; init; }
        public Task<IReadOnlyList<FaceBox>> DetectAsync(JpegFrame frame, CancellationToken token) =>
            Failure is null ? Task.FromResult<IReadOnlyList<FaceBox>>([new(100, 100, 100, 100)]) : Task.FromException<IReadOnlyList<FaceBox>>(Failure);
        public void Dispose() { }
    }
    [Theory]
    [InlineData("\"captureMode\":\"bad\"")]
    [InlineData("\"captureMode\":null")]
    [InlineData("\"stableDurationMs\":499")]
    [InlineData("\"stableDurationMs\":10001")]
    [InlineData("\"stableDurationMs\":1.5")]
    public async Task Open_InvalidAutoOptions_RejectsBeforeOpening(string fields)
    {
        var camera = new FakeCameraService();
        await using var session = new CaptureSession(Guid.NewGuid(), camera, new(), new(), (_, _) => Task.CompletedTask);
        await Assert.ThrowsAsync<ProtocolException>(() => Send(session, "camera.open", fields));
        Assert.Equal(0, camera.OpenCount);
    }

    internal static async Task<JsonElement> Send(CaptureSession session, string type, string fields = "")
    {
        var json = "{\"type\":\"" + type + "\",\"requestId\":\"test\"" + (fields.Length > 0 ? "," + fields : "") + "}";
        var result = await session.HandleAsync(ProtocolParser.Parse(System.Text.Encoding.UTF8.GetBytes(json)), CancellationToken.None);
        return JsonDocument.Parse(result).RootElement.Clone();
    }
}
