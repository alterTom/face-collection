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

/// <summary>统一协议封装：命令响应带 requestId，主动事件带 event 标记并在 data 中携带轮次。</summary>
public static class ResponseEnvelope
{
    public static byte[] Event(string type, object data) =>
        JsonSerializer.SerializeToUtf8Bytes(new { type, @event = true, data }, SerializerOptions);

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
