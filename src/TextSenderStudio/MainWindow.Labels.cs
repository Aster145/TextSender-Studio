using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;
using TextSenderStudio.Core;
using TextSenderStudio.Vision;

namespace TextSenderStudio;

public partial class MainWindow
{
    private bool _labelEditorOpen;
    private FrameSnapshot? CurrentLabelFrame() => _capture.GetLatestFrame() ?? _frame?.Copy();
    private void NewLabelClick(object sender, RoutedEventArgs e)
    {
        if (_project == null || _operation != null || _labelEditorOpen) return;
        string name = "新标签"; int suffix = 2; while (_project.Labels.Any(label => label.Name == name)) name = "新标签_" + suffix++;
        var draft = new ImageLabel { Name = name, SearchRegion = _project.Capture.RecognitionRegion is null ? null : ProjectStore.Clone(_project.Capture.RecognitionRegion) };
        ShowLabelEditor(draft, null, CurrentLabelFrame(), null);
    }
    private void EditLabelClick(object sender, RoutedEventArgs e)
    {
        if (_project == null || _selectedLabel == null || _operation != null || _labelEditorOpen) return;
        try
        {
            var template = ImageTools.Load(_store.Resolve(_directory, _selectedLabel.ImagePath)); FrameSnapshot? source = null;
            if (!string.IsNullOrWhiteSpace(_selectedLabel.SourceImagePath))
            {
                var path = _store.Resolve(_directory, _selectedLabel.SourceImagePath);
                if (File.Exists(path)) source = ImageTools.Load(path);
            }
            ShowLabelEditor(ProjectStore.Clone(_selectedLabel), _selectedLabel, source, template);
        }
        catch (Exception error) { Dialogs.Error(this, error); }
    }
    private void ShowLabelEditor(ImageLabel draft, ImageLabel? original, FrameSnapshot? source, FrameSnapshot? template)
    {
        if (_project == null || _operation != null || _labelEditorOpen) return;
        _labelEditorOpen = true; UpdateResourceEditingState();
        try
        {
            var editor = new LabelEditorWindow(this, draft, source, template, CurrentLabelFrame,
                name => !_project.Labels.Any(label => label != original && label.Name == name));
            if (editor.ShowDialog() == true) PersistLabelEditor(original, editor);
        }
        catch (Exception error) { Dialogs.Error(this, error); }
        finally { _labelEditorOpen = false; UpdateResourceEditingState(); }
    }
    private void PersistLabelEditor(ImageLabel? original, LabelEditorWindow editor)
    {
        if (_project == null || _operation != null) throw new InvalidOperationException("项目正在执行，暂时不能保存标签。");
        var label = ProjectStore.Clone(editor.Result);
        if (string.IsNullOrWhiteSpace(label.Name) || _project.Labels.Any(item => item != original && item.Name == label.Name)) throw new ArgumentException("标签名称不能为空或重复。");
        var template = editor.TemplateFrame ?? throw new InvalidOperationException("没有可保存的目标图片。");
        string stem = "labels/" + Guid.NewGuid().ToString("N"); label.ImagePath = stem + ".png";
        ImageTools.Save(template, _store.Resolve(_directory, label.ImagePath));
        if (editor.SourceFrame is { } source)
        {
            label.SourceImagePath = stem + ".source.png"; ImageTools.Save(source, _store.Resolve(_directory, label.SourceImagePath));
        }
        int index = original is null ? -1 : _project.Labels.IndexOf(original);
        if (index < 0) _project.Labels.Add(label); else _project.Labels[index] = label;
        try { _store.Save(_directory, _project); }
        catch { if (index < 0) _project.Labels.Remove(label); else _project.Labels[index] = original!; throw; }
        RefreshResources(); MainTabs.SelectedItem = ImageLabelsTab; LabelsList.SelectedItem = label; LabelResult.Text = "标签已保存，原图与选区可再次编辑。";
    }
    private void LabelSelected(object sender, SelectionChangedEventArgs e)
    {
        _selectedLabel = LabelsList.SelectedItem as ImageLabel; LabelDetails.Visibility = _selectedLabel == null ? Visibility.Collapsed : Visibility.Visible; if (_selectedLabel == null) return;
        LabelName.Text = _selectedLabel.Name; LabelThreshold.Text = (_selectedLabel.Threshold * 100).ToString("0.##"); LabelGrayscale.IsChecked = _selectedLabel.Grayscale;
        string method = TemplateMatcher.SupportedMethods.FirstOrDefault(item => item.Id == _selectedLabel.SearchMethod)?.Name ?? _selectedLabel.SearchMethod;
        LabelRegion.Text = "搜索区域：" + (_selectedLabel.SearchRegion?.ToString() ?? "全画面") + "\n目标位置：" + (_selectedLabel.TargetRegion?.ToString() ?? "旧标签未记录") + "\n搜索方法：" + method;
        try { LabelImage.Source = ImageRegionView.Bitmap(ImageTools.Load(_store.Resolve(_directory, _selectedLabel.ImagePath)).Png); } catch (Exception error) { LabelImage.Source = null; LabelResult.Text = error.Message; }
    }
    private void SaveLabelClick(object sender, RoutedEventArgs e) => EditLabelClick(sender, e);
    private void AdoptLabelRegionClick(object sender, RoutedEventArgs e) { if (_selectedLabel == null || _project == null || _operation != null || _labelEditorOpen) return; _selectedLabel.SearchRegion = _project.Capture.RecognitionRegion == null ? null : ProjectStore.Clone(_project.Capture.RecognitionRegion); _store.Save(_directory, _project); LabelSelected(LabelsList, new SelectionChangedEventArgs(Selector.SelectionChangedEvent, Array.Empty<object>(), Array.Empty<object>())); }
    private async void TestLabelClick(object sender, RoutedEventArgs e)
    {
        if (_selectedLabel == null || _operation != null || _labelEditorOpen) return;
        try { var frame = CurrentLabelFrame() ?? throw new InvalidOperationException("请先开始画面预览，或在标签编辑器中打开图片测试。"); var template = ImageTools.Load(_store.Resolve(_directory, _selectedLabel.ImagePath)); var label = ProjectStore.Clone(_selectedLabel); var result = await Task.Run(() => TemplateMatcher.Match(frame, template, label)); LabelResult.Text = $"匹配度 {result.Score * 100:F1}% · {(result.IsMatch ? "达到阈值" : "未达到阈值")} · {result.Bounds}"; } catch (Exception error) { LabelResult.Text = error.Message; }
    }
    private void InsertLabelClick(object sender, RoutedEventArgs e) { if (_selectedLabel != null) InsertCode("$score = @" + _selectedLabel.Name); }
    private void DeleteLabelClick(object sender, RoutedEventArgs e) { if (_project == null || _selectedLabel == null || _operation != null || _labelEditorOpen) return; _project.Labels.Remove(_selectedLabel); _store.Save(_directory, _project); RefreshResources(); }
    private sealed record LabelBundle(ImageLabel Label, string PngBase64, string? SourcePngBase64 = null);
    private LabelBundle CreateLabelBundle(ImageLabel original)
    {
        var label = ProjectStore.Clone(original); string? source = null;
        if (!string.IsNullOrWhiteSpace(label.SourceImagePath))
        {
            var path = _store.Resolve(_directory, label.SourceImagePath);
            if (File.Exists(path)) source = Convert.ToBase64String(File.ReadAllBytes(path)); else label.SourceImagePath = null;
        }
        return new(label, Convert.ToBase64String(File.ReadAllBytes(_store.Resolve(_directory, label.ImagePath))), source);
    }
    private void ExportLabelClick(object sender, RoutedEventArgs e)
    {
        if (_selectedLabel == null) return; var picker = new SaveFileDialog { Filter = "TextSender 图像标签|*.tslabel", FileName = _selectedLabel.Name + ".tslabel" }; if (picker.ShowDialog(this) != true) return;
        try { ProjectStore.Write(picker.FileName, CreateLabelBundle(_selectedLabel)); } catch (Exception error) { Dialogs.Error(this, error); }
    }
    private void ImportLabelClick(object sender, RoutedEventArgs e)
    {
        if (_project == null || _operation != null || _labelEditorOpen) return; var picker = new OpenFileDialog { Filter = "TextSender 图像标签 / 图片|*.tslabel;*.png;*.jpg;*.jpeg;*.bmp" }; if (picker.ShowDialog(this) != true) return;
        try { ImportLabelFile(picker.FileName); }
        catch (OperationCanceledException) { } catch (Exception error) { Dialogs.Error(this, error); }
    }
    private ImageLabel ImportLabelFile(string filename)
    {
            if (_project == null || _operation != null || _labelEditorOpen) throw new InvalidOperationException("当前不能加载标签。");
            ImageLabel label; FrameSnapshot image; FrameSnapshot? source = null;
            if (Path.GetExtension(filename).Equals(".tslabel", StringComparison.OrdinalIgnoreCase))
            {
                var bundle = ProjectStore.Read<LabelBundle>(filename) ?? throw new IOException("标签文件无效。"); label = ProjectStore.Clone(bundle.Label);
                image = ImageTools.FromEncoded(Convert.FromBase64String(bundle.PngBase64));
                if (bundle.SourcePngBase64 is not null) source = ImageTools.FromEncoded(Convert.FromBase64String(bundle.SourcePngBase64));
            }
            else { label = new() { Name = Path.GetFileNameWithoutExtension(filename) }; image = ImageTools.Load(filename); source = image; label.TargetRegion = new() { Width = image.Width, Height = image.Height }; }
            if (_project.Labels.Any(l => l.Name == label.Name)) label.Name = Dialogs.Prompt(this, "同名标签", "输入新标签名", label.Name + "_2") ?? throw new OperationCanceledException();
            if (string.IsNullOrWhiteSpace(label.Name) || _project.Labels.Any(l => l.Name == label.Name)) throw new ArgumentException("标签名称不能为空或重复。");
            string stem = "labels/" + Guid.NewGuid().ToString("N"); label.ImagePath = stem + ".png"; label.SourceImagePath = null;
            if (source is not null)
            {
                if (label.TargetRegion is not null) ImageTools.ValidateRegion(label.TargetRegion, source.Width, source.Height);
                label.SourceImagePath = stem + ".source.png"; ImageTools.Save(source, _store.Resolve(_directory, label.SourceImagePath));
            }
            ImageTools.Save(image, _store.Resolve(_directory, label.ImagePath)); _project.Labels.Add(label);
            try { _store.Save(_directory, _project); } catch { _project.Labels.Remove(label); throw; }
            RefreshResources(); LabelsList.SelectedItem = label; return label;
    }
}
