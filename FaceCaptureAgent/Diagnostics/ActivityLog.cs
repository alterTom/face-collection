using System.Text.Json;

namespace FaceCaptureAgent.Diagnostics;

/// <summary>线程安全的内存日志，最多保留最近 1000 条，供托盘窗口读取快照。</summary>
public sealed class ActivityLog
{
    private readonly object _gate = new();
    private readonly Queue<ActivityEntry> _entries = new();
    private long _sequence;

    public void Write(string message)
    {
        lock (_gate)
        {
            _entries.Enqueue(new(++_sequence, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}"));
            while (_entries.Count > 1000) _entries.Dequeue();
        }
    }

    public ActivityEntry[] Snapshot()
    {
        lock (_gate) return _entries.ToArray();
    }

    // 只提取操作结果和错误码，避免将抓拍响应中的照片或 Base64 写入日志。
    public void RecordResponse(string? command, ReadOnlyMemory<byte> response)
    {
        var action = command switch
        {
            "camera.open" => "打开摄像头",
            "camera.close" => "关闭摄像头",
            "capture" => "抓拍照片",
            "preview.start" => "开始预览",
            "preview.stop" => "停止预览",
            "device.list" => "枚举摄像头",
            _ => "处理命令"
        };
        using var document = JsonDocument.Parse(response);
        if (document.RootElement.GetProperty("type").GetString() == "error")
            Write($"{action}失败：{document.RootElement.GetProperty("error").GetProperty("code").GetString()}");
        else if (action != "处理命令")
            Write($"{action}成功");
    }
}

public sealed record ActivityEntry(long Sequence, string Text);
