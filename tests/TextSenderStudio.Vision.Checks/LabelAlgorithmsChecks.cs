using System.Text.Json;
using OpenCvSharp;
using TextSenderStudio.Core;
using TextSenderStudio.Vision;
using Point = OpenCvSharp.Point;

internal static class LabelAlgorithmsChecks
{
    public static void Run(Action<bool, string> check)
    {
        void Reject<T>(Action action, string name) where T : Exception
        {
            try { action(); } catch (T) { check(true, name); return; }
            throw new Exception("FAIL: " + name);
        }
        static FrameSnapshot Frame(Mat pixels)
        {
            Cv2.ImEncode(".png", pixels, out var png);
            return ImageTools.FromEncoded(png);
        }
        static Mat Pixels(FrameSnapshot frame) => Cv2.ImDecode(frame.Png, ImreadModes.Unchanged);

        var legacy = JsonSerializer.Deserialize<ImageLabel>("{\"Name\":\"旧标签\",\"Grayscale\":true}")!;
        check(legacy.SearchMethod == "CCoeffNormed" && legacy.Grayscale && !legacy.Binary && !legacy.GaussianBlur
            && legacy.TargetRegion == null && legacy.SourceImagePath == null, "old image labels retain the original matching defaults");
        var metadata = new ImageLabel
        {
            Name = "独立选区", SourceImagePath = "labels/source.png", ImagePath = "labels/target.png",
            TargetRegion = new() { X = 23, Y = 19, Width = 32, Height = 26 },
            SearchRegion = new() { X = 20, Y = 15, Width = 40, Height = 40 },
            SearchMethod = "SqDiffNormed", Grayscale = true, Binary = true, GaussianBlur = true
        };
        var restored = ProjectStore.Clone(metadata);
        check(restored.TargetRegion?.X == 23 && restored.SearchRegion?.X == 20 && restored.SourceImagePath == metadata.SourceImagePath
            && restored.SearchMethod == "SqDiffNormed" && restored.Grayscale && restored.Binary && restored.GaussianBlur,
            "target region source frame search range and combined options persist independently");
        check(TemplateMatcher.SupportedMethods.Select(method => method.Id).SequenceEqual(new[]
        { "SqDiffNormed", "CCorrNormed", "CCoeffNormed", "MaskedSqDiffNormed", "EdgeDetectXY", "EdgeDetectLaplacian" }),
            "only implemented EasyCon image matching methods are offered");

        using var pattern = new Mat(26, 32, MatType.CV_8UC3, Scalar.Black);
        Cv2.Rectangle(pattern, new Rect(7, 6, 18, 14), new Scalar(40, 180, 230), -1);
        Cv2.Rectangle(pattern, new Rect(11, 9, 7, 8), new Scalar(210, 60, 30), -1);
        Cv2.Line(pattern, new Point(8, 18), new Point(23, 7), new Scalar(255, 255, 255), 2);
        using var scene = new Mat(80, 90, MatType.CV_8UC3, Scalar.Black);
        using (var destination = new Mat(scene, new Rect(23, 19, pattern.Width, pattern.Height))) pattern.CopyTo(destination);
        var frame = Frame(scene); var template = Frame(pattern);
        foreach (var method in TemplateMatcher.SupportedMethods.Where(method => method.Id != "MaskedSqDiffNormed"))
        {
            var result = TemplateMatcher.Match(frame, template, new()
            {
                SearchMethod = method.Id, Threshold = .99,
                TargetRegion = new() { X = 999, Y = 999, Width = 1, Height = 1 },
                SearchRegion = new() { X = 20, Y = 15, Width = 40, Height = 40 }
            });
            check(result.IsMatch && result.Score > .999 && result.Bounds.X == 23 && result.Bounds.Y == 19
                && result.Bounds.Width == 32 && result.Bounds.Height == 26,
                "matching method returns correct global bounds without re-cropping target metadata: " + method.Id);
        }
        for (int flags = 0; flags < 8; flags++)
        {
            var label = new ImageLabel { Grayscale = (flags & 1) != 0, Binary = (flags & 2) != 0, GaussianBlur = (flags & 4) != 0, Threshold = .99 };
            var result = TemplateMatcher.Match(template, template, label);
            check(result.IsMatch && result.Score > .999, "preprocessing flags combine consistently for source and template: " + flags);
        }

        using var inverse = new Mat(); pattern.ConvertTo(inverse, MatType.CV_8U, -1, 255);
        var opposite = TemplateMatcher.Match(Frame(inverse), template, new() { Threshold = .1 });
        check(opposite.Score == 0 && !opposite.IsMatch, "default coefficient score preserves Studio clipping instead of shifting negative correlations upward");

        using var colors = new Mat(1, 4, MatType.CV_8UC3);
        colors.Set(0, 0, new Vec3b(0, 0, 0)); colors.Set(0, 1, new Vec3b(255, 255, 255));
        colors.Set(0, 2, new Vec3b(0, 0, 255)); colors.Set(0, 3, new Vec3b(255, 0, 0));
        var colorFrame = Frame(colors); var originalBytes = colorFrame.Png.ToArray();
        var grayFrame = ImageTools.Preprocess(colorFrame, new() { Grayscale = true });
        using var gray = Pixels(grayFrame);
        check(gray.Channels() == 1 && gray.At<byte>(0, 0) == 0 && gray.At<byte>(0, 1) == 255
            && gray.At<byte>(0, 2) == 76 && gray.At<byte>(0, 3) == 29, "grayscale preview applies BGR luminance conversion");
        check(grayFrame.Width == 4 && grayFrame.Height == 1 && grayFrame.CapturedAt == colorFrame.CapturedAt
            && colorFrame.Png.SequenceEqual(originalBytes), "preprocessing keeps dimensions timestamp and original source bytes");
        using var thresholds = new Mat(1, 2, MatType.CV_8UC1);
        thresholds.Set(0, 0, (byte)127); thresholds.Set(0, 1, (byte)128);
        using var binary = Pixels(ImageTools.Preprocess(Frame(thresholds), new() { Binary = true }));
        check(binary.Channels() == 1 && binary.At<byte>(0, 0) == 0 && binary.At<byte>(0, 1) == 255,
            "binary preview uses threshold 127 and automatically converts to gray");
        using var impulse = new Mat(9, 9, MatType.CV_8UC1, Scalar.Black); impulse.Set(4, 4, (byte)255);
        using var blur = Pixels(ImageTools.Preprocess(Frame(impulse), new() { Grayscale = true, GaussianBlur = true }));
        check(blur.At<byte>(4, 4) is > 0 and < 255 && blur.At<byte>(4, 5) > 0, "Gaussian preview actually smooths neighboring pixels");
        using var combined = Pixels(ImageTools.Preprocess(template, new() { Grayscale = true, GaussianBlur = true, Binary = true }));
        bool onlyBinary = true;
        int combinedWidth = combined.Width, combinedHeight = combined.Height;
        for (int y = 0; y < combinedHeight; y++) for (int x = 0; x < combinedWidth; x++) onlyBinary &= combined.At<byte>(y, x) is 0 or 255;
        check(combined.Channels() == 1 && onlyBinary, "combined preprocessing applies binary after blur");

        using var randomScene = new Mat(60, 80, MatType.CV_8UC3);
        var random = new Random(137);
        int sceneWidth = randomScene.Width, sceneHeight = randomScene.Height;
        for (int y = 0; y < sceneHeight; y++) for (int x = 0; x < sceneWidth; x++)
            randomScene.Set(y, x, new Vec3b((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)));
        using var cutout = new Mat(randomScene, new Rect(23, 19, 23, 17));
        using var rgba = new Mat(); Cv2.CvtColor(cutout, rgba, ColorConversionCodes.BGR2BGRA);
        int alphaWidth = rgba.Width, alphaHeight = rgba.Height;
        for (int y = 0; y < alphaHeight; y++) for (int x = 0; x < alphaWidth; x++)
            if (x < 5 || x >= alphaWidth - 5 || y < 4 || y >= alphaHeight - 4) rgba.Set(y, x, new Vec4b(255, 255, 255, 0));
        var alphaTemplate = Frame(rgba);
        var alphaResult = TemplateMatcher.Match(Frame(randomScene), alphaTemplate, new() { SearchMethod = "MaskedSqDiffNormed", Threshold = .99 });
        check(alphaResult.IsMatch && alphaResult.Score > .999 && alphaResult.Bounds.X == 23 && alphaResult.Bounds.Y == 19,
            "transparent matching ignores hidden RGB pixels and locates visible content");
        using var alphaCrop = Pixels(ImageTools.Crop(alphaTemplate, new() { X = 1, Y = 1, Width = 21, Height = 15 }));
        using var alphaPreview = Pixels(ImageTools.Preprocess(alphaTemplate, new() { Grayscale = true, SearchMethod = "MaskedSqDiffNormed" }));
        check(alphaCrop.Channels() == 4 && alphaCrop.At<Vec4b>(0, 0).Item3 == 0 && alphaPreview.Channels() == 4
            && alphaPreview.At<Vec4b>(0, 0).Item3 == 0 && alphaPreview.At<Vec4b>(8, 10).Item3 == 255,
            "crop and processed preview preserve PNG transparency");
        string saved = Path.Combine(Path.GetTempPath(), "TextSender-Vision-Alpha-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            ImageTools.Save(alphaTemplate, saved);
            using var savedAlpha = Pixels(ImageTools.Load(saved));
            check(savedAlpha.Channels() == 4 && savedAlpha.At<Vec4b>(0, 0).Item3 == 0, "transparent source survives saved PNG round trip");
        }
        finally { if (File.Exists(saved)) File.Delete(saved); }
        using var blackScene = new Mat(60, 80, MatType.CV_8UC3, Scalar.Black);
        var noSignal = TemplateMatcher.Match(Frame(blackScene), alphaTemplate, new() { SearchMethod = "MaskedSqDiffNormed" });
        check(double.IsFinite(noSignal.Score) && !noSignal.IsMatch && noSignal.Bounds.X >= 0 && noSignal.Bounds.Y >= 0,
            "masked normalization on black source never reports undefined similarity as a match");
        var noSignalAtZero = TemplateMatcher.Match(Frame(blackScene), alphaTemplate, new() { SearchMethod = "MaskedSqDiffNormed", Threshold = 0 });
        check(noSignalAtZero.Score == 0 && !noSignalAtZero.IsMatch,
            "undefined masked similarity remains unmatched even at a zero threshold");
        using var transparent = new Mat(10, 10, MatType.CV_8UC4, Scalar.All(0));
        Reject<InvalidOperationException>(() => TemplateMatcher.Match(frame, Frame(transparent), new() { SearchMethod = "MaskedSqDiffNormed" }), "fully transparent template is rejected");
        Reject<InvalidOperationException>(() => TemplateMatcher.Match(frame, template, new() { SearchMethod = "MaskedSqDiffNormed" }), "masked method requires an alpha channel");
        using var flat = new Mat(10, 10, MatType.CV_8UC3, new Scalar(90, 90, 90));
        var flatFrame = Frame(flat);
        check(TemplateMatcher.Match(flatFrame, flatFrame, new() { SearchMethod = "SqDiffNormed" }).IsMatch,
            "squared-difference method can match a nonzero constant target");
        foreach (string method in new[] { "CCoeffNormed", "EdgeDetectXY", "EdgeDetectLaplacian" })
            Reject<InvalidOperationException>(() => TemplateMatcher.Match(frame, flatFrame, new() { SearchMethod = method }), "detail-free coefficient or edge template is rejected: " + method);
        using var single = new Mat(1, 1, MatType.CV_8UC3, new Scalar(90, 90, 90));
        var small = ImageTools.Preprocess(Frame(single), new() { GaussianBlur = true });
        check(small.Width == 1 && small.Height == 1, "preprocessing small valid images does not generate invalid matrix dimensions");
        Reject<InvalidOperationException>(() => TemplateMatcher.Match(frame, Frame(single), new() { SearchMethod = "EdgeDetectLaplacian" }), "one-pixel edge template reports insufficient detail");
        Reject<NotSupportedException>(() => TemplateMatcher.Match(frame, template, new() { SearchMethod = "TesserDetect" }), "unintegrated OCR is rejected explicitly");
        Reject<NotSupportedException>(() => ImageTools.Preprocess(template, new() { SearchMethod = "not-real" }), "unknown saved method never silently falls back");
    }
}
