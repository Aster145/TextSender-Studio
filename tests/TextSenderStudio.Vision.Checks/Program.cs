using System.Drawing;
using System.Drawing.Imaging;
using TextSenderStudio.Core;
using TextSenderStudio.Vision;

var checks = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    checks++;
}
void Throws<T>(Action action, string description) where T : Exception
{
    try { action(); } catch (T) { checks++; return; }
    throw new Exception(description);
}
FrameSnapshot BuildImage(int width, int height, bool constant = false)
{
    using var bitmap = new Bitmap(width, height);
    var random = new Random(1703);
    for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            bitmap.SetPixel(x, y, constant ? Color.FromArgb(40, 70, 90) : Color.FromArgb(random.Next(256), random.Next(256), random.Next(256)));
    using var png = new MemoryStream();
    bitmap.Save(png, ImageFormat.Png);
    return new FrameSnapshot(png.ToArray(), width, height, DateTimeOffset.UtcNow);
}

var frame = BuildImage(80, 60);
var template = ImageTools.Crop(frame, new PixelRegion { X = 23, Y = 19, Width = 17, Height = 13 });
Check(template.Width == 17 && template.Height == 13, "crop dimensions");
Check(template.CapturedAt == frame.CapturedAt, "crop timestamp");
var label = new ImageLabel { Threshold = .99 };
var matched = TemplateMatcher.Match(frame, template, label);
Check(matched.IsMatch && matched.Score > .999, "exact match score");
Check(matched.Bounds.X == 23 && matched.Bounds.Y == 19 && matched.Bounds.Width == 17 && matched.Bounds.Height == 13, "absolute match location");
label.SearchRegion = new PixelRegion { X = 20, Y = 15, Width = 22, Height = 20 };
matched = TemplateMatcher.Match(frame, template, label);
Check(matched.IsMatch && matched.Bounds.X == 23 && matched.Bounds.Y == 19, "ROI preserves global location");
label.Grayscale = true;
Check(TemplateMatcher.Match(frame, template, label).IsMatch, "grayscale matching");
label.SearchRegion = new PixelRegion { X = 0, Y = 0, Width = 17, Height = 13 };
Check(!TemplateMatcher.Match(frame, template, label).IsMatch, "excluded region cannot match");
label.SearchRegion = new PixelRegion { Width = 2, Height = 2 };
Throws<InvalidOperationException>(() => TemplateMatcher.Match(frame, template, label), "oversized template rejected");
label.SearchRegion = null;
label.Threshold = double.NaN;
Throws<ArgumentOutOfRangeException>(() => TemplateMatcher.Match(frame, template, label), "NaN threshold rejected");
label.Threshold = 1.01;
Throws<ArgumentOutOfRangeException>(() => TemplateMatcher.Match(frame, template, label), "high threshold rejected");
label.Threshold = .9;
Throws<InvalidOperationException>(() => TemplateMatcher.Match(frame, BuildImage(10, 10, true), label), "constant template rejected");
Throws<InvalidDataException>(() => ImageTools.FromEncoded([]), "empty image rejected");
Throws<ArgumentOutOfRangeException>(() => ImageTools.Crop(frame, new PixelRegion { X = -1, Width = 1, Height = 1 }), "negative frame region rejected");
Throws<ArgumentOutOfRangeException>(() => ImageTools.Crop(frame, new PixelRegion { Width = 0, Height = 1 }), "empty crop rejected");
Throws<ArgumentOutOfRangeException>(() => ImageTools.ValidateRegion(new PixelRegion { X = 1, Width = int.MaxValue, Height = 1 }, 80, 60), "overflow region rejected");
CaptureService.ValidateDesktopRegion(new Rectangle(-1800, 30, 600, 300), new Rectangle(-1920, -200, 3840, 1280));
checks++;
Throws<ArgumentOutOfRangeException>(() => CaptureService.ValidateDesktopRegion(new Rectangle(-1921, 0, 100, 100), new Rectangle(-1920, 0, 3840, 1080)), "outside virtual desktop rejected");
var copy = frame.Copy();
copy.Png[0] = 0;
Check(frame.Png[0] != 0, "frame clones own bytes");
var testPath = Path.Combine(AppContext.BaseDirectory, "test-artifacts", "图像标签.png");
ImageTools.Save(template, testPath);
var loaded = ImageTools.Load(testPath);
Check(loaded.Width == 17 && loaded.Height == 13, "Unicode file PNG round trip");
Check(TemplateMatcher.Match(frame, loaded, label).IsMatch, "saved template reusable");
await using var service = new CaptureService();
Check(service.GetLatestFrame() is null && !service.IsRunning, "no capture starts implicitly");
await service.StopAsync();
await service.StopAsync();
Check(service.GetLatestFrame() is null, "idle stop is idempotent");
using var canceled = new CancellationTokenSource();canceled.Cancel();
try { await service.StartAsync(new CaptureSettings(), canceled.Token); throw new Exception("canceled start accepted"); }
catch (OperationCanceledException) { checks++; }
LabelAlgorithmsChecks.Run(Check);
Console.WriteLine($"Vision checks passed: {checks}. Synthetic images only; no camera or screen capture started.");
