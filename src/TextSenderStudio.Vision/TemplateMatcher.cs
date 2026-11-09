using OpenCvSharp;
using TextSenderStudio.Core;
using Size = OpenCvSharp.Size;

namespace TextSenderStudio.Vision;

public sealed record TemplateSearchMethod(string Id, string Name);

public static class TemplateMatcher
{
    public static IReadOnlyList<TemplateSearchMethod> SupportedMethods { get; } = [
        new("SqDiffNormed", "标准差匹配"),
        new("CCorrNormed", "标准相关匹配"),
        new("CCoeffNormed", "标准相关系数匹配"),
        new("MaskedSqDiffNormed", "透明背景标准差匹配"),
        new("EdgeDetectXY", "XY平均值边缘检测"),
        new("EdgeDetectLaplacian", "拉普拉斯边缘检测")
    ];

    public static MatchResult Match(FrameSnapshot frame, FrameSnapshot template, ImageLabel label)
    {
        ValidateOptions(label);
        if (!double.IsFinite(label.Threshold) || label.Threshold < 0 || label.Threshold > 1)
            throw new ArgumentOutOfRangeException(nameof(label), "匹配阈值须在 0–1 之间。");
        using var image = ImageTools.Decode(frame.Png);
        using var target = ImageTools.Decode(template.Png);
        var roi = label.SearchRegion ?? new PixelRegion { Width = image.Width, Height = image.Height };
        ImageTools.ValidateRegion(roi, image.Width, image.Height);
        if (target.Width > roi.Width || target.Height > roi.Height)
            throw new InvalidOperationException("目标图片大于搜索区域，请扩大识图区域或重新截取标签。");
        using var search = new Mat(image, ImageTools.ToRect(roi));
        using var source = Prepare(search, label);
        using var needle = Prepare(target, label);
        using var mask = new Mat();
        bool masked = label.SearchMethod == "MaskedSqDiffNormed";
        bool squaredDifference = label.SearchMethod is "SqDiffNormed" or "MaskedSqDiffNormed";
        bool coefficient = label.SearchMethod is "CCoeffNormed" or "EdgeDetectXY" or "EdgeDetectLaplacian";
        if (masked)
        {
            if (target.Channels() != 4)
                throw new InvalidOperationException("透明背景匹配需要带 Alpha 通道的 PNG 标签图片。");
            Cv2.ExtractChannel(target, mask, 3);
            Cv2.Threshold(mask, mask, 0, 255, ThresholdTypes.Binary);
            if (Cv2.CountNonZero(mask) == 0)
                throw new InvalidOperationException("标签图片完全透明，没有可用于匹配的像素。");
        }
        Scalar mean, deviation;
        if (masked) Cv2.MeanStdDev(needle, out mean, out deviation, mask);
        else Cv2.MeanStdDev(needle, out mean, out deviation);
        double variance = SquaredChannels(deviation);
        if (coefficient && variance < 0.01)
            throw new InvalidOperationException("标签图片为纯色或预处理后没有细节，无法可靠匹配。请框选包含文字、边缘或图案的区域。");
        if (!coefficient && variance + SquaredChannels(mean) < 0.01)
            throw new InvalidOperationException("标签有效像素全黑，无法进行归一化匹配。请改选包含明暗细节的区域。");
        var mode = squaredDifference ? TemplateMatchModes.SqDiffNormed
            : coefficient ? TemplateMatchModes.CCoeffNormed : TemplateMatchModes.CCorrNormed;
        using var result = new Mat();
        if (masked) Cv2.MatchTemplate(source, needle, result, mode, mask);
        else Cv2.MatchTemplate(source, needle, result, mode);
        Cv2.MinMaxLoc(result, out double minimum, out double maximum, out var minLocation, out var maxLocation);
        double raw = squaredDifference ? minimum : maximum;
        var location = squaredDifference ? minLocation : maxLocation;
        // Normalized masked matching can produce infinities in all-black source windows.
        // Never turn those undefined results into a successful match or invalid bounds.
        var score = double.IsFinite(raw) ? Math.Clamp(squaredDifference ? 1 - raw : raw, 0, 1) : 0;
        if (location.X < 0 || location.Y < 0) location = new(0, 0);
        return new(score, double.IsFinite(raw) && score >= label.Threshold, new PixelRegion
        {
            X = roi.X + location.X, Y = roi.Y + location.Y, Width = needle.Width, Height = needle.Height
        });
    }

    internal static void ValidateOptions(ImageLabel label)
    {
        if (!SupportedMethods.Any(method => method.Id == label.SearchMethod))
            throw new NotSupportedException(label.SearchMethod == "TesserDetect"
                ? "OCR 文字识别尚未接入，请选择图像匹配方法。" : "不支持的搜索方法：" + label.SearchMethod);
    }

    internal static Mat Prepare(Mat source, ImageLabel label)
    {
        var prepared = new Mat();
        try
        {
            bool grayscale = label.Grayscale || label.Binary || label.SearchMethod is "EdgeDetectXY" or "EdgeDetectLaplacian";
            if (grayscale)
            {
                if (source.Channels() == 1) source.CopyTo(prepared);
                else Cv2.CvtColor(source, prepared, source.Channels() == 4 ? ColorConversionCodes.BGRA2GRAY : ColorConversionCodes.BGR2GRAY);
            }
            else if (source.Channels() == 1) Cv2.CvtColor(source, prepared, ColorConversionCodes.GRAY2BGR);
            else if (source.Channels() == 4) Cv2.CvtColor(source, prepared, ColorConversionCodes.BGRA2BGR);
            else source.CopyTo(prepared);
            if (label.GaussianBlur) Cv2.GaussianBlur(prepared, prepared, new Size(5, 5), 1.5);
            if (label.Binary) Cv2.Threshold(prepared, prepared, 127, 255, ThresholdTypes.Binary);
            if (label.SearchMethod == "EdgeDetectXY")
            {
                using var x = new Mat(); using var y = new Mat(); using var combined = new Mat();
                Cv2.Sobel(prepared, x, MatType.CV_16S, 1, 0, -1);
                Cv2.Sobel(prepared, y, MatType.CV_16S, 0, 1, -1);
                Cv2.AddWeighted(x, 0.5, y, 0.5, 0, combined);
                combined.ConvertTo(prepared, MatType.CV_8U);
            }
            else if (label.SearchMethod == "EdgeDetectLaplacian")
            {
                using var blurred = new Mat(); using var laplacian = new Mat(); using var absolute = new Mat();
                Cv2.GaussianBlur(prepared, blurred, new Size(5, 5), 1.5);
                Cv2.Laplacian(blurred, laplacian, MatType.CV_16S, 3);
                Cv2.ConvertScaleAbs(laplacian, absolute);
                Cv2.Threshold(absolute, prepared, 30, 255, ThresholdTypes.Binary);
            }
            return prepared;
        }
        catch { prepared.Dispose(); throw; }
    }

    private static double SquaredChannels(Scalar value) => value.Val0 * value.Val0 + value.Val1 * value.Val1 + value.Val2 * value.Val2;
}
