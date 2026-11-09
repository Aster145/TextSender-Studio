using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Win32;
using TextSenderStudio.Core;
using TextSenderStudio.Vision;

namespace TextSenderStudio;

internal sealed class LabelEditorWindow : Window
{
    private readonly Func<FrameSnapshot?> _currentCapture;
    private readonly Func<string, bool> _nameAvailable;
    private readonly LabelSelectionView _view = new();
    private readonly TextBox _name = new();
    private readonly TextBox _threshold = new() { Width = 72 };
    private readonly ComboBox _method = new() { Width = 240, DisplayMemberPath = "Name", SelectedValuePath = "Id" };
    private readonly Button _parameters = new() { Content = "参数 · 默认", MinWidth = 116 };
    private readonly CheckBox _fullRange = new() { Content = "全画面", Margin = new(12, 0, 0, 0) };
    private readonly Button _rangeButton = new() { Content = "圈选范围" };
    private readonly Button _targetButton = new() { Content = "圈选目标" };
    private readonly Button _test = new() { Content = "标签测试" };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _match = new() { Text = "匹配度：—", FontWeight = FontWeights.SemiBold, Margin = new(0, 8, 0, 8) };
    private readonly Image _targetPreview = new() { Height = 72, Stretch = Stretch.Uniform };
    private readonly Image _rangePreview = new() { Height = 72, Stretch = Stretch.Uniform };
    private readonly TextBox[] _targetFields = new TextBox[4], _rangeFields = new TextBox[4];
    private FrameSnapshot? _source, _template;
    private bool _changing, _testing, _closed;
    private int _revision;
    public ImageLabel Result { get; }
    public FrameSnapshot? SourceFrame => _source;
    public FrameSnapshot? TemplateFrame => _template;

    public LabelEditorWindow(Window owner, ImageLabel draft, FrameSnapshot? source, FrameSnapshot? template,
        Func<FrameSnapshot?> currentCapture, Func<string, bool> nameAvailable)
    {
        Owner = owner; Title = "制作图像标签 — TextSender Studio";
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Width = 1160; Height = 880; MinWidth = 1000; MinHeight = 740;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Topmost = owner.Topmost;
        Result = ProjectStore.Clone(draft); _source = source?.Copy(); _template = template?.Copy();
        _currentCapture = currentCapture; _nameAvailable = nameAvailable;
        BuildInterface();
        _changing = true;
        _name.Text = Result.Name; _threshold.Text = (Result.Threshold * 100).ToString("0.##", CultureInfo.CurrentCulture);
        _method.ItemsSource = TemplateMatcher.SupportedMethods; _method.SelectedValue = Result.SearchMethod;
        _fullRange.IsChecked = Result.SearchRegion is null;
        WriteRegion(_targetFields, Result.TargetRegion);
        WriteRegion(_rangeFields, Result.SearchRegion ?? (_source is null ? null : FullFrame(_source)));
        _changing = false;
        _view.SetFrame(_source ?? _template); SyncSelection(); UpdateParameterText(); RefreshPreviews();
        _status.Text = _source is not null ? "右键拖动圈选；滚轮缩放，中键拖动平移。圈选后点击“确定”。"
            : _template is not null ? "此旧标签只保存了目标图。可直接修改名称与参数；打开原图或截图后可重新圈选。"
            : "先打开图片，或点击“截图”读取主程序当前预览。";
        _view.SelectionChanged += SelectionChanged;
        _method.SelectionChanged += (_, _) => OptionsChanged();
        _fullRange.Checked += (_, _) => FullRangeChanged(); _fullRange.Unchecked += (_, _) => FullRangeChanged();
        Closed += (_, _) => _closed = true;
    }

    private void BuildInterface()
    {
        // EasyCon TagEditor flow: source image + action rail, then label/settings,
        // target/range coordinates and two previews. Refactoring UI §2.1/§3.6:
        // image is primary; fields and previews use existing 8/16/24 spacing tokens.
        var root = new DockPanel { Margin = new(24) }; Content = root;
        var heading = new StackPanel { Margin = new(0, 0, 0, 16) };
        heading.Children.Add(new TextBlock { Text = "制作图像标签", FontSize = 22, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(Caption("与 EasyCon 相同：圈选搜索范围，再圈选目标。右键圈选 · 滚轮缩放 · 中键平移", new(0, 8, 0, 0)));
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var footer = new DockPanel { Margin = new(0, 16, 0, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var cancel = new Button { Content = "取消", IsCancel = true }; cancel.Click += (_, _) => DialogResult = false;
        var save = new Button { Content = "保存标签", Style = (Style)Application.Current.FindResource("PrimaryButton"), Margin = new(0) };
        save.Click += (_, _) => { if (CommitDraft()) DialogResult = true; };
        actions.Children.Add(cancel); actions.Children.Add(save); DockPanel.SetDock(actions, Dock.Right); footer.Children.Add(actions);
        _status.Margin = new(0, 0, 24, 0); _status.Style = (Style)Application.Current.FindResource("Caption"); footer.Children.Add(_status);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var body = new Grid(); body.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star), MinHeight = 230 });
        body.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.Children.Add(body);
        var source = new Grid(); source.ColumnDefinitions.Add(new()); source.ColumnDefinitions.Add(new() { Width = new GridLength(132) });
        source.Children.Add(_view);
        var rail = new StackPanel { Margin = new(16, 0, 0, 0) };
        var open = new Button { Content = "打开" }; open.Click += (_, _) => OpenImage();
        var capture = new Button { Content = "截图" }; capture.Click += (_, _) => CaptureImage();
        _rangeButton.Click += (_, _) => ToggleSelection(LabelSelectionMode.Range);
        _targetButton.Click += (_, _) => ToggleSelection(LabelSelectionMode.Target);
        _test.Click += async (_, _) => await TestAsync();
        var fit = new Button { Content = "适应窗口" }; fit.Click += (_, _) => _view.FitToView();
        foreach (var button in new[] { open, capture, _rangeButton, _targetButton, _test, fit }) { button.Margin = new(0, 0, 0, 8); rail.Children.Add(button); }
        rail.Children.Add(Caption("红框：搜索范围\n绿框：目标图片", new(0, 8, 0, 0)));
        Grid.SetColumn(rail, 1); source.Children.Add(rail);
        body.Children.Add(new Border { Style = (Style)Application.Current.FindResource("Panel"), Padding = new(8), Child = source });

        var details = new Grid { Margin = new(0, 16, 0, 0) };
        details.ColumnDefinitions.Add(new()); details.ColumnDefinitions.Add(new() { Width = new GridLength(24) }); details.ColumnDefinitions.Add(new() { Width = new GridLength(208) });
        Grid.SetRow(details, 1); body.Children.Add(details);
        var fields = new StackPanel(); details.Children.Add(fields);
        var nameLine = new DockPanel { Margin = new(0, 0, 0, 12) };
        nameLine.Children.Add(Caption("标签名称", new(0, 0, 12, 0))); _name.MinWidth = 180; nameLine.Children.Add(_name); fields.Children.Add(nameLine);
        var methodLine = new WrapPanel { Margin = new(0, 0, 0, 12) };
        methodLine.Children.Add(Caption("搜索方法", new(0, 0, 12, 0))); methodLine.Children.Add(_method); methodLine.Children.Add(_parameters);
        methodLine.Children.Add(Caption("阈值 %", new(8, 0, 8, 0))); methodLine.Children.Add(_threshold); fields.Children.Add(methodLine);
        BuildParameterMenu();
        var regions = new Grid(); regions.ColumnDefinitions.Add(new()); regions.ColumnDefinitions.Add(new() { Width = new GridLength(16) }); regions.ColumnDefinitions.Add(new());
        regions.Children.Add(RegionFields("目标位置", _targetFields, false)); var range = RegionFields("搜索范围", _rangeFields, true); Grid.SetColumn(range, 2); regions.Children.Add(range); fields.Children.Add(regions);
        var previews = new StackPanel(); Grid.SetColumn(previews, 2); details.Children.Add(previews);
        previews.Children.Add(Caption("目标图", new(0, 0, 0, 4))); previews.Children.Add(PreviewBorder(_targetPreview));
        previews.Children.Add(_match); previews.Children.Add(Caption("搜索范围图", new(0, 0, 0, 4))); previews.Children.Add(PreviewBorder(_rangePreview));
    }

    private StackPanel RegionFields(string title, TextBox[] boxes, bool range)
    {
        var panel = new StackPanel(); var heading = new WrapPanel { Margin = new(0, 0, 0, 8) };
        heading.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold }); if (range) heading.Children.Add(_fullRange); panel.Children.Add(heading);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new()); grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new());
        string[] labels = ["X", "Y", "宽", "高"];
        for (int index = 0; index < 4; index++)
        {
            var line = new DockPanel { Margin = new(0, 0, 8, 8) };
            line.Children.Add(new TextBlock { Text = labels[index], Width = 24 });
            var spin = new StackPanel { Width = 18 }; DockPanel.SetDock(spin, Dock.Right); line.Children.Add(spin);
            var box = new TextBox { Text = "0", MinWidth = 48, Padding = new(6, 4, 6, 4), TextAlignment = TextAlignment.Center }; boxes[index] = box; line.Children.Add(box);
            foreach (int delta in new[] { 1, -1 })
            {
                var button = new Button { Content = delta > 0 ? "▴" : "▾", Height = 18, MinHeight = 18, Padding = new(0), Margin = new(0), FontSize = 10 };
                button.Click += (_, _) => { if (!int.TryParse(box.Text, out int value)) value = 0; box.Text = Math.Clamp((long)value + delta, 0, 99999).ToString(); ApplyCoordinates(range); }; spin.Children.Add(button);
            }
            box.LostKeyboardFocus += (_, _) => ApplyCoordinates(range);
            Grid.SetColumn(line, index % 2); Grid.SetRow(line, index / 2); grid.Children.Add(line);
        }
        panel.Children.Add(grid); return panel;
    }

    private void BuildParameterMenu()
    {
        var menu = new ContextMenu();
        void Option(string name, Func<bool> get, Action<bool> set)
        {
            var item = new MenuItem { Header = name, IsCheckable = true, IsChecked = get(), StaysOpenOnClick = true };
            item.Checked += (_, _) => { set(true); OptionsChanged(); }; item.Unchecked += (_, _) => { set(false); OptionsChanged(); }; menu.Items.Add(item);
        }
        Option("灰度", () => Result.Grayscale, value => Result.Grayscale = value);
        Option("二值", () => Result.Binary, value => Result.Binary = value);
        Option("高斯模糊", () => Result.GaussianBlur, value => Result.GaussianBlur = value);
        _parameters.ContextMenu = menu; _parameters.Click += (_, _) => { menu.PlacementTarget = _parameters; menu.Placement = PlacementMode.Bottom; menu.IsOpen = true; };
    }
    private static Border PreviewBorder(Image image) => new() { BorderThickness = new(1), BorderBrush = (Brush)Application.Current.FindResource("LineBrush"), Background = (Brush)Application.Current.FindResource("PageBrush"), CornerRadius = new(6), Child = image, Padding = new(4) };
    private static TextBlock Caption(string text, Thickness margin) => new() { Text = text, Style = (Style)Application.Current.FindResource("Caption"), Margin = margin, TextWrapping = TextWrapping.Wrap };
    private static PixelRegion FullFrame(FrameSnapshot frame) => new() { Width = frame.Width, Height = frame.Height };
    private static void WriteRegion(TextBox[] boxes, PixelRegion? region)
    {
        int[] values = [region?.X ?? 0, region?.Y ?? 0, region?.Width ?? 0, region?.Height ?? 0];
        for (int index = 0; index < boxes.Length; index++) boxes[index].Text = values[index].ToString();
    }
    private static PixelRegion ReadRegion(TextBox[] boxes)
    {
        var values = boxes.Select(box => int.TryParse(box.Text, out int value) ? value : -1).ToArray();
        if (values[0] < 0 || values[1] < 0 || values[2] <= 0 || values[3] <= 0) throw new ArgumentException("选区坐标须为非负整数，宽高须大于 0。");
        return new() { X = values[0], Y = values[1], Width = values[2], Height = values[3] };
    }
    private void OpenImage()
    {
        var picker = new OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp" };
        if (picker.ShowDialog(this) != true) return;
        try { SetSource(ImageTools.Load(picker.FileName)); } catch (Exception error) { _status.Text = error.Message; }
    }
    private void CaptureImage()
    {
        try { SetSource(_currentCapture() ?? throw new InvalidOperationException("暂无预览画面。请先在图像标签页开始预览，或打开本地图片。")); }
        catch (Exception error) { _status.Text = error.Message; }
    }
    private void SetSource(FrameSnapshot frame)
    {
        _source = frame.Copy(); _revision++; _view.SelectionMode = LabelSelectionMode.None;
        if (Result.TargetRegion is not null)
            try { ImageTools.ValidateRegion(Result.TargetRegion, frame.Width, frame.Height); } catch { Result.TargetRegion = null; }
        if (Result.SearchRegion is not null)
            try { ImageTools.ValidateRegion(Result.SearchRegion, frame.Width, frame.Height); } catch { Result.SearchRegion = null; }
        _changing = true; _fullRange.IsChecked = Result.SearchRegion is null;
        WriteRegion(_targetFields, Result.TargetRegion); WriteRegion(_rangeFields, Result.SearchRegion ?? FullFrame(frame)); _changing = false;
        _view.SetFrame(frame); SyncSelection(); RefreshPreviews();
        _status.Text = $"已载入 {frame.Width} × {frame.Height} 图片。右键圈选目标后点击“确定目标”。";
    }
    private void ToggleSelection(LabelSelectionMode mode)
    {
        if (_source is null) return;
        if (_view.SelectionMode == mode)
        {
            if (!ApplyCoordinates(mode == LabelSelectionMode.Range)) return;
            _view.SelectionMode = LabelSelectionMode.None; _status.Text = mode == LabelSelectionMode.Range ? "搜索范围已确定。" : "目标图片已确定。";
        }
        else { _view.SelectionMode = mode; _status.Text = mode == LabelSelectionMode.Range ? "按住鼠标右键拖动，圈选搜索范围，然后点击“确定范围”。" : "按住鼠标右键拖动，圈选目标图片，然后点击“确定目标”。"; }
        SyncSelection();
    }
    private void SelectionChanged(object? sender, LabelSelectionChangedEventArgs e)
    {
        _changing = true;
        if (e.Mode == LabelSelectionMode.Range) { Result.SearchRegion = e.Region; _fullRange.IsChecked = false; WriteRegion(_rangeFields, e.Region); }
        else { Result.TargetRegion = e.Region; WriteRegion(_targetFields, e.Region); }
        _changing = false; _revision++; SyncSelection(); RefreshPreviews();
    }
    private bool ApplyCoordinates(bool range)
    {
        if (_changing) return true;
        try
        {
            if (range && _fullRange.IsChecked == true) Result.SearchRegion = null;
            else
            {
                var region = ReadRegion(range ? _rangeFields : _targetFields);
                if (_source is not null) ImageTools.ValidateRegion(region, _source.Width, _source.Height);
                if (range) Result.SearchRegion = region; else Result.TargetRegion = region;
            }
            _revision++; SyncSelection(); RefreshPreviews(); return true;
        }
        catch (Exception error) { _status.Text = error.Message; return false; }
    }
    private void FullRangeChanged()
    {
        if (_changing) return;
        if (_fullRange.IsChecked == true && _source is not null) WriteRegion(_rangeFields, FullFrame(_source));
        ApplyCoordinates(true);
    }
    private void SyncSelection()
    {
        _view.RangeRegion = _source is null ? null : Result.SearchRegion ?? FullFrame(_source); _view.TargetRegion = _source is null ? null : Result.TargetRegion;
        _rangeButton.IsEnabled = _targetButton.IsEnabled = _source is not null;
        foreach (var field in _targetFields) ((FrameworkElement)field.Parent).IsEnabled = _source is not null;
        foreach (var field in _rangeFields) ((FrameworkElement)field.Parent).IsEnabled = _fullRange.IsChecked != true;
        _rangeButton.Content = _view.SelectionMode == LabelSelectionMode.Range ? "确定范围" : "圈选范围";
        _targetButton.Content = _view.SelectionMode == LabelSelectionMode.Target ? "确定目标" : "圈选目标";
    }
    private void OptionsChanged()
    {
        if (_changing) return; Result.SearchMethod = _method.SelectedValue as string ?? Result.SearchMethod;
        _revision++; UpdateParameterText(); RefreshPreviews();
    }
    private void UpdateParameterText()
    {
        var names = new List<string>(); if (Result.Grayscale) names.Add("灰度"); if (Result.Binary) names.Add("二值"); if (Result.GaussianBlur) names.Add("高斯");
        _parameters.Content = "参数 · " + (names.Count == 0 ? "默认" : string.Join("+", names));
    }
    private void RefreshPreviews()
    {
        _match.Text = "匹配度：—";
        try
        {
            if (_source is not null && Result.TargetRegion is not null) _template = ImageTools.Crop(_source, Result.TargetRegion);
            _targetPreview.Source = _template is null ? null : ImageRegionView.Bitmap(ImageTools.Preprocess(_template, Result).Png);
            _rangePreview.Source = _source is null ? null : ImageRegionView.Bitmap(ImageTools.Preprocess(ImageTools.Crop(_source, Result.SearchRegion ?? FullFrame(_source)), Result).Png);
        }
        catch (Exception error) { _status.Text = error.Message; }
    }
    private bool ReadFields(bool validateName)
    {
        try
        {
            string name = _name.Text.Trim();
            if (validateName && (name.Length == 0 || !_nameAvailable(name))) throw new ArgumentException("标签名不能为空或重复。");
            if (!double.TryParse(_threshold.Text, out double percent) || !double.IsFinite(percent) || percent < 0 || percent > 100) throw new ArgumentException("匹配阈值须为 0–100。");
            if (_method.SelectedValue is not string method) throw new ArgumentException("请选择支持的搜索方法。");
            Result.Name = name; Result.Threshold = percent / 100; Result.SearchMethod = method;
            if (_source is not null && !ApplyCoordinates(false)) return false;
            if (!ApplyCoordinates(true)) return false;
            if (_template is null) throw new InvalidOperationException("请先打开图片并圈选目标。");
            return true;
        }
        catch (Exception error) { _status.Text = error.Message; return false; }
    }
    private bool CommitDraft() => ReadFields(true);
    private async Task TestAsync()
    {
        if (_testing || !ReadFields(false)) return;
        try
        {
            var current = _currentCapture(); var frame = current?.Copy() ?? _source?.Copy() ?? throw new InvalidOperationException("请先打开原图，或在图像标签页开始预览。");
            var label = ProjectStore.Clone(Result); var template = _template!.Copy(); int revision = _revision;
            _testing = true; _test.IsEnabled = false; _match.Text = "匹配度：计算中…";
            var result = await Task.Run(() => TemplateMatcher.Match(frame, template, label));
            if (_closed || revision != _revision) return;
            _match.Text = $"匹配度：{result.Score * 100:F1}%";
            _rangePreview.Source = ImageRegionView.Bitmap(ImageTools.Preprocess(ImageTools.Crop(frame, label.SearchRegion ?? FullFrame(frame)), label).Png);
            _status.Text = $"{(current is null ? "静态图片测试" : "当前预览测试")} · {(result.IsMatch ? "达到阈值" : "未达到阈值")} · 位置 {result.Bounds}";
        }
        catch (Exception error) { if (!_closed) { _match.Text = "匹配度：—"; _status.Text = error.Message; } }
        finally { _testing = false; _test.IsEnabled = true; }
    }
}
