using System.Net.WebSockets;
using FaceCaptureAgent.Camera;
using FaceCaptureAgent.Configuration;
using FaceCaptureAgent.Protocol;
using FaceCaptureAgent.Diagnostics;

namespace FaceCaptureAgent.Sessions;

public sealed class WebSocketConnection
{
    public const string RequiredSubprotocol = "face-capture.v1";

    private readonly WebSocket _socket;
    private readonly ICameraService _camera;
    private readonly CameraLeaseManager _leaseManager;
    private readonly AgentOptions _options;
    private readonly ActivityLog? _log;
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    public WebSocketConnection(
        WebSocket socket,
        ICameraService camera,
        CameraLeaseManager leaseManager,
        AgentOptions options,
        ActivityLog? log = null)
    {
        _socket = socket;
        _camera = camera;
        _leaseManager = leaseManager;
        _options = options;
        _log = log;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _log?.Write("客户端已连接");
        await using var session = new CaptureSession(
            Guid.NewGuid(),
            _camera,
            _leaseManager,
            _options,
            SendBinaryAsync,
            SendTextAsync);

        try
        {
            while (_socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var incoming = await ReceiveMessageAsync(cancellationToken).ConfigureAwait(false);
                if (incoming is null)
                {
                    break;
                }

                if (incoming.Value.MessageType != WebSocketMessageType.Text)
                {
                    await SendTextAsync(
                            ResponseEnvelope.Error(
                                string.Empty,
                                ErrorCodes.InvalidMessage,
                                "Commands must use WebSocket text frames.",
                                false),
                            cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                ClientMessage? message = null;
                ReadOnlyMemory<byte> response;
                try
                {
                    message = ProtocolParser.Parse(incoming.Value.Payload.Span);
                    response = await session.HandleAsync(message, cancellationToken).ConfigureAwait(false);
                }
                catch (ProtocolException exception)
                {
                    response = ResponseEnvelope.Error(
                        message?.RequestId ?? string.Empty,
                        exception.Code,
                        exception.Message,
                        exception.Retryable);
                }
                catch (CameraException exception)
                {
                    response = ResponseEnvelope.Error(
                        message?.RequestId ?? string.Empty,
                        exception.Code,
                        exception.Message,
                        exception.Retryable);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch
                {
                    response = ResponseEnvelope.Error(
                        message?.RequestId ?? string.Empty,
                        ErrorCodes.InternalError,
                        "The local face capture agent encountered an internal error.",
                        true);
                }

                _log?.RecordResponse(message?.Type, response);
                await SendTextAsync(response, cancellationToken).ConfigureAwait(false);
                // Establish the round in the client before emitting any of its events.
                session.StartPendingAutoCapture();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (WebSocketException)
        {
        }
        finally
        {
            try
            {
                await session.DisposeAsync().ConfigureAwait(false);
                _log?.Write("客户端已断开连接，摄像头资源已释放");
            }
            catch
            {
                _log?.Write("断开连接时释放摄像头失败");
                throw;
            }
            if (_socket.State == WebSocketState.CloseReceived)
            {
                try
                {
                    await _socket.CloseOutputAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "Connection closed.",
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (WebSocketException)
                {
                }
            }
        }
    }

    private async Task<IncomingMessage?> ReceiveMessageAsync(CancellationToken cancellationToken)
    {
        using var payload = new MemoryStream();
        var buffer = new byte[8 * 1024];
        WebSocketMessageType? messageType = null;

        while (true)
        {
            var result = await _socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            messageType ??= result.MessageType;
            if (messageType != result.MessageType)
            {
                await CloseForProtocolErrorAsync("A message cannot mix frame types.", cancellationToken)
                    .ConfigureAwait(false);
                return null;
            }

            if (payload.Length + result.Count > ProtocolParser.MaxMessageBytes)
            {
                await SendTextAsync(
                        ResponseEnvelope.Error(
                            string.Empty,
                            ErrorCodes.InvalidMessage,
                            "Command exceeds the 65536-byte limit.",
                            false),
                        cancellationToken)
                    .ConfigureAwait(false);
                await CloseForProtocolErrorAsync("Command is too large.", cancellationToken)
                    .ConfigureAwait(false);
                return null;
            }

            payload.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                return new IncomingMessage(messageType.Value, payload.ToArray());
            }
        }
    }

    private Task SendBinaryAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken) =>
        SendAsync(bytes, WebSocketMessageType.Binary, cancellationToken);

    private Task SendTextAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken) =>
        SendAsync(bytes, WebSocketMessageType.Text, cancellationToken);

    private async Task SendAsync(
        ReadOnlyMemory<byte> bytes,
        WebSocketMessageType messageType,
        CancellationToken cancellationToken)
    {
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                await _socket.SendAsync(bytes, messageType, true, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private async Task CloseForProtocolErrorAsync(string description, CancellationToken cancellationToken)
    {
        if (_socket.State == WebSocketState.Open)
        {
            await _socket.CloseOutputAsync(
                    WebSocketCloseStatus.ProtocolError,
                    description,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private readonly record struct IncomingMessage(
        WebSocketMessageType MessageType,
        ReadOnlyMemory<byte> Payload);
}
