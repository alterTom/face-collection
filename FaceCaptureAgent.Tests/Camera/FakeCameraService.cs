using FaceCaptureAgent.Camera;

namespace FaceCaptureAgent.Tests.Camera;

internal sealed class FakeCameraService : ICameraService
{
    private int _openCount;
    private int _readCount;
    private int _closeCount;

    public IReadOnlyList<CameraDevice> Devices { get; set; } = [new("0", "Camera 0")];

    public CameraOpenResult OpenResult { get; set; } = new(1280, 720, 15);

    public JpegFrame NextFrame { get; set; } =
        new([0xff, 0xd8, 0xff, 0xd9], 2, 2, DateTimeOffset.Parse("2026-09-03T10:20:30+08:00"));

    public int OpenCount => Volatile.Read(ref _openCount);

    public int ReadCount => Volatile.Read(ref _readCount);

    public int CloseCount => Volatile.Read(ref _closeCount);

    public TaskCompletionSource Closed { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource ReadAttempted { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Exception? ReadException { get; set; }

    public Action? BeforeClose { get; set; }

    public bool IsOpen { get; private set; }

    public Task<IReadOnlyList<CameraDevice>> ListDevicesAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Devices);

    public Task<CameraOpenResult> OpenAsync(CameraOpenRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _openCount);
        IsOpen = true;
        return Task.FromResult(OpenResult);
    }

    public Task<JpegFrame> ReadJpegFrameAsync(int jpegQuality, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _readCount);
        ReadAttempted.TrySetResult();
        if (ReadException is not null)
        {
            throw ReadException;
        }

        return Task.FromResult(NextFrame);
    }

    public Task CloseAsync(CancellationToken cancellationToken)
    {
        BeforeClose?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _closeCount);
        IsOpen = false;
        Closed.TrySetResult();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
