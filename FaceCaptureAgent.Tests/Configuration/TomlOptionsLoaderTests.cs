using FaceCaptureAgent.Configuration;
using Xunit;

namespace FaceCaptureAgent.Tests.Configuration;

public sealed class TomlOptionsLoaderTests : IDisposable
{
    private readonly List<string> _temporaryFiles = [];

    [Fact]
    public void Load_AcceptsLoopbackConfiguration()
    {
        var path = WriteTemp(ValidToml("127.0.0.1"));

        var options = TomlOptionsLoader.Load(path);

        Assert.Equal("127.0.0.1", options.ListenAddress);
        Assert.Equal(17653, options.ListenPort);
        Assert.Equal(0, options.CameraIndex);
        Assert.Equal(1280, options.CaptureWidth);
        Assert.Equal(720, options.CaptureHeight);
        Assert.Equal(5, options.PreviewFps);
        Assert.Equal(85, options.JpegQuality);
        Assert.Equal(2_097_152, options.MaxImageBytes);
        Assert.Equal(10, options.CameraTimeoutSeconds);
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("192.168.1.10")]
    [InlineData("::1")]
    public void Load_RejectsEveryNonIpv4LoopbackAddress(string address)
    {
        var path = WriteTemp(ValidToml(address));

        var error = Assert.Throws<InvalidOperationException>(() => TomlOptionsLoader.Load(path));

        Assert.Contains("127.0.0.1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_RejectsMissingFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"face-capture-missing-{Guid.NewGuid():N}.toml");

        Assert.Throws<FileNotFoundException>(() => TomlOptionsLoader.Load(path));
    }

    [Theory]
    [InlineData("listen_port", "80")]
    [InlineData("camera_index", "-1")]
    [InlineData("preview_fps", "0")]
    [InlineData("preview_fps", "16")]
    [InlineData("jpeg_quality", "101")]
    [InlineData("max_image_bytes", "0")]
    [InlineData("camera_timeout_seconds", "0")]
    public void Load_RejectsInvalidNumericSetting(string key, string invalidValue)
    {
        var toml = ValidToml("127.0.0.1");
        var path = WriteTemp(ReplaceSetting(toml, key, invalidValue));

        Assert.Throws<InvalidOperationException>(() => TomlOptionsLoader.Load(path));
    }

    public void Dispose()
    {
        foreach (var path in _temporaryFiles)
        {
            File.Delete(path);
        }
    }

    private string WriteTemp(string contents)
    {
        var path = Path.Combine(Path.GetTempPath(), $"face-capture-{Guid.NewGuid():N}.toml");
        File.WriteAllText(path, contents);
        _temporaryFiles.Add(path);
        return path;
    }

    private static string ReplaceSetting(string toml, string key, string value)
    {
        var line = toml.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(candidate => candidate.TrimEnd('\r'))
            .Single(candidate => candidate.StartsWith(key + " =", StringComparison.Ordinal));
        return toml.Replace(line, $"{key} = {value}", StringComparison.Ordinal);
    }

    private static string ValidToml(string listenAddress) => $$"""
        listen_address = "{{listenAddress}}"
        listen_port = 17653
        camera_index = 0
        capture_width = 1280
        capture_height = 720
        preview_fps = 5
        jpeg_quality = 85
        max_image_bytes = 2097152
        camera_timeout_seconds = 10
        """;
}
