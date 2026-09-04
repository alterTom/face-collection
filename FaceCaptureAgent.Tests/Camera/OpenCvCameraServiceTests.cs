using FaceCaptureAgent.Camera;
using Xunit;

namespace FaceCaptureAgent.Tests.Camera;

public sealed class OpenCvCameraServiceTests
{
    [Fact]
    public void CandidateIndices_DefaultFirstThenRemainingZeroThroughNine()
    {
        Assert.Equal(
            [2, 0, 1, 3, 4, 5, 6, 7, 8, 9],
            OpenCvCameraService.CandidateIndices(2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void ValidateQuality_RejectsOutOfRange(int quality)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OpenCvCameraService.ValidateQuality(quality));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(85)]
    [InlineData(100)]
    public void ValidateQuality_AcceptsValidQuality(int quality)
    {
        OpenCvCameraService.ValidateQuality(quality);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("9", 9)]
    public void ParseDeviceIndex_AcceptsProbeRange(string id, int expected)
    {
        Assert.Equal(expected, OpenCvCameraService.ParseDeviceIndex(id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("10")]
    [InlineData("camera-zero")]
    public void ParseDeviceIndex_RejectsUnsupportedId(string id)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OpenCvCameraService.ParseDeviceIndex(id));
    }
}
