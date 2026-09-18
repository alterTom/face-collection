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
    [InlineData("42", 42)]
    [InlineData("2147483647", int.MaxValue)]
    public void ParseDeviceIndex_AcceptsNonnegativeInteger(string id, int expected)
    {
        Assert.Equal(expected, OpenCvCameraService.ParseDeviceIndex(id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData(" 1")]
    [InlineData("/dev/video0")]
    [InlineData("2147483648")]
    [InlineData("camera-zero")]
    public void ParseDeviceIndex_RejectsUnsupportedId(string id)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OpenCvCameraService.ParseDeviceIndex(id));
    }
}
