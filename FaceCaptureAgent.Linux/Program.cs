using FaceCaptureAgent.Desktop;
using FaceCaptureAgent.Hosting;

var headless = args.Contains("--headless", StringComparer.Ordinal);
var hostArgs = args.Where(arg => arg != "--headless").ToArray();
var app = AgentApplication.Build(hostArgs, builder =>
{
    if (!headless) builder.Services.AddHostedService<LinuxDesktopService>();
});
app.Run();

public partial class Program;
