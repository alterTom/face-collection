using Microsoft.AspNetCore.StaticFiles;

namespace FaceCaptureAgent.Hosting;

public static class TestPage
{
    public static void Map(WebApplication app)
    {
        var assembly = typeof(TestPage).Assembly;
        var resources = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith("TestPage/", StringComparison.Ordinal))
            .ToDictionary(name => name["TestPage/".Length..].Replace('\\', '/'), StringComparer.Ordinal);
        var contentTypes = new FileExtensionContentTypeProvider();
        app.MapGet("/test/{**path}", (HttpContext context, string? path) =>
        {
            if (context.Request.Path == "/test") return Results.Redirect("/test/");
            var file = string.IsNullOrEmpty(path) ? "index.html" : path;
            if (!resources.TryGetValue(file, out var resource)) return Results.NotFound();
            if (!contentTypes.TryGetContentType(file, out var contentType))
                contentType = "application/octet-stream";
            context.Response.Headers.CacheControl = "no-cache";
            return Results.Stream(assembly.GetManifestResourceStream(resource)!, contentType);
        });
    }
}
