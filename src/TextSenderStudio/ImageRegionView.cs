using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using TextSenderStudio.Core;
using TextSenderStudio.Vision;

namespace TextSenderStudio;

public sealed class RegionChangedEventArgs(PixelRegion region) : EventArgs { public PixelRegion Region { get; } = region; }
public sealed class ImageRegionView : UserControl
{
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly Canvas _overlay = new() { Background = Brushes.Transparent, ClipToBounds = true };
    private readonly Rectangle _outline = new() { StrokeThickness = 2, Stroke = new SolidColorBrush(Color.FromRgb(12, 93, 119)), Fill = new SolidColorBrush(Color.FromArgb(30, 12, 93, 119)), IsHitTestVisible = false };
    private readonly TextBlock _empty = new() { Text = "暂无画面", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private FrameSnapshot? _frame;
    private PixelRegion? _region;
    private Point? _start;
    public bool SelectionEnabled { get; set; }
    public event EventHandler<RegionChangedEventArgs>? RegionChanged;
    public PixelRegion? Region { get => _region; set { _region = value; Draw(); } }
    public ImageRegionView()
    {
        var grid = new Grid { Background = new SolidColorBrush(Color.FromRgb(234, 241, 245)) };
        grid.Children.Add(_image); grid.Children.Add(_empty); grid.Children.Add(_overlay); _overlay.Children.Add(_outline); Content = grid;
        SizeChanged += (_, _) => Draw();
        _overlay.MouseLeftButtonDown += (_, e) => { if (!SelectionEnabled || _frame == null) return; _start = ToPixel(e.GetPosition(_overlay)); _overlay.CaptureMouse(); e.Handled = true; };
        _overlay.MouseMove += (_, e) => { if (_start != null) UpdateRegion(e.GetPosition(_overlay)); };
        _overlay.MouseLeftButtonUp += (_, e) => { if (_start == null) return; UpdateRegion(e.GetPosition(_overlay)); _start = null; _overlay.ReleaseMouseCapture(); SelectionEnabled = false; if (_region != null) RegionChanged?.Invoke(this, new(_region)); };
        _overlay.LostMouseCapture += (_, _) => _start = null;
    }
    public static BitmapSource Bitmap(byte[] png)
    {
        using var stream = new MemoryStream(png); var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze(); return image;
    }
    public void SetFrame(FrameSnapshot frame) { _frame = frame; _image.Source = Bitmap(frame.Png); _empty.Visibility = Visibility.Collapsed; Draw(); }
    private (double Scale, double Left, double Top) Fit()
    {
        if (_frame == null) return (1, 0, 0);
        var scale = Math.Min(ActualWidth / _frame.Width, ActualHeight / _frame.Height);
        return (scale, (ActualWidth - _frame.Width * scale) / 2, (ActualHeight - _frame.Height * scale) / 2);
    }
    private Point ToPixel(Point p)
    {
        var fit = Fit(); if (_frame == null || fit.Scale <= 0) return new();
        return new(Math.Clamp((p.X - fit.Left) / fit.Scale, 0, _frame.Width), Math.Clamp((p.Y - fit.Top) / fit.Scale, 0, _frame.Height));
    }
    private void UpdateRegion(Point point)
    {
        if (_start is not { } start || _frame == null) return; var end = ToPixel(point);
        var x = Math.Min(_frame.Width - 1, (int)Math.Min(start.X, end.X)); var y = Math.Min(_frame.Height - 1, (int)Math.Min(start.Y, end.Y));
        _region = new() { X = x, Y = y, Width = Math.Clamp((int)Math.Abs(end.X - start.X), 1, _frame.Width - x), Height = Math.Clamp((int)Math.Abs(end.Y - start.Y), 1, _frame.Height - y) }; Draw();
    }
    private void Draw()
    {
        _outline.Visibility = _frame != null && _region != null ? Visibility.Visible : Visibility.Collapsed;
        if (_frame == null || _region == null) return; var fit = Fit();
        Canvas.SetLeft(_outline, fit.Left + _region.X * fit.Scale); Canvas.SetTop(_outline, fit.Top + _region.Y * fit.Scale); _outline.Width = _region.Width * fit.Scale; _outline.Height = _region.Height * fit.Scale;
    }
}
