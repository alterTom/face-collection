using System.Net;
using System.Text.RegularExpressions;
using FaceCaptureAgent.Desktop;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FaceCaptureAgent.Tests.Hosting;

public sealed class TestPageTests
{
    [Fact]
    public async Task TestPage_ServesHtmlAndBundledAssets()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/test/", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("id=\"app\"", html);
        var assets = Regex.Matches(html, "(?:src|href)=\"(\\./assets/[^\"]+)\"");
        Assert.NotEmpty(assets);
        foreach (Match asset in assets)
        {
            var resource = await client.GetAsync("/test/" + asset.Groups[1].Value[2..], TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, resource.StatusCode);
            Assert.NotEmpty(await resource.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        }
    }

    [Theory]
    [InlineData("/test/config.toml")]
    [InlineData("/test/assets/missing.js")]
    public async Task TestPage_UnknownFile_ReturnsNotFound(string path)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path, TestContext.Current.CancellationToken)).StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.Remove(services.Single(service => service.ImplementationType == typeof(TrayService)))));
}
