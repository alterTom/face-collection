using FaceCaptureAgent.Desktop;
using FaceCaptureAgent.Hosting;

var app = AgentApplication.Build(args, builder => builder.Services.AddHostedService<TrayService>());
app.Run();

public partial class Program;
