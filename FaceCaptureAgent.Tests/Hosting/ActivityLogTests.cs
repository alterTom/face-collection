using FaceCaptureAgent.Diagnostics;
using FaceCaptureAgent.Protocol;
using Xunit;

namespace FaceCaptureAgent.Tests.Hosting;

public sealed class ActivityLogTests
{
    [Fact]
    public void History_DropsOldestEntries()
    {
        var log = new ActivityLog();
        for (var i = 0; i < 1005; i++) log.Write($"event-{i}");
        var entries = log.Snapshot();
        Assert.Equal(1000, entries.Length);
        Assert.EndsWith("event-5", entries[0].Text);
        Assert.EndsWith("event-1004", entries[^1].Text);
    }

    [Fact]
    public void CaptureResult_RecordsOutcomeWithoutPhotoOrRequestData()
    {
        var log = new ActivityLog();
        log.RecordResponse("capture", ResponseEnvelope.Success("capture.result", "private-id", new { base64 = "private-photo" }));
        var text = Assert.Single(log.Snapshot()).Text;
        Assert.Contains("抓拍照片成功", text);
        Assert.DoesNotContain("private", text);
    }

    [Fact]
    public void FailedCommand_RecordsErrorCodeWithoutUntrustedMessage()
    {
        var log = new ActivityLog();
        log.RecordResponse("camera.open", ResponseEnvelope.Error("private-id", "CAMERA_BUSY", "private-details", true));
        var text = Assert.Single(log.Snapshot()).Text;
        Assert.Contains("打开摄像头失败", text);
        Assert.Contains("CAMERA_BUSY", text);
        Assert.DoesNotContain("private", text);
    }
}
