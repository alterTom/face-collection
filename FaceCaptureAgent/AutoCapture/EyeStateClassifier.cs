using OpenCvSharp;
using OpenCvSharp.Dnn;

namespace FaceCaptureAgent.AutoCapture;

/// <summary>离线 Face Mesh 眼睑几何判断；不依赖眼睛纹理的二分类概率。</summary>
public sealed class EyeStateClassifier : IDisposable
{
    private Net? _network;
    private static readonly int[] FirstEye = [33, 160, 158, 133, 153, 144];
    private static readonly int[] SecondEye = [362, 385, 387, 263, 373, 380];

    public EyeState Classify(Mat image, FaceBox face, Point2f first, Point2f second) =>
        Measure(image, face, first, second)?.State ?? EyeState.Unknown;

    public EyeMeasurement? Measure(Mat image, FaceBox face, Point2f first, Point2f second) =>
        MeasureFace(image, face, first, second)?.Eyes;

    public FaceMeasurement? MeasureFace(Mat image, FaceBox face, Point2f first, Point2f second)
    {
        if (!Valid(first) || !Valid(second) || !double.IsFinite(face.X) || !double.IsFinite(face.Y)
            || !double.IsFinite(face.Width) || !double.IsFinite(face.Height) || face.Width <= 0 || face.Height <= 0)
            return null;
        var distance = Math.Abs(second.X - first.X);
        if (distance < 24 || distance < face.Width * .25 || distance > face.Width * .8) return null;
        // 上游 detection_to_roi：长边 1.5 倍的正方形，按双眼连线旋转，一次插值至 192。
        if (first.X > second.X) (first, second) = (second, first);
        var center = new Point2f((float)(face.X + face.Width / 2), (float)(face.Y + face.Height / 2));
        var side = Math.Max(face.Width, face.Height) * 1.5;
        var angle = Math.Atan2(second.Y - first.Y, second.X - first.X) * 180 / Math.PI;
        using var transform = Cv2.GetRotationMatrix2D(center, angle, 192 / side);
        transform.Set(0, 2, transform.At<double>(0, 2) + 96 - center.X);
        transform.Set(1, 2, transform.At<double>(1, 2) + 96 - center.Y);
        using var crop = new Mat();
        Cv2.WarpAffine(image, crop, transform, new Size(192, 192));
        return Infer(crop, transform, image.Width, image.Height);
    }

    private FaceMeasurement? Infer(Mat crop, Mat transform, int imageWidth, int imageHeight)
    {
        _network ??= Cv2.Dnn.ReadNetFromONNX(Path.Combine(AppContext.BaseDirectory, "Models", "face_mesh_Nx3x192x192.onnx"))
            ?? throw new InvalidOperationException("Eye landmark model could not be loaded.");
        using var blob = Cv2.Dnn.BlobFromImage(crop, 1d / 255, new Size(192, 192), swapRB: true);
        _network.SetInput(blob);
        using var landmarks = new Mat();
        using var score = new Mat();
        _network.Forward([landmarks, score], ["landmarks", "score"]);
        if (landmarks.Total() != 468 * 3 || score.Total() != 1)
            throw new InvalidOperationException("Invalid eye landmark model output.");
        // 原始输出是 logit，log(9) 对应 90% 的人脸存在概率。
        var presence = score.At<float>(0);
        if (!float.IsFinite(presence) || presence < Math.Log(9)) return null;
        using var points = landmarks.Reshape(1, 468);
        using var inverse = new Mat();
        Cv2.InvertAffineTransform(transform, inverse);
        var measurement = new EyeMeasurement(Ratio(FirstEye), Ratio(SecondEye));
        var mouthLeft = Point(61);
        var mouthRight = Point(291);
        var upperLip = Point(13);
        var lowerLip = Point(14);
        var leftEye = Point(33);
        var rightEye = Point(263);
        var nose = Point(1);
        return new(measurement, MouthOpening(mouthLeft, mouthRight, upperLip, lowerLip), HeadTurn(leftEye, rightEye, nose));

        Point2f Point(int index)
        {
            var x = points.At<float>(index, 0);
            var y = points.At<float>(index, 1);
            var sx = inverse.At<double>(0, 0) * x + inverse.At<double>(0, 1) * y + inverse.At<double>(0, 2);
            var sy = inverse.At<double>(1, 0) * x + inverse.At<double>(1, 1) * y + inverse.At<double>(1, 2);
            return !float.IsFinite(x) || !float.IsFinite(y) || x < 0 || x >= 192 || y < 0 || y >= 192
                || sx < 0 || sx >= imageWidth || sy < 0 || sy >= imageHeight
                ? new(float.NaN, float.NaN) : new(x, y);
        }

        double Ratio(int[] indices)
        {
            var eye = new Point2f[6];
            for (var i = 0; i < eye.Length; i++)
            {
                var x = points.At<float>(indices[i], 0);
                var y = points.At<float>(indices[i], 1);
                if (!float.IsFinite(x) || !float.IsFinite(y) || x < 0 || x >= 192 || y < 0 || y >= 192) return double.NaN;
                var sourceX = inverse.At<double>(0, 0) * x + inverse.At<double>(0, 1) * y + inverse.At<double>(0, 2);
                var sourceY = inverse.At<double>(1, 0) * x + inverse.At<double>(1, 1) * y + inverse.At<double>(1, 2);
                if (sourceX < 0 || sourceX >= imageWidth || sourceY < 0 || sourceY >= imageHeight) return double.NaN;
                eye[i] = new(x, y);
            }
            var width = Length(eye[0], eye[3]);
            return width < 4 ? double.NaN : (Length(eye[1], eye[5]) + Length(eye[2], eye[4])) / (2 * width);
        }
    }

    /// <summary>上下眼睑间距 / 眼角宽度；保留 0.13–0.16 的过渡区，避免半睁眼直接计为闭眼。</summary>
    public static EyeState FromAspectRatios(double first, double second)
    {
        if (!double.IsFinite(first) || !double.IsFinite(second) || first < 0 || second < 0 || first > 1 || second > 1)
            return EyeState.Unknown;
        if (first >= .16 && second >= .16) return EyeState.Open;
        if (first <= .13 && second <= .13) return EyeState.Closed;
        return EyeState.Transition;
    }

    // 比例指标不依赖图像大小；转头用眼睛轴上的鼻尖偏移，抵消平移和歪头。
    // 原始摄像头帧不镜像：正值表示使用者向自己的左侧转头。它不是角度估计。
    public static double HeadTurn(Point2f first, Point2f second, Point2f nose)
    {
        if (!Valid(first) || !Valid(second) || !Valid(nose)) return double.NaN;
        if (first.X > second.X) (first, second) = (second, first);
        var dx = second.X - first.X;
        var dy = second.Y - first.Y;
        var squared = dx * dx + dy * dy;
        return squared < 16 ? double.NaN : ((nose.X - (first.X + second.X) / 2) * dx
            + (nose.Y - (first.Y + second.Y) / 2) * dy) / squared;
    }

    public static double MouthOpening(Point2f left, Point2f right, Point2f upper, Point2f lower)
    {
        if (!Valid(left) || !Valid(right) || !Valid(upper) || !Valid(lower)) return double.NaN;
        var width = Length(left, right);
        return width < 4 ? double.NaN : Length(upper, lower) / width;
    }

    private static bool Valid(Point2f point) => float.IsFinite(point.X) && float.IsFinite(point.Y);
    private static double Length(Point2f a, Point2f b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
    public void Dispose() => _network?.Dispose();
}

public sealed record EyeMeasurement(double First, double Second)
{
    public EyeState State => EyeStateClassifier.FromAspectRatios(First, Second);
}

public sealed record FaceMeasurement(EyeMeasurement Eyes, double MouthRatio, double TurnRatio);
