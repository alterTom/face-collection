using System.Text.Json;
using FaceCaptureAgent.AutoCapture;
using FaceCaptureAgent.Protocol;
using OpenCvSharp;
using Xunit;

namespace FaceCaptureAgent.Tests.Sessions;

public sealed class BlinkTests
{
    private static readonly FaceBox Face = new(100, 100, 100, 100);
    private static FaceBox Eyes(EyeState state) => Face with { Eyes = state };

    [Fact]
    public void SingleOpenSampleWithTransitions_IsNotAnOpenBaseline()
    {
        var gate = new BlinkCaptureTracker(500);
        gate.Update([Eyes(EyeState.Open)], 0);
        gate.Update([Eyes(EyeState.Transition)], 50);
        gate.Update([Eyes(EyeState.Transition)], 100);
        gate.Update([Eyes(EyeState.Closed)], 150);
        for (var t = 250; t <= 1500; t += 100) Assert.False(gate.Update([Eyes(EyeState.Open)], t).Ready);
    }

    [Fact]
    public void BriefTransitionFrames_DoNotEraseBlink_ButCannotPassAlone()
    {
        var gate = new BlinkCaptureTracker(500);
        gate.Update([Eyes(EyeState.Open)], 0);
        gate.Update([Eyes(EyeState.Open)], 100);
        gate.Update([Eyes(EyeState.Transition)], 150);
        gate.Update([Eyes(EyeState.Closed)], 200);
        gate.Update([Eyes(EyeState.Transition)], 250);
        gate.Update([Eyes(EyeState.Open)], 300);
        Assert.Equal("blink-passed", gate.Update([Eyes(EyeState.Open)], 400).Status);
        var uncertain = new BlinkCaptureTracker(500);
        uncertain.Update([Eyes(EyeState.Open)], 0);
        for (var t = 100; t <= 900; t += 100) Assert.False(uncertain.Update([Eyes(EyeState.Transition)], t).Ready);
        for (var t = 1000; t <= 2000; t += 100) Assert.False(uncertain.Update([Eyes(EyeState.Open)], t).Ready);
    }

    [Fact]
    public void CompleteBlink_ThenStableOpenEyes_Captures()
    {
        var gate = new BlinkCaptureTracker(500, 10000);
        Assert.False(gate.Update([Eyes(EyeState.Open)], 0).Ready);
        Assert.False(gate.Update([Eyes(EyeState.Open)], 100).Ready);
        Assert.False(gate.Update([Eyes(EyeState.Closed)], 200).Ready);
        Assert.False(gate.Update([Eyes(EyeState.Open)], 300).Ready);
        Assert.False(gate.Update([Eyes(EyeState.Open)], 400).Ready);
        Assert.False(gate.Update([Eyes(EyeState.Open)], 600).Ready);
        Assert.False(gate.Update([Eyes(EyeState.Open)], 800).Ready);
        Assert.True(gate.Update([Eyes(EyeState.Open)], 900).Ready);
        Assert.False(gate.Update([Eyes(EyeState.Open)], 1000).Ready);
    }

    [Theory]
    [InlineData(EyeState.Open)]
    [InlineData(EyeState.Closed)]
    [InlineData(EyeState.Unknown)]
    public void StaticEyes_NeverPass(EyeState state)
    {
        var gate = new BlinkCaptureTracker(500, 10000);
        for (var t = 0; t <= 2000; t += 100) Assert.False(gate.Update([Eyes(state)], t).Ready);
    }

    [Fact]
    public void LossMultipleFacesMovementAndStaleFrames_RequireNewBlink()
    {
        foreach (var disturbance in new IReadOnlyList<FaceBox>[] { [], [Face, Face], [Eyes(EyeState.Open) with { X = 180 }], [Eyes(EyeState.Unknown)] })
        {
            var gate = Passed();
            Assert.False(gate.Update(disturbance, 500).Ready);
            for (var t = 600; t <= 1500; t += 100) Assert.False(gate.Update([Eyes(EyeState.Open)], t).Ready);
        }
        Assert.False(Passed().Update([Eyes(EyeState.Open)], 1200).Ready);
    }

    [Fact]
    public void LongClosure_AndMissingOpenBaseline_DoNotPass()
    {
        var gate = new BlinkCaptureTracker(500, 10000);
        gate.Update([Eyes(EyeState.Closed)], 0);
        for (var t = 100; t <= 1000; t += 100) Assert.False(gate.Update([Eyes(EyeState.Open)], t).Ready);
        for (var t = 1100; t <= 3000; t += 100) Assert.False(gate.Update([Eyes(EyeState.Closed)], t).Ready);
        for (var t = 3100; t <= 4000; t += 100) Assert.False(gate.Update([Eyes(EyeState.Open)], t).Ready);
    }

    [Fact]
    public void Timeout_IsTerminal_EvenWithoutFaces()
    {
        var gate = new BlinkCaptureTracker(500, 1000);
        gate.Update([], 0);
        Assert.Equal("blink-timeout", gate.Update([], 1000).Status);
        Assert.Equal("blink-timeout", gate.Update([Eyes(EyeState.Open)], 1100).Status);
    }

    [Fact]
    public void BundledEyeModel_RunsOnCpu()
    {
        using var detector = new EyeStateClassifier();
        using var crop = new Mat(320, 320, MatType.CV_8UC3, Scalar.All(127));
        Assert.Equal(EyeState.Unknown, detector.Classify(crop, new(50,50,200,200), new(110,130), new(210,130)));
    }

    [Theory]
    [InlineData(.1797, .1991, EyeState.Open)]
    [InlineData(.1717, .1867, EyeState.Open)]
    [InlineData(.08, .09, EyeState.Closed)]
    [InlineData(.1205, .1162, EyeState.Closed)]
    [InlineData(.13, .13, EyeState.Closed)]
    [InlineData(.1301, .12, EyeState.Transition)]
    [InlineData(.1461, .1497, EyeState.Transition)]
    [InlineData(.18, .08, EyeState.Transition)]
    [InlineData(.12, .14, EyeState.Transition)]
    [InlineData(double.NaN, .2, EyeState.Unknown)]
    public void EyelidGeometry_SeparatesNarrowOpenClosedAndTransition(double first, double second, EyeState expected)
    {
        Assert.Equal(expected, EyeStateClassifier.FromAspectRatios(first,second));
    }

    [Fact]
    public void MeasuredBlinkGeometry_CompletesOnlyAfterReopeningAndStability()
    {
        var gate = new BlinkCaptureTracker(1500);
        StabilityResult Sample(double a, double b, int t) =>
            gate.Update([Eyes(EyeStateClassifier.FromAspectRatios(a, b))], t);
        for (var t = 0; t <= 300; t += 50) Assert.False(Sample(.20, .20, t).Ready);
        Assert.False(Sample(.1247, .1213, 350).Ready);
        Assert.False(Sample(.1205, .1162, 400).Ready);
        Assert.False(Sample(.1201, .1160, 450).Ready);
        Assert.False(Sample(.1704, .1628, 500).Ready);
        Assert.Equal("blink-passed", Sample(.20, .20, 600).Status);
        for (var t = 700; t < 2100; t += 100) Assert.False(Sample(.20, .20, t).Ready);
        Assert.True(Sample(.20, .20, 2100).Ready);
    }

    [Fact]
    public void PassedBlink_StartsStabilityFromCurrentFacePosition()
    {
        var gate = new BlinkCaptureTracker(500);
        gate.Update([Eyes(EyeState.Open)], 0);
        gate.Update([Eyes(EyeState.Open) with { Y = 118 }], 100);
        gate.Update([Eyes(EyeState.Closed) with { Y = 118 }], 200);
        gate.Update([Eyes(EyeState.Open) with { Y = 118 }], 300);
        Assert.Equal("blink-passed", gate.Update([Eyes(EyeState.Open) with { Y = 118 }], 400).Status);
        for (var t = 500; t < 900; t += 100)
            Assert.False(gate.Update([Eyes(EyeState.Open) with { Y = 122 }], t).Ready);
        Assert.True(gate.Update([Eyes(EyeState.Open) with { Y = 122 }], 900).Ready);
    }

    [Fact]
    public void PartialClosureGeometry_DoesNotCountAsBlink()
    {
        var gate = new BlinkCaptureTracker(500);
        for (var t = 0; t < 2000; t += 50)
        {
            var state = t is >= 300 and <= 450
                ? EyeStateClassifier.FromAspectRatios(.1461, .1497)
                : EyeStateClassifier.FromAspectRatios(.20, .20);
            Assert.False(gate.Update([Eyes(state)], t).Ready);
        }
    }

    private static BlinkCaptureTracker Passed()
    {
        var gate = new BlinkCaptureTracker(500, 10000);
        gate.Update([Eyes(EyeState.Open)], 0);
        gate.Update([Eyes(EyeState.Open)], 100);
        gate.Update([Eyes(EyeState.Closed)], 200);
        gate.Update([Eyes(EyeState.Open)], 300);
        gate.Update([Eyes(EyeState.Open)], 400);
        return gate;
    }

    private static FaceBox Measured(double first, double second) => Face with
    {
        Eyes = EyeStateClassifier.FromAspectRatios(first, second),
        EyeMeasurement = new(first, second)
    };

    [Theory]
    [InlineData(EyeState.Transition)]
    [InlineData(EyeState.Closed)]
    public void AfterPassing_StableFaceWaitsForOpenFrameWithoutRepeatingBlink(EyeState state)
    {
        var gate = Passed();
        for (var t = 500; t <= 1100; t += 100)
            Assert.False(gate.Update([Eyes(state)], t).Ready);
        Assert.True(gate.Update([Eyes(EyeState.Open)], 1200).Ready);
    }

    [Fact]
    public void AfterPassing_TransitionDoesNotHideAFrameGap()
    {
        var gate = Passed();
        gate.Update([Eyes(EyeState.Transition)], 500);
        gate.Update([Eyes(EyeState.Transition)], 900);
        for (var t = 1000; t <= 2000; t += 100)
            Assert.False(gate.Update([Eyes(EyeState.Open)], t).Ready);
    }

    [Fact]
    public void RelativeClosure_UsesThisRoundsOpenBaseline()
    {
        var gate = new BlinkCaptureTracker(500);
        for (var t = 0; t <= 500; t += 50) gate.Update([Measured(.232, .228)], t);
        gate.Update([Measured(.173, .161)], 550);
        gate.Update([Measured(.1532, .1435)], 600);
        gate.Update([Measured(.1553, .1455)], 650);
        gate.Update([Measured(.1778, .1744)], 700);
        Assert.Equal("blink-passed", gate.Update([Measured(.178, .174)], 800).Status);
        for (var t = 900; t < 1300; t += 100) Assert.False(gate.Update([Measured(.18, .18)], t).Ready);
        Assert.True(gate.Update([Measured(.18, .18)], 1300).Ready);
    }

    [Fact]
    public void FiveFpsBlink_WithIntermediateFrame_IsNotAStaleFrameGap()
    {
        var gate = new BlinkCaptureTracker(500);
        gate.Update([Measured(.23, .23)], 0);
        gate.Update([Measured(.23, .23)], 200);
        gate.Update([Measured(.23, .23)], 400);
        gate.Update([Measured(.153, .143)], 600);
        gate.Update([Measured(.162, .151)], 800);
        gate.Update([Measured(.18, .18)], 1000);
        Assert.Equal("blink-passed", gate.Update([Measured(.18, .18)], 1200).Status);
        gate.Update([Measured(.18, .18)], 1400);
        gate.Update([Measured(.18, .18)], 1600);
        Assert.True(gate.Update([Measured(.18, .18)], 1800).Ready);
    }

    [Fact]
    public void RelativeClosure_RejectsSmallDropWinkAndStaticNarrowEyes()
    {
        foreach (var blink in new[] { new EyeMeasurement(.153, .143), new EyeMeasurement(.12, .19) })
        {
            var gate = new BlinkCaptureTracker(500);
            for (var t = 0; t <= 500; t += 50) gate.Update([Measured(.18, .18)], t);
            for (var t = 550; t <= 700; t += 50) Assert.False(gate.Update([Measured(blink.First, blink.Second)], t).Ready);
            for (var t = 750; t <= 2000; t += 50) Assert.False(gate.Update([Measured(.18, .18)], t).Ready);
        }
        var staticGate = new BlinkCaptureTracker(500);
        for (var t = 0; t <= 2000; t += 50) Assert.False(staticGate.Update([Measured(.153, .143)], t).Ready);
    }

    [Fact]
    public void RelativeClosure_CalibrationCannotSurviveFaceLoss()
    {
        var gate = new BlinkCaptureTracker(500);
        for (var t = 0; t <= 500; t += 50) gate.Update([Measured(.23, .23)], t);
        gate.Update([], 550);
        for (var t = 600; t <= 900; t += 50) gate.Update([Measured(.18, .18)], t);
        gate.Update([Measured(.15, .14)], 950);
        gate.Update([Measured(.15, .14)], 1000);
        for (var t = 1050; t <= 2000; t += 50) Assert.False(gate.Update([Measured(.18, .18)], t).Ready);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RelativeClosure_IgnoresSingleHighOutlierAndExpiredBaseline(bool expired)
    {
        var gate = new BlinkCaptureTracker(500);
        for (var t = 0; t <= 500; t += 50)
            gate.Update([Measured(expired ? .23 : .18, expired ? .23 : .18)], t);
        gate.Update([Measured(expired ? .18 : .50, expired ? .18 : .50)], 550);
        for (var t = 600; t <= 2800; t += 50) gate.Update([Measured(.18, .18)], t);
        if (!expired) gate.Update([Measured(.50, .50)], 2850);
        gate.Update([Measured(.153, .143)], 2900);
        gate.Update([Measured(.153, .143)], 2950);
        for (var t = 3000; t <= 4000; t += 50)
            Assert.False(gate.Update([Measured(.18, .18)], t).Ready);
    }
}
