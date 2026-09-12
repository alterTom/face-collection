using FaceCaptureAgent.Camera;
using OpenCvSharp;

namespace FaceCaptureAgent.AutoCapture;

public interface IFaceDetector : IDisposable
{
    Task<IReadOnlyList<FaceBox>> DetectAsync(JpegFrame frame, CancellationToken cancellationToken);
}

/// <summary>使用随程序发布的 YuNet 模型检测人脸框，模型在首次检测时加载。</summary>
public sealed class YuNetFaceDetector : IFaceDetector
{
    private FaceDetectorYN? _detector;

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
            // 仅取检测结果前四列的框坐标；保留模型输入坐标系供相对位移比较。
            var rows = output.Rows;
            for (var row = 0; row < rows; row++)
                faces.Add(new(output.At<float>(row, 0), output.At<float>(row, 1), output.At<float>(row, 2), output.At<float>(row, 3)));
            return faces;
        }, cancellationToken);

    public void Dispose() => _detector?.Dispose();
}
