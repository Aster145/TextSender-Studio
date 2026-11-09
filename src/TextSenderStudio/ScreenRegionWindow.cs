using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using TextSenderStudio.Core;

namespace TextSenderStudio;

internal sealed class ScreenRegionWindow : Window
{
    [StructLayout(LayoutKind.Sequential)] private struct PointNative { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out PointNative point);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    public PixelRegion? Region { get; private set; }
    public ScreenRegionWindow()
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = new SolidColorBrush(Color.FromArgb(50, 0, 0, 0)); Topmost = true; ShowInTaskbar = false; Cursor = Cursors.Cross; ResizeMode = ResizeMode.NoResize;
        var canvas = new Canvas(); Content = canvas; var outline = new Rectangle { Stroke = Brushes.Cyan, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(30, 0, 255, 255)) }; canvas.Children.Add(outline);
        var hint = new TextBlock { Text = "拖动选择屏幕采集范围 · Esc 取消", Background = Brushes.White, Foreground = Brushes.Black, Padding = new(16), FontSize = 16 }; Canvas.SetLeft(hint, 24); Canvas.SetTop(hint, 24); canvas.Children.Add(hint);
        PointNative? start = null;
        SourceInitialized += (_, _) => SetWindowPos(new WindowInteropHelper(this).Handle, new nint(-1), GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79), 0x0040);
        MouseLeftButtonDown += (_, _) => { GetCursorPos(out var position); start = position; CaptureMouse(); };
        MouseMove += (_, _) =>
        {
            if (start is not { } first) return; GetCursorPos(out var end);
            Region = new() { X = Math.Min(first.X, end.X), Y = Math.Min(first.Y, end.Y), Width = Math.Max(1, Math.Abs(end.X - first.X)), Height = Math.Max(1, Math.Abs(end.Y - first.Y)) };
            var a = PointFromScreen(new(Region.X, Region.Y)); var b = PointFromScreen(new(Region.X + Region.Width, Region.Y + Region.Height)); Canvas.SetLeft(outline, a.X); Canvas.SetTop(outline, a.Y); outline.Width = b.X - a.X; outline.Height = b.Y - a.Y;
        };
        MouseLeftButtonUp += (_, _) => { ReleaseMouseCapture(); if (Region != null && Region.Width > 1 && Region.Height > 1) DialogResult = true; };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) DialogResult = false; };
    }
}
