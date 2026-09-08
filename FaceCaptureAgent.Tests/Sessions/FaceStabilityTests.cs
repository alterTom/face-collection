using FaceCaptureAgent.AutoCapture;
using Xunit;

namespace FaceCaptureAgent.Tests.Sessions;

public sealed class FaceStabilityTests
{
    private static readonly FaceBox Face = new(100, 100, 100, 100);
    [Fact]
    public void StableFace_FiresOnce_AfterRequiredDuration()
    {
        var tracker = new FaceStabilityTracker(1500);
        Assert.False(tracker.Update([Face], 0).Ready);
        Assert.False(tracker.Update([Face], 500).Ready);
        Assert.False(tracker.Update([Face], 1000).Ready);
        Assert.True(tracker.Update([Face], 1500).Ready);
        Assert.False(tracker.Update([Face], 2000).Ready);
    }

    [Fact]
    public void MissingMultipleMovementAndGaps_ResetStableWindow()
    {
        foreach (var disturbance in new IReadOnlyList<FaceBox>[] { [], [Face, Face], [Face with { X = 109 }], [Face with { Width = 111 }] })
        {
            var tracker = new FaceStabilityTracker(500);
            tracker.Update([Face], 0);
            Assert.False(tracker.Update(disturbance, 500).Ready);
            Assert.False(tracker.Update([Face], 600).Ready);
        }
        var gap = new FaceStabilityTracker(500);
        gap.Update([Face], 0);
        Assert.False(gap.Update([Face], 751).Ready);
    }

    [Fact]
    public void SlowDrift_ComparedWithWindowAnchor()
    {
        var tracker = new FaceStabilityTracker(1500);
        for (var i = 0; i < 10; i++)
            Assert.False(tracker.Update([Face with { X = 100 + i * 4 }], i * 500).Ready);
    }

    [Fact]
    public async Task BundledModel_LoadsAndDetectsBlankImage()
    {
        using var detector = new YuNetFaceDetector();
        using var blank = new OpenCvSharp.Mat(320, 320, OpenCvSharp.MatType.CV_8UC3, OpenCvSharp.Scalar.All(0));
        OpenCvSharp.Cv2.ImEncode(".jpg", blank, out var jpeg);
        var faces = await detector.DetectAsync(new(jpeg, 320, 320, DateTimeOffset.Now), CancellationToken.None);
        Assert.Empty(faces);
    }
}
