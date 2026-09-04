using System.Net;
using FaceCaptureAgent.Camera;
using FaceCaptureAgent.Configuration;
using FaceCaptureAgent.Sessions;

var configPath = ResolveConfigPath(args);
var agentOptions = TomlOptionsLoader.Load(configPath);

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(server =>
    server.Listen(IPAddress.Parse(agentOptions.ListenAddress), agentOptions.ListenPort));
builder.Services.AddSingleton(agentOptions);
builder.Services.AddSingleton<CameraLeaseManager>();
builder.Services.AddTransient<ICameraService, OpenCvCameraService>();

var app = builder.Build();
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30)
});

app.Map("/face", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("A WebSocket connection is required.");
        return;
    }

    if (!context.WebSockets.WebSocketRequestedProtocols.Contains(
            WebSocketConnection.RequiredSubprotocol,
            StringComparer.Ordinal))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("The face-capture.v1 WebSocket subprotocol is required.");
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync(
        WebSocketConnection.RequiredSubprotocol);
    var connection = new WebSocketConnection(
        socket,
        context.RequestServices.GetRequiredService<ICameraService>(),
        context.RequestServices.GetRequiredService<CameraLeaseManager>(),
        agentOptions);
    await connection.RunAsync(context.RequestAborted);
});

app.Run();

static string ResolveConfigPath(string[] arguments)
{
    for (var index = 0; index < arguments.Length; index++)
    {
        if (!string.Equals(arguments[index], "--config", StringComparison.Ordinal))
        {
            continue;
        }

        if (index + 1 >= arguments.Length || string.IsNullOrWhiteSpace(arguments[index + 1]))
        {
            throw new InvalidOperationException("--config requires an absolute file path.");
        }

        var explicitPath = arguments[index + 1];
        if (!Path.IsPathFullyQualified(explicitPath))
        {
            throw new InvalidOperationException("--config requires an absolute file path.");
        }

        return explicitPath;
    }

    return Path.Combine(AppContext.BaseDirectory, "config.toml");
}

public partial class Program;
