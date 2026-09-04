using System.Text.Json;

namespace FaceCaptureAgent.Protocol;

public sealed record ClientMessage(string Type, string RequestId, JsonElement Payload);

public sealed class ProtocolException : Exception
{
    public ProtocolException(string code, string message, bool retryable = false)
        : base(message)
    {
        Code = code;
        Retryable = retryable;
    }

    public string Code { get; }

    public bool Retryable { get; }
}

public static class ResponseEnvelope
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static byte[] Success(string type, string requestId, object data) =>
        JsonSerializer.SerializeToUtf8Bytes(new { type, requestId, data }, SerializerOptions);

    public static byte[] Error(
        string requestId,
        string code,
        string message,
        bool retryable) =>
        JsonSerializer.SerializeToUtf8Bytes(
            new { type = "error", requestId, error = new { code, message, retryable } },
            SerializerOptions);
}
