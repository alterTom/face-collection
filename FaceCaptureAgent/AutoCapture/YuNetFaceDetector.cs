using FaceCaptureAgent.Camera;
using OpenCvSharp;

namespace FaceCaptureAgent.AutoCapture;

public interface IFaceDetector : IDisposable
{
    Task<IReadOnlyList<FaceBox>> DetectAsync(JpegFrame frame, CancellationToken cancellationToken);
}

/// <summary>使用随程序发布的 YuNet 模型检测人脸框，模型在首次检测时加载。</summary>
public sealed class YuNetFaceDetector(string verificationAction = "none") : IFaceDetector
{
    private FaceDetectorYN? _detector;
    private EyeStateClassifier? _eyes;

    public Task<IReadOnlyList<FaceBox>> DetectAsync(JpegFrame frame, CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<FaceBox>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _detector ??= FaceDetectorYN.Create(
                Path.Combine(AppContext.BaseDirectory, "Models", "face_detection_yunet_2023mar.onnx"),
                "", new Size(320, 320), .85f);
            using var source = Cv2.ImDecode(frame.Bytes, ImreadModes.Color);
            if (source.Empty()) throw new InvalidOperationException("Invalid detection frame.");
            using var resized = new Mat();
            // 等比例缩放到 320×320 内，右侧或底部补黑，避免拉伸人脸影响稳定判断。
            var scale = 320d / Math.Max(source.Width, source.Height);
            Cv2.Resize(source, resized, new Size(Math.Max(1, (int)(source.Width * scale)), Math.Max(1, (int)(source.Height * scale))));
            using var input = new Mat(320, 320, MatType.CV_8UC3, Scalar.All(0));
            using var region = new Mat(input, new Rect(0, 0, resized.Width, resized.Height));
            resized.CopyTo(region);
            using var output = new Mat();
            _detector.Detect(input, output);
            cancellationToken.ThrowIfCancellationRequested();
            var faces = new List<FaceBox>();
            // 框保留模型输入坐标系；眼睛关键点映射回原图，避免缩小后丢失眼部细节。
            var rows = output.Rows;
            for (var row = 0; row < rows; row++)
            {
                var face = new FaceBox(output.At<float>(row, 0), output.At<float>(row, 1), output.At<float>(row, 2), output.At<float>(row, 3));
                if (verificationAction != "none" && rows == 1)
                {
                    _eyes ??= new();
                    Point2f Point(int column) => new((float)(output.At<float>(row, column) / scale), (float)(output.At<float>(row, column + 1) / scale));
                    var sourceBox = new FaceBox(face.X / scale, face.Y / scale, face.Width / scale, face.Height / scale);
                    var measurement = _eyes.MeasureFace(source, sourceBox, Point(4), Point(6));
                    face = face with { Eyes = measurement?.Eyes.State ?? EyeState.Unknown, EyeMeasurement = measurement?.Eyes,
                        MouthRatio = measurement?.MouthRatio ?? double.NaN, TurnRatio = measurement?.TurnRatio ?? double.NaN };
                }
                faces.Add(face);
            }
            return faces;
        }, cancellationToken);

    public void Dispose() { _eyes?.Dispose(); _detector?.Dispose(); }
}
