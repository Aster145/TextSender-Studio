using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TextSenderStudio.Core;
using TextSenderStudio.Vision;

namespace TextSenderStudio;

public enum LabelSelectionMode { None, Range, Target }
public sealed class LabelSelectionChangedEventArgs(LabelSelectionMode mode, PixelRegion region) : EventArgs
{
    public LabelSelectionMode Mode { get; } = mode;
    public PixelRegion Region { get; } = region;
}

/// <summary>WPF counterpart of EasyCon's dual-ROI image selector, with zoom and pan.</summary>
public sealed class LabelSelectionView : FrameworkElement
{
    private FrameSnapshot? _frame;
    private BitmapSource? _image;
    private PixelRegion? _range, _target;
    private LabelSelectionMode _mode;
    private Point _offset, _dragStart, _dragCurrent, _panStart;
    private double _zoom = 1;
    private bool _selecting, _panning, _fit = true;
    public event EventHandler<LabelSelectionChangedEventArgs>? SelectionChanged;
    public PixelRegion? RangeRegion { get => _range; set { _range = value; InvalidateVisual(); } }
    public PixelRegion? TargetRegion { get => _target; set { _target = value; InvalidateVisual(); } }
    public LabelSelectionMode SelectionMode
    {
        get => _mode;
        set { _mode = value; _selecting = _panning = false; ReleaseMouseCapture(); Cursor = value == LabelSelectionMode.None ? Cursors.Hand : Cursors.Cross; InvalidateVisual(); }
    }
    public LabelSelectionView()
    {
        ClipToBounds = true; Focusable = true; Cursor = Cursors.Hand;
        SizeChanged += (_, _) => { if (_fit) FitToView(); else InvalidateVisual(); };
    }
    public void SetFrame(FrameSnapshot? frame)
    {
        _frame = frame; _image = frame is null ? null : ImageRegionView.Bitmap(frame.Png);
        _fit = true; FitToView(); InvalidateVisual();
    }
    public void FitToView()
    {
        _fit = true;
        if (_frame is null || ActualWidth <= 0 || ActualHeight <= 0) return;
        _zoom = Math.Min(ActualWidth / _frame.Width, ActualHeight / _frame.Height) * .9;
        _offset = new((ActualWidth - _frame.Width * _zoom) / 2, (ActualHeight - _frame.Height * _zoom) / 2);
        InvalidateVisual();
    }
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e); if (_frame is null || _zoom <= 0) return;
        var mouse = e.GetPosition(this); double previous = _zoom;
        _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1), .05, 50);
        double ratio = _zoom / previous;
        _offset = new(mouse.X - (mouse.X - _offset.X) * ratio, mouse.Y - (mouse.Y - _offset.Y) * ratio);
        _fit = false; InvalidateVisual(); e.Handled = true;
    }
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e); if (_frame is null) return;
        if (e.ChangedButton == MouseButton.Right && _mode != LabelSelectionMode.None)
        {
            _selecting = true; _dragStart = _dragCurrent = ToImage(e.GetPosition(this)); CaptureMouse(); e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Left && _mode == LabelSelectionMode.None)
        {
            _panning = true; _panStart = e.GetPosition(this); CaptureMouse(); e.Handled = true;
        }
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e); if (_frame is null) return;
        var point = e.GetPosition(this);
        if (_selecting) { _dragCurrent = ToImage(point); InvalidateVisual(); e.Handled = true; }
        else if (_panning) { _offset += point - _panStart; _panStart = point; _fit = false; InvalidateVisual(); e.Handled = true; }
    }
    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (_selecting && e.ChangedButton == MouseButton.Right)
        {
            _dragCurrent = ToImage(e.GetPosition(this)); CommitSelection(_dragStart, _dragCurrent); e.Handled = true;
        }
        _selecting = _panning = false; ReleaseMouseCapture(); InvalidateVisual();
    }
    protected override void OnLostMouseCapture(MouseEventArgs e) { base.OnLostMouseCapture(e); _selecting = _panning = false; InvalidateVisual(); }
    private Point ToImage(Point point) => _frame is null || _zoom <= 0 ? new() : new(
        Math.Clamp((point.X - _offset.X) / _zoom, 0, _frame.Width), Math.Clamp((point.Y - _offset.Y) / _zoom, 0, _frame.Height));
    private PixelRegion? Region(Point start, Point end)
    {
        if (_frame is null || Math.Abs(start.X - end.X) < 1 || Math.Abs(start.Y - end.Y) < 1) return null;
        int x = Math.Clamp((int)Math.Floor(Math.Min(start.X, end.X)), 0, _frame.Width - 1);
        int y = Math.Clamp((int)Math.Floor(Math.Min(start.Y, end.Y)), 0, _frame.Height - 1);
        return new() { X = x, Y = y, Width = Math.Clamp((int)Math.Ceiling(Math.Max(start.X, end.X)) - x, 1, _frame.Width - x), Height = Math.Clamp((int)Math.Ceiling(Math.Max(start.Y, end.Y)) - y, 1, _frame.Height - y) };
    }
    private void CommitSelection(Point start, Point end)
    {
        var region = Region(start, end); if (region is null || _mode == LabelSelectionMode.None) return;
        if (_mode == LabelSelectionMode.Range) RangeRegion = region; else TargetRegion = region;
        SelectionChanged?.Invoke(this, new(_mode, region));
    }
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing); drawing.DrawRectangle(TryFindResource("PageBrush") as Brush ?? Brushes.WhiteSmoke, null, new Rect(RenderSize));
        if (_image is null || _frame is null)
        {
            var text = Text("打开图片或从当前预览截图", TryFindResource("MutedBrush") as Brush ?? Brushes.DimGray);
            drawing.DrawText(text, new(Math.Max(8, (ActualWidth - text.Width) / 2), Math.Max(8, (ActualHeight - text.Height) / 2))); return;
        }
        drawing.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        drawing.DrawImage(_image, new Rect(_offset.X, _offset.Y, _frame.Width * _zoom, _frame.Height * _zoom));
        DrawRegion(drawing, _range, Brushes.Firebrick, "搜索范围", false);
        DrawRegion(drawing, _target, Brushes.ForestGreen, "目标", false);
        if (_selecting) DrawRegion(drawing, Region(_dragStart, _dragCurrent), _mode == LabelSelectionMode.Range ? Brushes.Firebrick : Brushes.ForestGreen, "", true);
        drawing.Pop();
    }
    private void DrawRegion(DrawingContext drawing, PixelRegion? region, Brush brush, string label, bool dashed)
    {
        if (region is null) return;
        var rect = new Rect(_offset.X + region.X * _zoom, _offset.Y + region.Y * _zoom, region.Width * _zoom, region.Height * _zoom);
        var pen = new Pen(brush, 2) { DashStyle = dashed ? DashStyles.Dash : DashStyles.Solid };
        drawing.DrawRectangle(null, pen, rect);
        if (label.Length == 0) return;
        var text = Text(label, brush); double top = Math.Max(0, rect.Top - text.Height - 4), left = Math.Max(0, rect.Left);
        drawing.DrawRectangle(Brushes.White, null, new Rect(left, top, text.Width + 8, text.Height + 4));
        drawing.DrawText(text, new(left + 4, top + 2));
    }
    private FormattedText Text(string value, Brush brush) => new(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
        new Typeface("Microsoft YaHei UI"), 12, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
}
