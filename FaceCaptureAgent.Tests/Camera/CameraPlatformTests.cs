using FaceCaptureAgent.Camera;
using OpenCvSharp;
using Xunit;

namespace FaceCaptureAgent.Tests.Camera;

public sealed class CameraPlatformTests
{
    [Fact]
    public void LinuxIndices_PreservesNodeNumbersAndPrioritizesExistingDefault()
    {
        Assert.Equal([42, 0, 12], CameraPlatform.LinuxIndices(
            ["video12", "video0", "video42", "video12", "video-1", "video01", "video2147483648", "video", "audio0"], 42));
        Assert.Equal([12, 42], CameraPlatform.LinuxIndices(["video42", "video12"], 0));
    }

    [Fact]
    public void Backends_AreExclusiveToPlatform()
    {
        Assert.Equal([VideoCaptureAPIs.V4L2], CameraPlatform.Backends(false, true));
        Assert.Equal([VideoCaptureAPIs.MSMF, VideoCaptureAPIs.DSHOW], CameraPlatform.Backends(true, false));
        Assert.Throws<PlatformNotSupportedException>(() => CameraPlatform.Backends(false, false));
    }

    [Fact]
    public void LinuxName_UsesSysfsAndFallsBackForMissingOrControlCharacters()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "video42"));
            File.WriteAllText(Path.Combine(root, "video42", "name"), "USB Camera\n");
            Assert.Equal("USB Camera", CameraPlatform.LinuxName(42, root));
            Assert.Equal("Camera 12", CameraPlatform.LinuxName(12, root));
            File.WriteAllText(Path.Combine(root, "video42", "name"), "Camera\u001b[31m");
            Assert.Equal("Camera 42", CameraPlatform.LinuxName(42, root));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
