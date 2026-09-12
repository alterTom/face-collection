using System.Text.Json;

namespace FaceCaptureAgent.Protocol;

/// <summary>校验消息大小、JSON 结构和命令名称；具体命令参数由会话层继续校验。</summary>
public static class ProtocolParser
{
    public const int MaxMessageBytes = 65_536;

    private static readonly HashSet<string> AcceptedCommands =
    [
        "system.info",
        "device.list",
        "camera.open",
        "camera.setCaptureMode",
        "capture.rearm",
        "preview.start",
        "preview.stop",
        "capture",
        "camera.close",
        "ping"
    ];

    public static ClientMessage Parse(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length is 0 or > MaxMessageBytes)
        {
            throw Invalid("Message must contain between 1 and 65536 UTF-8 bytes.");
        }

        try
        {
            using var document = JsonDocument.Parse(utf8.ToArray());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw Invalid("Message must be a JSON object.");
            }

            var type = RequiredString(root, "type");
            var requestId = RequiredString(root, "requestId");

            if (!AcceptedCommands.Contains(type))
            {
                throw Invalid($"Unsupported command type: {type}.");
            }

            return new ClientMessage(type, requestId, root.Clone());
        }
        catch (ProtocolException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new ProtocolException(ErrorCodes.InvalidMessage, "Message contains invalid JSON.", false)
            {
                Source = exception.Source
            };
        }
    }

    private static string RequiredString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw Invalid($"{propertyName} must be a non-empty string.");
        }

        return value.GetString()!;
    }

    private static ProtocolException Invalid(string message) =>
        new(ErrorCodes.InvalidMessage, message);
}
