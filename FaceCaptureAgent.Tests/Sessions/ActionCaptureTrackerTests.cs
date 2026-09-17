using FaceCaptureAgent.AutoCapture;
using Xunit;
using OpenCvSharp;

namespace FaceCaptureAgent.Tests.Sessions;

public sealed class ActionCaptureTrackerTests
{
    [Fact]
    public void Geometry_IsScaleTranslationAndRollInvariant_AndDistinguishesDirection()
    {
        Assert.Equal(.3, EyeStateClassifier.HeadTurn(new(10, 10), new(110, 10), new(90, 70)), 5);
        Assert.Equal(-.3, EyeStateClassifier.HeadTurn(new(10, 10), new(110, 10), new(30, 70)), 5);
        // 旋转、缩放并平移同一组点，鼻尖沿眼睛轴的偏移保持不变。
        Point2f Transform(Point2f p) => new(500 + 2 * (.8f * p.X - .6f * p.Y), 300 + 2 * (.6f * p.X + .8f * p.Y));
        Assert.Equal(.3, EyeStateClassifier.HeadTurn(Transform(new(10, 10)), Transform(new(110, 10)), Transform(new(90, 70))), 5);
        Assert.Equal(.4, EyeStateClassifier.MouthOpening(new(0, 0), new(100, 0), new(50, 0), new(50, 40)), 5);
        Assert.Equal(.4, EyeStateClassifier.MouthOpening(Transform(new(0, 0)), Transform(new(100, 0)), Transform(new(50, 0)), Transform(new(50, 40))), 5);
        Assert.True(double.IsNaN(EyeStateClassifier.HeadTurn(new(0, 0), new(0, 0), new(0, 0))));
        Assert.True(double.IsNaN(EyeStateClassifier.MouthOpening(new(float.NaN, 0), new(100, 0), new(50, 0), new(50, 40))));
    }

    [Theory]
    [InlineData("turn-left", .35)]
    [InlineData("turn-right", -.35)]
    public void Turns_DoNotDependOnMouthOrEyeMeasurements(string action, double turn)
    {
        var gate = new ActionCaptureTracker(action, 500);
        for (var t = 0; t <= 200; t += 100) gate.Update([Sample(double.NaN)], t);
        for (var t = 300; t <= 600; t += 100) gate.Update([Sample(double.NaN, turn)], t);
        for (var t = 700; t < 1400; t += 100) Assert.False(gate.Update([Sample(double.NaN)], t).Ready);
        Assert.True(gate.Update([Sample(double.NaN)], 1400).Ready);
    }

    private static FaceBox Sample(double mouth = .03, double turn = 0) =>
        new(100, 100, 100, 100) { MouthRatio = mouth, TurnRatio = turn };

    [Theory]
    [InlineData("mouth-open", .45, 0)]
    [InlineData("turn-left", .03, .35)]
    [InlineData("turn-right", .03, -.35)]
    public void SelectedAction_ReturnToNeutralThenStable_CapturesWithoutBlink(string action, double mouth, double turn)
    {
        var gate = new ActionCaptureTracker(action, 500);
        for (var t = 0; t <= 200; t += 100) Assert.False(gate.Update([Sample()], t).Ready);
        for (var t = 300; t <= 600; t += 100) Assert.False(gate.Update([Sample(mouth, turn)], t).Ready);
        for (var t = 700; t < 1400; t += 100) Assert.False(gate.Update([Sample()], t).Ready);
        Assert.True(gate.Update([Sample()], 1400).Ready);
        Assert.False(gate.Update([Sample()], 1500).Ready);
    }

    [Theory]
    [InlineData("mouth-open", .03, .35)]
    [InlineData("turn-left", .45, 0)]
    [InlineData("turn-left", .03, -.35)]
    [InlineData("turn-right", .03, .35)]
    public void WrongAction_NeverPasses(string action, double mouth, double turn)
    {
        var gate = new ActionCaptureTracker(action, 500);
        for (var t = 0; t <= 200; t += 100) gate.Update([Sample()], t);
        for (var t = 300; t <= 700; t += 100) Assert.False(gate.Update([Sample(mouth, turn)], t).Ready);
        for (var t = 800; t <= 2000; t += 100) Assert.False(gate.Update([Sample()], t).Ready);
    }

    [Fact]
    public void HeldActionWithoutBaseline_AndSingleSpike_DoNotPass()
    {
        var gate = new ActionCaptureTracker("mouth-open", 500);
        for (var t = 0; t <= 500; t += 100) gate.Update([Sample(.5)], t);
        for (var t = 600; t <= 1400; t += 100) Assert.False(gate.Update([Sample()], t).Ready);
        gate.Update([Sample(.5)], 1500);
        for (var t = 1600; t <= 2500; t += 100) Assert.False(gate.Update([Sample()], t).Ready);
    }

    [Fact]
    public void FaceLossMultipleFacesInvalidMeasurementsAndFrameGap_ResetProgress()
    {
        foreach (var disturbance in new IReadOnlyList<FaceBox>[] { [], [Sample(), Sample()], [Sample(double.NaN)], [Sample() with { X = 180 }] })
        {
            var gate = new ActionCaptureTracker("mouth-open", 500);
            for (var t = 0; t <= 200; t += 100) gate.Update([Sample()], t);
            for (var t = 300; t <= 600; t += 100) gate.Update([Sample(.5)], t);
            gate.Update(disturbance, 700);
            for (var t = 800; t <= 2200; t += 100) Assert.False(gate.Update([Sample()], t).Ready);
        }
        var stale = new ActionCaptureTracker("turn-left", 500);
        for (var t = 0; t <= 200; t += 100) stale.Update([Sample()], t);
        for (var t = 300; t <= 600; t += 100) stale.Update([Sample(turn: .35)], t);
        for (var t = 1000; t <= 2200; t += 100) Assert.False(stale.Update([Sample()], t).Ready);
    }

    [Fact]
    public void Timeout_IsTerminal()
    {
        var gate = new ActionCaptureTracker("mouth-open", 500, 1000);
        gate.Update([], 0);
        Assert.Equal("action-timeout", gate.Update([], 1000).Status);
        Assert.Equal("action-timeout", gate.Update([Sample()], 1100).Status);
    }
}
