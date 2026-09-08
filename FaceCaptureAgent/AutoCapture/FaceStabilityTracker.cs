namespace FaceCaptureAgent.AutoCapture;

public sealed record FaceBox(double X, double Y, double Width, double Height);
public sealed record StabilityResult(bool Ready, string Status);

public sealed class FaceStabilityTracker(int stableDurationMs)
{
    private FaceBox? _anchor;
    private long _since;
    private long? _last;
    private bool _fired;

    public StabilityResult Update(IReadOnlyList<FaceBox> faces, long nowMs)
    {
        if (_fired) return new(false, "complete");
        var box = faces.Count == 1 ? faces[0] : null;
        if (box is null || !double.IsFinite(box.X) || !double.IsFinite(box.Y)
            || !double.IsFinite(box.Width) || !double.IsFinite(box.Height) || box.Width <= 0 || box.Height <= 0)
        {
            _anchor = null;
            _last = null;
            return new(false, faces.Count > 1 ? "multiple-faces" : "no-face");
        }
        if (_anchor is null || _last is null || nowMs <= _last || nowMs - _last > 750
            || Math.Abs(box.X - _anchor.X) > _anchor.Width * .08
            || Math.Abs(box.Y - _anchor.Y) > _anchor.Height * .08
            || Math.Abs(box.Width - _anchor.Width) > _anchor.Width * .1
            || Math.Abs(box.Height - _anchor.Height) > _anchor.Height * .1)
        {
            _anchor = box;
            _since = nowMs;
        }
        _last = nowMs;
        _fired = nowMs - _since >= stableDurationMs;
        return new(_fired, _fired ? "complete" : "stabilizing");
    }
}
