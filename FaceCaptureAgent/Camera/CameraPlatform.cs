using System.Globalization;
using OpenCvSharp;

namespace FaceCaptureAgent.Camera;

/// <summary>Platform policy; device IDs always retain the actual OS camera index.</summary>
public static class CameraPlatform
{
    public static IReadOnlyList<VideoCaptureAPIs> Backends(bool windows, bool linux) =>
        windows ? [VideoCaptureAPIs.MSMF, VideoCaptureAPIs.DSHOW] :
        linux ? [VideoCaptureAPIs.V4L2] :
        throw new PlatformNotSupportedException("Camera capture is supported on Windows and Linux.");

    public static IReadOnlyList<int> LinuxIndices(IEnumerable<string> names, int defaultIndex)
    {
        var indices = new SortedSet<int>();
        foreach (var name in names)
        {
            if (name.StartsWith("video", StringComparison.Ordinal)
                && int.TryParse(name.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                && index >= 0 && name == $"video{index.ToString(CultureInfo.InvariantCulture)}")
            {
                indices.Add(index);
            }
        }

        var result = indices.ToList();
        if (result.Remove(defaultIndex)) result.Insert(0, defaultIndex);
        return result;
    }

    public static string LinuxName(int index, string sysRoot = "/sys/class/video4linux")
    {
        var fallback = $"Camera {index}";
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        try
        {
            var name = File.ReadAllText(Path.Combine(sysRoot, $"video{index.ToString(CultureInfo.InvariantCulture)}", "name")).Trim();
            return name.Length is > 0 and <= 256 && !name.Any(char.IsControl) ? name : fallback;
        }
        catch (IOException) { return fallback; }
        catch (UnauthorizedAccessException) { return fallback; }
    }

    public static IReadOnlyList<int> Candidates(int defaultIndex) => OperatingSystem.IsLinux()
        ? LinuxIndices(Directory.EnumerateFileSystemEntries("/dev", "video*").Select(path => Path.GetFileName(path)), defaultIndex)
        : OperatingSystem.IsWindows() ? OpenCvCameraService.CandidateIndices(defaultIndex)
        : throw new PlatformNotSupportedException("Camera capture is supported on Windows and Linux.");
}
