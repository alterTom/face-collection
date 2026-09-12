using System.Net;
using FaceCaptureAgent.Camera;
using FaceCaptureAgent.Configuration;
using FaceCaptureAgent.Sessions;
using FaceCaptureAgent.Diagnostics;
using FaceCaptureAgent.Desktop;

// 启动顺序：加载并校验配置 → 注册服务 → 映射 HTTP/WebSocket 入口 → 启动托盘和监听。
var configPath = ResolveConfigPath(args);
var agentOptions = TomlOptionsLoader.Load(configPath);

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(server =>
    server.Listen(IPAddress.Parse(agentOptions.ListenAddress), agentOptions.ListenPort));
builder.Services.AddSingleton(agentOptions);
builder.Services.AddSingleton<ActivityLog>();
builder.Services.AddHostedService<TrayService>();
// 租约在所有连接间共享；摄像头服务按连接创建，由会话负责释放。
builder.Services.AddSingleton<CameraLeaseManager>();
builder.Services.AddTransient<ICameraService, OpenCvCameraService>();

var app = builder.Build();
FaceCaptureAgent.Hosting.TestPage.Map(app);
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
        agentOptions,
        context.RequestServices.GetRequiredService<ActivityLog>());
    // 浏览器断开或托盘退出都必须结束会话，确保摄像头不再被占用。
    using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
        context.RequestAborted, app.Lifetime.ApplicationStopping);
    await connection.RunAsync(cancellation.Token);
});

var activityLog = app.Services.GetRequiredService<ActivityLog>();
app.Lifetime.ApplicationStarted.Register(() => activityLog.Write($"服务已启动：{agentOptions.ListenAddress}:{agentOptions.ListenPort}"));
app.Lifetime.ApplicationStopping.Register(() => activityLog.Write("服务正在停止"));
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
