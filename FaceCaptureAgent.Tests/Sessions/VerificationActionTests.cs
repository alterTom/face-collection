using System.Text.Json;
using FaceCaptureAgent.AutoCapture;
using FaceCaptureAgent.Protocol;
using Xunit;

namespace FaceCaptureAgent.Tests.Sessions;

public sealed class VerificationActionTests
{
    [Theory]
    [InlineData("none")]
    [InlineData("blink")]
    [InlineData("mouth-open")]
    [InlineData("turn-left")]
    [InlineData("turn-right")]
    public void Options_ReturnSelectedAction(string action)
    {
        var options = AutoCaptureOptions.Parse(JsonSerializer.SerializeToElement(new { verificationAction = action }));
        var json = JsonSerializer.SerializeToElement(options, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.True(json.TryGetProperty("verificationAction", out var value));
        Assert.Equal(action, value.GetString());
        Assert.False(json.TryGetProperty("blinkVerification", out _));
    }

    [Theory]
    [InlineData("\"random\"")]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("[]")]
    public void Options_RejectInvalidAction(string value) =>
        Assert.Throws<ProtocolException>(() => AutoCaptureOptions.Parse(JsonDocument.Parse("{\"verificationAction\":" + value + "}").RootElement));
}
