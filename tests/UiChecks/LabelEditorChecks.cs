using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TextSenderStudio;
using TextSenderStudio.Core;
using TextSenderStudio.Vision;

internal static class LabelEditorChecks
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run(MainWindow main, Action<bool, string> check, Action<Window, string> capture)
    {
        var type = typeof(MainWindow).Assembly.GetType("TextSenderStudio.LabelEditorWindow")!;
        var frame = SyntheticFrame();
        FrameSnapshot? current = null;
        var input = new ImageLabel { Name = "标签编辑检查", Grayscale = false };
        var project = (StudioProject)typeof(MainWindow).GetField("_project", Hidden)!.GetValue(main)!;
        string directory = (string)typeof(MainWindow).GetField("_directory", Hidden)!.GetValue(main)!;
        var store = new ProjectStore(App.DataRoot); var originalLabels = project.Labels.ToArray();
        var tabs = (TabControl)main.FindName("MainTabs"); int originalTab = tabs.SelectedIndex;
        var labels = (ListBox)main.FindName("LabelsList"); var originalSelection = labels.SelectedItem;
        var originalFrame = typeof(MainWindow).GetField("_frame", Hidden)!.GetValue(main);
        var windows = new List<Window>();
        Window Create(ImageLabel label, FrameSnapshot? source, FrameSnapshot? template)
        {
            var editor = (Window)Activator.CreateInstance(type, main, label, source, template,
                new Func<FrameSnapshot?>(() => current), new Func<string, bool>(name => name != "重复"))!;
            editor.Left = -20000; editor.Top = -20000; editor.WindowStartupLocation = WindowStartupLocation.Manual; editor.ShowActivated = false;
            windows.Add(editor); return editor;
        }
        object? Call(Window editor, string name, params object?[] arguments) => type.GetMethod(name, Hidden)!.Invoke(editor, arguments);
        T Field<T>(Window editor, string name) => (T)type.GetField(name, Hidden)!.GetValue(editor)!;
        ImageLabel Result(Window editor) => (ImageLabel)type.GetProperty("Result")!.GetValue(editor)!;
        try
        {
            var empty = Create(new ImageLabel { Name = "无画面" }, null, null); empty.Show(); Pump();
            check(!Field<Button>(empty, "_rangeButton").IsEnabled && !Field<Button>(empty, "_targetButton").IsEnabled
                && !(bool)Call(empty, "CommitDraft")!, "label editor opens without capture and requires an image and target before saving");
            Call(empty, "CaptureImage");
            check(Field<TextBlock>(empty, "_status").Text.Contains("暂无预览"), "screenshot reports missing preview without opening a capture device");
            string imagePath = Path.Combine(App.DataRoot, "label-editor-source.png"); ImageTools.Save(frame, imagePath);
            Call(empty, "SetSource", ImageTools.Load(imagePath));
            check(Field<Button>(empty, "_targetButton").IsEnabled, "opening an image enables dual-ROI selection without a capture card");
            empty.Close();

            var editor = Create(input, frame, null); editor.Show(); Pump();
            var view = Field<LabelSelectionView>(editor, "_view");
            var rangeFields = Field<TextBox[]>(editor, "_rangeFields"); var targetFields = Field<TextBox[]>(editor, "_targetFields");
            var method = Field<ComboBox>(editor, "_method");
            check(method.Items.Count == 6 && !Descendants<Button>(editor).Any(button => button.Content?.ToString() == "动态测试"),
                "editor lists six implemented image methods and does not expose unsupported dynamic testing");
            Call(editor, "ToggleSelection", LabelSelectionMode.Range);
            typeof(LabelSelectionView).GetMethod("CommitSelection", Hidden)!.Invoke(view, [new Point(100, 60), new Point(700, 390)]);
            check(Result(editor).SearchRegion is { X: 100, Y: 60, Width: 600, Height: 330 }
                && Field<Button>(editor, "_rangeButton").Content?.ToString() == "确定范围", "range selection updates its own coordinates and waits for confirmation");
            Call(editor, "ToggleSelection", LabelSelectionMode.Range);
            check(view.SelectionMode == LabelSelectionMode.None && view.RangeRegion is { Width: 600 }, "confirming range ends selection while retaining its overlay");
            Call(editor, "ToggleSelection", LabelSelectionMode.Target);
            typeof(LabelSelectionView).GetMethod("CommitSelection", Hidden)!.Invoke(view, [new Point(220, 140), new Point(580, 300)]);
            Call(editor, "ToggleSelection", LabelSelectionMode.Target);
            check(Result(editor).TargetRegion is { X: 220, Y: 140, Width: 360, Height: 160 }
                && Result(editor).SearchRegion is { Width: 600 }, "target selection remains independent from the search rectangle");
            check(Field<Image>(editor, "_targetPreview").Source is not null && Field<Image>(editor, "_rangePreview").Source is not null,
                "both target and search-range image previews render");
            targetFields[0].Text = "230"; targetFields[2].Text = "350";
            check((bool)Call(editor, "ApplyCoordinates", false)! && Result(editor).TargetRegion is { X: 230, Width: 350 }
                && ((FrameSnapshot)type.GetProperty("TemplateFrame")!.GetValue(editor)!).Width == 350,
                "numeric target edits recrop the raw template and move the overlay");
            rangeFields[2].Text = "9999";
            check(!(bool)Call(editor, "ApplyCoordinates", true)! && Result(editor).SearchRegion is { Width: 600 },
                "out-of-bounds coordinate edit is rejected without replacing the valid region");
            rangeFields[2].Text = "600"; Call(editor, "ApplyCoordinates", true);
            var menu = Field<Button>(editor, "_parameters").ContextMenu!;
            menu.Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == "灰度").IsChecked = true;
            menu.Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == "高斯模糊").IsChecked = true;
            check(Result(editor).Grayscale && Result(editor).GaussianBlur && menu.Items.Count == 3,
                "preprocessing supports multiple enabled options and omits unimplemented Other");
            check(input.Name == "标签编辑检查" && input.TargetRegion is null && !input.Grayscale && !input.GaussianBlur,
                "editor changes stay isolated from the input label until saved");
            Await((Task)Call(editor, "TestAsync")!);
            check(Field<TextBlock>(editor, "_status").Text.Contains("静态图片测试")
                && Field<TextBlock>(editor, "_status").Text.Contains("达到阈值"), "label test matches the loaded source when there is no live preview");
            current = frame;
            Await((Task)Call(editor, "TestAsync")!);
            check(Field<TextBlock>(editor, "_status").Text.Contains("当前预览测试"), "label test reads the latest supplied preview snapshot when available");
            Field<TextBox>(editor, "_name").Text = "重复";
            check(!(bool)Call(editor, "CommitDraft")!, "save refuses duplicate label names");
            Field<TextBox>(editor, "_name").Text = "标签编辑检查";
            check((bool)Call(editor, "CommitDraft")!, "valid label draft is ready to save");
            editor.UpdateLayout(); capture(editor, "label-editor-1160.png");
            editor.Width = 1000; editor.Height = 740; Pump();
            var saveButton = Descendants<Button>(editor).Single(button => button.Content?.ToString() == "保存标签");
            var savePosition = saveButton.TranslatePoint(new Point(), editor);
            check(savePosition.Y >= 0 && savePosition.Y + saveButton.ActualHeight <= editor.ActualHeight - 16
                && view.ActualHeight >= 230, "image viewport and save action stay usable at minimum editor size");
            check(targetFields.Concat(rangeFields).All(field =>
            {
                var position = field.TranslatePoint(new Point(), editor);
                return position.X >= 0 && position.X + field.ActualWidth <= editor.ActualWidth;
            }), "both coordinate groups remain within the minimum-width editor");
            capture(editor, "label-editor-1000.png");

            typeof(MainWindow).GetMethod("PersistLabelEditor", Hidden)!.Invoke(main, [null, editor]);
            var saved = project.Labels.Single(label => label.Name == "标签编辑检查");
            check(saved.TargetRegion is { X: 230, Width: 350 } && saved.SearchRegion is { Width: 600 }
                && saved.GaussianBlur && saved.Grayscale && saved.SourceImagePath is not null,
                "saving preserves method, preprocessing, both ROIs and source-image metadata");
            check(ImageTools.Load(store.Resolve(directory, saved.ImagePath)).Width == 350
                && ImageTools.Load(store.Resolve(directory, saved.SourceImagePath!)).Width == frame.Width,
                "project stores cropped template separately from the complete source image");
            var bundle = typeof(MainWindow).GetMethod("CreateLabelBundle", Hidden)!.Invoke(main, [saved])!;
            ((ImageLabel)bundle.GetType().GetProperty("Label")!.GetValue(bundle)!).Name = "标签导入检查";
            string bundlePath = Path.Combine(App.DataRoot, "label-roundtrip.tslabel");
            File.WriteAllText(bundlePath, JsonSerializer.Serialize(bundle, bundle.GetType(), ProjectStore.Json));
            var imported = (ImageLabel)typeof(MainWindow).GetMethod("ImportLabelFile", Hidden)!.Invoke(main, [bundlePath])!;
            check(imported.SourceImagePath is not null && imported.TargetRegion is { Width: 350 } && imported.SearchRegion is { Width: 600 }
                && imported.Grayscale && imported.GaussianBlur && imported.ImagePath != saved.ImagePath,
                "export/import round trip preserves editable source and parameters under new project-local paths");
            string legacyPath = Path.Combine(App.DataRoot, "legacy-label.tslabel");
            var legacy = new ImageLabel { Name = "旧标签检查", SearchRegion = new() { X = 100, Y = 60, Width = 600, Height = 330 } };
            ProjectStore.Write(legacyPath, new { Label = legacy, PngBase64 = Convert.ToBase64String(ImageTools.Crop(frame, saved.TargetRegion!).Png) });
            var old = (ImageLabel)typeof(MainWindow).GetMethod("ImportLabelFile", Hidden)!.Invoke(main, [legacyPath])!;
            check(old.SourceImagePath is null && old.TargetRegion is null && old.SearchRegion is { Width: 600 },
                "legacy label bundles remain readable without source-image fields");
            var legacyTemplate = ImageTools.Load(store.Resolve(directory, old.ImagePath));
            var oldEditor = Create(old, null, legacyTemplate); oldEditor.Show(); Pump();
            Field<TextBox>(oldEditor, "_name").Text = "旧标签已改名";
            check((bool)Call(oldEditor, "CommitDraft")! && type.GetProperty("SourceFrame")!.GetValue(oldEditor) is null
                && ((FrameSnapshot)type.GetProperty("TemplateFrame")!.GetValue(oldEditor)!).Png.SequenceEqual(legacyTemplate.Png),
                "template-only legacy labels can edit metadata without changing their image or requiring a capture");
            oldEditor.Close(); editor.Close();

            int count = project.Labels.Count; Exception? automationError = null;
            typeof(MainWindow).GetField("_frame", Hidden)!.SetValue(main, frame);
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                var dialog = Application.Current.Windows.Cast<Window>().FirstOrDefault(candidate => candidate.GetType() == type && candidate.IsVisible);
                if (dialog is null) return; timer.Stop();
                try
                {
                    check((bool)typeof(MainWindow).GetField("_labelEditorOpen", Hidden)!.GetValue(main)!, "main window holds the editor lease during modal label editing");
                    Descendants<Button>(dialog).Single(button => button.Content?.ToString() == "取消")
                        .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                }
                catch (Exception error) { automationError = error; dialog.Close(); }
            };
            timer.Start(); typeof(MainWindow).GetMethod("NewLabelClick", Hidden)!.Invoke(main, [main, new RoutedEventArgs()]); timer.Stop();
            if (automationError is not null) throw automationError;
            check(project.Labels.Count == count && !(bool)typeof(MainWindow).GetField("_labelEditorOpen", Hidden)!.GetValue(main)!,
                "canceling the main label flow releases its lease and leaves project labels unchanged");
        }
        finally
        {
            foreach (var window in windows) if (window.IsVisible) window.Close();
            typeof(MainWindow).GetField("_frame", Hidden)!.SetValue(main, originalFrame);
            project.Labels.Clear(); project.Labels.AddRange(originalLabels); store.Save(directory, project);
            typeof(MainWindow).GetMethod("RefreshResources", Hidden)!.Invoke(main, null);
            labels.SelectedItem = originalSelection; tabs.SelectedIndex = originalTab;
        }
    }

    private static void Await(Task task) { var watch = Stopwatch.StartNew(); while (!task.IsCompleted && watch.ElapsedMilliseconds < 5000) Pump(); if (!task.IsCompleted) throw new TimeoutException("Label test did not finish."); task.GetAwaiter().GetResult(); }
    private static void Pump()
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index); if (child is T found) yield return found;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static FrameSnapshot SyntheticFrame()
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.AliceBlue, null, new Rect(0, 0, 800, 450));
            drawing.DrawRoundedRectangle(Brushes.White, new Pen(Brushes.SteelBlue, 3), new Rect(220, 140, 360, 160), 12, 12);
            drawing.DrawEllipse(Brushes.Teal, null, new Point(270, 220), 22, 22);
            drawing.DrawText(new FormattedText("READY", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 38, Brushes.DarkSlateBlue, 1), new Point(318, 191));
        }
        var bitmap = new RenderTargetBitmap(800, 450, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = new MemoryStream(); encoder.Save(stream);
        return new(stream.ToArray(), 800, 450, DateTimeOffset.Now);
    }
}
