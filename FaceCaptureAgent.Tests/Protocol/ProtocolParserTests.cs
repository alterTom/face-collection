using System.Text;
using System.Text.Json;
using FaceCaptureAgent.Protocol;
using Xunit;

namespace FaceCaptureAgent.Tests.Protocol;

public sealed class ProtocolParserTests
{
    [Fact]
    public void Parse_ReadsCaptureRequestAndKeepsPayloadAlive()
    {
        var message = ProtocolParser.Parse("""{"type":"capture","requestId":"r1","marker":42}"""u8);

        Assert.Equal("capture", message.Type);
        Assert.Equal("r1", message.RequestId);
        Assert.Equal(42, message.Payload.GetProperty("marker").GetInt32());
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    public void Parse_RejectsMalformedOrNonObjectJson(string json)
    {
        var error = Assert.Throws<ProtocolException>(() =>
            ProtocolParser.Parse(Encoding.UTF8.GetBytes(json)));

        Assert.Equal(ErrorCodes.InvalidMessage, error.Code);
    }

    [Theory]
    [InlineData("{\"requestId\":\"r1\"}")]
    [InlineData("{\"type\":null,\"requestId\":\"r1\"}")]
    [InlineData("{\"type\":42,\"requestId\":\"r1\"}")]
    [InlineData("{\"type\":\"   \",\"requestId\":\"r1\"}")]
    public void Parse_RejectsMissingOrInvalidType(string json)
    {
        var error = Assert.Throws<ProtocolException>(() =>
            ProtocolParser.Parse(Encoding.UTF8.GetBytes(json)));

        Assert.Equal(ErrorCodes.InvalidMessage, error.Code);
    }

    [Theory]
    [InlineData("{\"type\":\"capture\"}")]
    [InlineData("{\"type\":\"capture\",\"requestId\":null}")]
    [InlineData("{\"type\":\"capture\",\"requestId\":7}")]
    [InlineData("{\"type\":\"capture\",\"requestId\":\"\"}")]
    public void Parse_RejectsMissingOrInvalidRequestId(string json)
    {
        var error = Assert.Throws<ProtocolException>(() =>
            ProtocolParser.Parse(Encoding.UTF8.GetBytes(json)));

        Assert.Equal(ErrorCodes.InvalidMessage, error.Code);
    }

    [Fact]
    public void Parse_RejectsUnknownCommand()
    {
        var error = Assert.Throws<ProtocolException>(() =>
            ProtocolParser.Parse("""{"type":"camera.delete","requestId":"r1"}"""u8));

        Assert.Equal(ErrorCodes.InvalidMessage, error.Code);
    }

    [Fact]
    public void Parse_AcceptsEveryProtocolCommand()
    {
        string[] commands =
        [
            "system.info", "device.list", "camera.open", "preview.start",
            "preview.stop", "capture", "camera.close", "ping"
        ];

        foreach (var command in commands)
        {
            var json = Encoding.UTF8.GetBytes($$"""{"type":"{{command}}","requestId":"r1"}""");
            Assert.Equal(command, ProtocolParser.Parse(json).Type);
        }
    }

    [Fact]
    public void Parse_RejectsInputAboveMaximumSize()
    {
        var oversized = new byte[ProtocolParser.MaxMessageBytes + 1];

        var error = Assert.Throws<ProtocolException>(() => ProtocolParser.Parse(oversized));

        Assert.Equal(ErrorCodes.InvalidMessage, error.Code);
    }

    [Fact]
    public void Success_CreatesStableJsonEnvelope()
    {
        var bytes = ResponseEnvelope.Success("capture.result", "r1", new { size = 4 });
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;

        Assert.Equal("capture.result", root.GetProperty("type").GetString());
        Assert.Equal("r1", root.GetProperty("requestId").GetString());
        Assert.Equal(4, root.GetProperty("data").GetProperty("size").GetInt32());
    }

    [Fact]
    public void Error_CreatesStableJsonEnvelope()
    {
        var bytes = ResponseEnvelope.Error("r1", ErrorCodes.CameraBusy, "busy", true);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;

        Assert.Equal("error", root.GetProperty("type").GetString());
        Assert.Equal("r1", root.GetProperty("requestId").GetString());
        Assert.Equal(ErrorCodes.CameraBusy, root.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("busy", root.GetProperty("error").GetProperty("message").GetString());
        Assert.True(root.GetProperty("error").GetProperty("retryable").GetBoolean());
    }
}
