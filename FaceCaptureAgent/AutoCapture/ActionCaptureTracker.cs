namespace FaceCaptureAgent.AutoCapture;

/// <summary>单一动作校验：中性姿态、指定动作、恢复姿态，然后稳定抓拍。</summary>
public sealed class ActionCaptureTracker(string action, int stableDurationMs, int timeoutMs = 15000)
{
    private FaceStabilityTracker _stable = new(stableDurationMs);
    private FaceBox? _anchor;
    private long? _started, _last, _heldSince;
    private int _phase;
    private bool _fired, _timedOut;

    public StabilityResult Update(IReadOnlyList<FaceBox> faces, long now)
    {
        if (action is not ("mouth-open" or "turn-left" or "turn-right"))
            throw new ArgumentException("Unsupported action.", nameof(action));
        if (_fired) return new(false, "complete");
        _started ??= now;
        if (_timedOut || now - _started >= timeoutMs)
        {
            _timedOut = true;
            return new(false, "action-timeout");
        }
        var face = faces.Count == 1 ? faces[0] : null;
        if (face is null || !Valid(face))
        {
            Reset();
            return new(false, faces.Count > 1 ? "multiple-faces" : face is null ? "no-face" : "action-face-camera");
        }
        // 转头允许合理的脸框变化；眼睛状态不参与张嘴或转头校验。
        if (_last is not null && (now <= _last || now - _last > 350)
            || _anchor is not null && (Math.Abs(face.X - _anchor.X) > _anchor.Width * .45
                || Math.Abs(face.Y - _anchor.Y) > _anchor.Height * .45
                || Math.Abs(face.Width - _anchor.Width) > _anchor.Width * .45
                || Math.Abs(face.Height - _anchor.Height) > _anchor.Height * .45)) Reset();
        _last = now;
        _anchor ??= face;
        var neutral = Math.Abs(face.TurnRatio) <= .10 && (action != "mouth-open" || face.MouthRatio <= .12);
        var performed = action switch
        {
            "mouth-open" => face.MouthRatio >= .30 && Math.Abs(face.TurnRatio) <= .10,
            "turn-left" => face.TurnRatio >= .22,
            _ => face.TurnRatio <= -.22
        };
        if (_phase == 3)
        {
            if (!neutral) { _stable = new(stableDurationMs); return new(false, action == "mouth-open" ? "mouth-close-required" : "action-return"); }
            var result = _stable.Update(faces, now);
            _fired = result.Ready;
            return result;
        }
        var expected = _phase == 1 ? performed : neutral;
        if (!expected) _heldSince = null;
        else
        {
            _heldSince ??= now;
            if (now - _heldSince >= 200)
            {
                _phase++;
                _heldSince = null;
                if (_phase == 3)
                {
                    _anchor = face;
                    _stable.Update(faces, now);
                    return new(false, "action-passed");
                }
            }
        }
        return new(false, _phase switch { 0 => "action-face-camera", 1 => action + "-required", _ => action == "mouth-open" ? "mouth-close-required" : "action-return" });
    }

    private bool Valid(FaceBox face) => double.IsFinite(face.X) && double.IsFinite(face.Y)
        && double.IsFinite(face.Width) && double.IsFinite(face.Height) && face.Width > 0 && face.Height > 0
        && double.IsFinite(face.TurnRatio) && Math.Abs(face.TurnRatio) <= 1.5
        && (action != "mouth-open" || double.IsFinite(face.MouthRatio) && face.MouthRatio is >= 0 and <= 2);

    private void Reset()
    {
        _anchor = null;
        _last = _heldSince = null;
        _phase = 0;
        _stable = new(stableDurationMs);
    }
}
