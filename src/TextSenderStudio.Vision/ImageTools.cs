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
        var mat = Cv2.ImDecode(bytes, ImreadModes.Color);
        if (!mat.Empty()) return mat;
        mat.Dispose();
        throw new InvalidDataException("无法读取图片，请选择有效的 PNG、JPEG 或 BMP。");
    }
    internal static byte[] Encode(Mat mat)
    {
        if (!Cv2.ImEncode(".png", mat, out var bytes)) throw new InvalidDataException("无法编码 PNG 图片。");
        return bytes;
    }
    internal static FrameSnapshot FromMat(Mat mat) => new(Encode(mat), mat.Width, mat.Height, DateTimeOffset.UtcNow);
}

public static class TemplateMatcher
{
    public static MatchResult Match(FrameSnapshot frame, FrameSnapshot template, ImageLabel label)
    {
        if (!double.IsFinite(label.Threshold) || label.Threshold < 0 || label.Threshold > 1)
            throw new ArgumentOutOfRangeException(nameof(label), "匹配阈值须在 0–1 之间。");
        using var image = ImageTools.Decode(frame.Png);
        using var target = ImageTools.Decode(template.Png);
        var roi = label.SearchRegion ?? new PixelRegion { Width = image.Width, Height = image.Height };
        ImageTools.ValidateRegion(roi, image.Width, image.Height);
        if (target.Width > roi.Width || target.Height > roi.Height)
            throw new InvalidOperationException("目标图片大于搜索区域，请扩大识图区域或重新截取标签。");
        using var search = new Mat(image, ImageTools.ToRect(roi));
        using var searchGray = new Mat();
        using var targetGray = new Mat();
        Mat source = search, needle = target;
        if (label.Grayscale)
        {
            Cv2.CvtColor(search, searchGray, ColorConversionCodes.BGR2GRAY);
            Cv2.CvtColor(target, targetGray, ColorConversionCodes.BGR2GRAY);
            source = searchGray;
            needle = targetGray;
        }
        Cv2.MeanStdDev(needle, out _, out Scalar deviation);
        var variance = deviation.Val0 * deviation.Val0 + deviation.Val1 * deviation.Val1 + deviation.Val2 * deviation.Val2;
        if (variance < 0.01)
            throw new InvalidOperationException("标签图片为纯色或几乎没有细节，无法可靠匹配。请框选包含文字、边缘或图案的区域。");
        using var result = new Mat();
        Cv2.MatchTemplate(source, needle, result, TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(result, out _, out var maximum, out _, out var location);
        var score = double.IsFinite(maximum) ? Math.Clamp(maximum, 0, 1) : 0;
        return new MatchResult(score, score >= label.Threshold, new PixelRegion
        {
            X = roi.X + location.X, Y = roi.Y + location.Y, Width = needle.Width, Height = needle.Height
        });
    }
}
