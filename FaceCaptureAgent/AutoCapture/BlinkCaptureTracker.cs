namespace FaceCaptureAgent.AutoCapture;

/// <summary>每轮先校验双眼睁开、闭合、重新睁开，再计时抓拍；不做身份识别。</summary>
public sealed class BlinkCaptureTracker(int stableDurationMs, int timeoutMs = 15000)
{
    private FaceStabilityTracker _stable = new(stableDurationMs);
    private FaceBox? _anchor;
    private long? _started, _lastFrame, _last, _openSince, _closedSince, _reopenedSince;
    private bool _passed, _fired, _timedOut;
    private int _openSamples;
    private readonly Queue<(long Time, EyeMeasurement Eyes)> _openMeasurements = new();

    public StabilityResult Update(IReadOnlyList<FaceBox> faces, long now)
    {
        if (_fired) return new(false, "complete");
        _started ??= now;
        if (_timedOut || now - _started >= timeoutMs)
        {
            _timedOut = true;
            return new(false, "blink-timeout");
        }
        var face = faces.Count == 1 ? faces[0] : null;
        if (face is null || face.Eyes == EyeState.Unknown || face.EyeMeasurement?.State == EyeState.Unknown || !Valid(face))
        {
            Reset();
            return new(false, faces.Count > 1 ? "multiple-faces" : face is null ? "no-face" : "blink-face-camera");
        }
        if (_lastFrame is not null && (now <= _lastFrame || now - _lastFrame > 350)
            || _anchor is not null && (Math.Abs(face.X - _anchor.X) > _anchor.Width * .2
                || Math.Abs(face.Y - _anchor.Y) > _anchor.Height * .2
                || Math.Abs(face.Width - _anchor.Width) > _anchor.Width * .2
                || Math.Abs(face.Height - _anchor.Height) > _anchor.Height * .2)) Reset();
        _lastFrame = now;
        _anchor ??= face;
        var eyeState = ClassifyEyes(face, now);
        if (_passed)
        {
            // 动作已完成：连续人脸负责稳定计时，明确睁眼负责放行最终照片。
            // 再次眨眼或眼睑过渡不能抹掉已通过的结果，失脸/大幅移动/陈旧帧仍在上方重置。
            _last = now;
            var result = _stable.Update(faces, now, allowCapture: eyeState == EyeState.Open);
            _fired = result.Ready;
            return eyeState == EyeState.Open ? result : new(false, "blink-open-eyes");
        }
        if (eyeState == EyeState.Transition)
        {
            // 过渡帧最多容忍 200ms，不计为明确状态样本；阶段时长仍使用单调时钟。
            _stable = new(stableDurationMs);
            if (_last is null || now - _last > 200) Reset();
            return new(false, "blink-face-camera");
        }
        _last = now;
        if (eyeState == EyeState.Closed)
        {
            _reopenedSince = null;
            if (_openSince is null || _openSamples < 2 || now - _openSince < 100)
            {
                _openSince = null;
                _openSamples = 0;
                return new(false, "blink-open-eyes");
            }
            _closedSince ??= now;
            if (now - _closedSince > 1000) { Reset(); return new(false, "blink-open-eyes"); }
            return new(false, "blink-open-eyes");
        }
        if (_closedSince is not null)
        {
            if (now - _closedSince < 50 || now - _closedSince > 1000) Reset();
            else
            {
                _reopenedSince ??= now;
                if (now - _reopenedSince >= 100)
                {
                    _passed = true;
                    // 从通过动作时的位置开始稳定抓拍，避免沿用数秒前的基准累计正常微动。
                    _anchor = face;
                    _stable.Update(faces, now);
                    return new(false, "blink-passed");
                }
                return new(false, "blink-open-eyes");
            }
        }
        _openSince ??= now;
        _openSamples = Math.Min(2, _openSamples + 1);
        return new(false, "blink-required");
    }

    private void Reset()
    {
        _anchor = null;
        _lastFrame = _last = _openSince = _closedSince = _reopenedSince = null;
        _passed = false;
        _openSamples = 0;
        _openMeasurements.Clear();
        _stable = new(stableDurationMs);
    }

    private EyeState ClassifyEyes(FaceBox face, long now)
    {
        var eyes = face.EyeMeasurement;
        if (eyes is null) return face.Eyes;
        // 每轮只收集明确睁眼样本。使用近两秒的上四分位值，单帧高值不能抬高基线。
        while (_openMeasurements.TryPeek(out var oldest) && now - oldest.Time > 2000)
            _openMeasurements.Dequeue();
        if (eyes.State == EyeState.Open && !_passed && _closedSince is null)
        {
            _openMeasurements.Enqueue((now, eyes));
            while (_openMeasurements.Count > 40) _openMeasurements.Dequeue();
        }
        if (eyes.State != EyeState.Transition || _openMeasurements.Count < 3
            || _openMeasurements.Last().Time - _openMeasurements.Peek().Time < 100) return eyes.State;
        var first = _openMeasurements.Select(sample => sample.Eyes.First).Order().ToArray();
        var second = _openMeasurements.Select(sample => sample.Eyes.Second).Order().ToArray();
        var index = (first.Length - 1) * 3 / 4;
        // 双眼都要比本轮睁眼基线下降至少 30%，且均不超过 0.16；保留明确睁眼优先级。
        return eyes.First <= Math.Min(.16, first[index] * .70)
            && eyes.Second <= Math.Min(.16, second[index] * .70) ? EyeState.Closed : EyeState.Transition;
    }

    private static bool Valid(FaceBox face) => double.IsFinite(face.X) && double.IsFinite(face.Y)
        && double.IsFinite(face.Width) && double.IsFinite(face.Height) && face.Width > 0 && face.Height > 0;
}
