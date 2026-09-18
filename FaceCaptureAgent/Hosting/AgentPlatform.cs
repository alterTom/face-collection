using System.Runtime.InteropServices;

namespace FaceCaptureAgent.Hosting;

public static class AgentPlatform
{
    public static string OperatingSystemName => OperatingSystem.IsWindows() ? "windows"
        : OperatingSystem.IsLinux() ? "linux" : "unknown";
    public static string Architecture => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
    public static string RuntimeId => $"{(OperatingSystem.IsWindows() ? "win" : OperatingSystemName)}-{Architecture}";
}
