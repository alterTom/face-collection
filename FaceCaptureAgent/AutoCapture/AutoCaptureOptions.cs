using System.Text.Json;
using FaceCaptureAgent.Protocol;

namespace FaceCaptureAgent.AutoCapture;

public sealed record AutoCaptureOptions(string CaptureMode = "manual", int StableDurationMs = 1500)
{
    public static AutoCaptureOptions Parse(JsonElement payload, AutoCaptureOptions? defaults = null)
    {
        defaults ??= new();
        var mode = defaults.CaptureMode;
        var duration = defaults.StableDurationMs;
        if (payload.TryGetProperty("captureMode", out var value))
        {
            if (value.ValueKind != JsonValueKind.String || value.GetString() is not ("manual" or "auto"))
                throw new ProtocolException(ErrorCodes.InvalidMessage, "captureMode must be manual or auto.");
            mode = value.GetString()!;
        }
        if (payload.TryGetProperty("stableDurationMs", out value)
            && (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out duration) || duration is < 500 or > 10000))
            throw new ProtocolException(ErrorCodes.InvalidMessage, "stableDurationMs must be an integer between 500 and 10000.");
        return new(mode, duration);
    }
}
