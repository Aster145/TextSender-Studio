using OpenCvSharp;
using TextSenderStudio.Core;

namespace TextSenderStudio.Vision;

public sealed record FrameSnapshot(byte[] Png, int Width, int Height, DateTimeOffset CapturedAt)
{
    public FrameSnapshot Copy() => this with { Png = (byte[])Png.Clone() };
}

public sealed record MatchResult(double Score, bool IsMatch, PixelRegion Bounds)
{
    public double Score0To1 => Score;
}

public static class ImageTools
{
    public static FrameSnapshot Load(string path) => FromEncoded(File.ReadAllBytes(path));

    public static FrameSnapshot FromEncoded(byte[] encoded)
    {
        using var image = Decode(encoded);
        return FromMat(image);
    }

    public static void Save(FrameSnapshot frame, string path)
    {
        using var image = Decode(frame.Png);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        Directory.CreateDirectory(directory!);
        File.WriteAllBytes(path, Encode(image));
    }

    public static FrameSnapshot Crop(FrameSnapshot frame, PixelRegion region)
    {
        using var image = Decode(frame.Png);
        ValidateRegion(region, image.Width, image.Height);
        using var cropped = new Mat(image, ToRect(region));
        return FromMat(cropped) with { CapturedAt = frame.CapturedAt };
    }

    /// <summary>Preview the exact preprocessing used by matching, without changing the saved source image.</summary>
    public static FrameSnapshot Preprocess(FrameSnapshot frame, ImageLabel label)
    {
        TemplateMatcher.ValidateOptions(label);
        using var source = Decode(frame.Png);
        using var processed = TemplateMatcher.Prepare(source, label);
        if (source.Channels() != 4) return FromMat(processed) with { CapturedAt = frame.CapturedAt };
        using var preview = new Mat();
        Cv2.CvtColor(processed, preview, processed.Channels() == 1 ? ColorConversionCodes.GRAY2BGRA : ColorConversionCodes.BGR2BGRA);
        using var alpha = new Mat();
        Cv2.ExtractChannel(source, alpha, 3); Cv2.InsertChannel(alpha, preview, 3);
        return FromMat(preview) with { CapturedAt = frame.CapturedAt };
    }

    public static void ValidateRegion(PixelRegion region, int width, int height)
    {
        if (region.X < 0 || region.Y < 0 || region.Width <= 0 || region.Height <= 0 ||
            (long)region.X + region.Width > width || (long)region.Y + region.Height > height)
            throw new ArgumentOutOfRangeException(nameof(region), $"选区必须在 {width} × {height} 画面内，且宽高大于 0。");
    }

    internal static Rect ToRect(PixelRegion region) => new(region.X, region.Y, region.Width, region.Height);
    internal static Mat Decode(byte[] bytes)
    {
        if (bytes.Length == 0) throw new InvalidDataException("图片内容为空。");
        var mat = Cv2.ImDecode(bytes, ImreadModes.Unchanged);
        if (mat.Empty()) { mat.Dispose(); throw new InvalidDataException("无法读取图片，请选择有效的 PNG、JPEG 或 BMP。"); }
        if (mat.Depth() == MatType.CV_16U)
        {
            var normalized = new Mat(); mat.ConvertTo(normalized, MatType.CV_8U, 1.0 / 256); mat.Dispose(); mat = normalized;
        }
        if (mat.Depth() != MatType.CV_8U || mat.Channels() is not (1 or 3 or 4))
        { mat.Dispose(); throw new InvalidDataException("图片须为灰度、RGB 或 RGBA 格式。"); }
        return mat;
    }
    internal static byte[] Encode(Mat mat)
    {
        if (!Cv2.ImEncode(".png", mat, out var bytes)) throw new InvalidDataException("无法编码 PNG 图片。");
        return bytes;
    }
    internal static FrameSnapshot FromMat(Mat mat) => new(Encode(mat), mat.Width, mat.Height, DateTimeOffset.UtcNow);
}
