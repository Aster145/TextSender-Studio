using System.Net.Http.Headers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TextSenderStudio.Core;

namespace TextSenderStudio;

public partial class MainWindow
{
    private bool _httpEditing, _httpSelectionChanging, _httpToggleChanging;
    private string _httpProjectDirectory = "";

    private void RefreshHttpActions()
    {
        if (_project == null) return;
        var sameProject = _httpProjectDirectory == _directory;
        var previous = sameProject ? _selectedHttp : null;
        var selected = _project.HttpActions.FirstOrDefault(a => ReferenceEquals(a, previous))
            ?? _project.HttpActions.FirstOrDefault(a => a.FunctionName == previous?.FunctionName)
            ?? _project.HttpActions.FirstOrDefault();
        _httpSelectionChanging = true;
        try
        {
            HttpList.ItemsSource = null;
            HttpList.ItemsSource = _project.HttpActions;
            HttpList.SelectedItem = selected;
        }
        finally { _httpSelectionChanging = false; }
        if (!sameProject)
        {
            _httpProjectDirectory = _directory;
            _httpEditing = false;
            SetHttpToggle(false);
        }
        // Unrelated resource edits may replace the project's model. Preserve the
        // current HTTP draft while rebinding the selected action to that model.
        if (previous != null && selected?.FunctionName == previous.FunctionName)
            _selectedHttp = selected;
        else LoadHttpAction(selected);
        HttpEmpty.Visibility = selected == null ? Visibility.Visible : Visibility.Collapsed;
        HttpDetails.Visibility = selected == null ? Visibility.Collapsed : Visibility.Visible;
        UpdateHttpEditingUi();
    }

    private void LoadHttpAction(HttpAction? action)
    {
        _selectedHttp = action;
        HttpDetails.Visibility = action == null ? Visibility.Collapsed : Visibility.Visible;
        HttpName.Text = action?.Name ?? "";
        HttpFunction.Text = action?.FunctionName ?? "";
        _steps = action == null ? [] : new(action.Steps.Select(ProjectStore.Clone));
        HttpSteps.ItemsSource = _steps;
        HttpSteps.SelectedIndex = _steps.Count > 0 ? 0 : -1;
        HttpResult.Text = action == null ? "打开“允许编辑”后新建动作。" : "主脚本调用：CALL " + action.FunctionName;
        UpdateHttpEditingUi();
    }

    private void HttpSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_httpSelectionChanging) return;
        var next = HttpList.SelectedItem as HttpAction;
        if (ReferenceEquals(next, _selectedHttp)) return;
        try { SavePendingHttp(); }
        catch (Exception error)
        {
            _httpSelectionChanging = true;
            try { HttpList.SelectedItem = _selectedHttp; }
            finally { _httpSelectionChanging = false; }
            HttpResult.Text = "未切换动作，请先修正：" + error.Message;
            return;
        }
        LoadHttpAction(next);
    }

    private void SetHttpToggle(bool enabled)
    {
        _httpToggleChanging = true;
        try { HttpEditToggle.IsChecked = enabled; }
        finally { _httpToggleChanging = false; }
    }

    private void HttpEditingChanged(object sender, RoutedEventArgs e)
    {
        if (_httpToggleChanging || HttpSteps == null) return;
        if (_operation != null) { SetHttpToggle(_httpEditing); return; }
        if (HttpEditToggle.IsChecked == true) _httpEditing = true;
        else
        {
            try { SavePendingHttp(); _httpEditing = false; }
            catch (Exception error)
            {
                SetHttpToggle(true);
                HttpResult.Text = "未关闭编辑，请先修正：" + error.Message;
            }
        }
        UpdateHttpEditingUi();
    }

    private void UpdateHttpEditingUi()
    {
        if (HttpSteps == null) return;
        var editable = _httpEditing && _project != null && _operation == null;
        HttpEditToggle.IsEnabled = _operation == null;
        HttpDetails.IsEnabled = _operation == null;
        var hasAction = _selectedHttp != null;
        HttpSteps.IsReadOnly = !editable;
        HttpName.IsReadOnly = HttpFunction.IsReadOnly = HttpBody.IsReadOnly = HttpContentType.IsReadOnly = !editable;
        NewHttpButton.IsEnabled = editable;
        SaveHttpButton.IsEnabled = DeleteHttpButton.IsEnabled = AddHttpStepButton.IsEnabled = editable && hasAction;
        var index = HttpSteps.SelectedIndex;
        DeleteHttpStepButton.IsEnabled = editable && index >= 0;
        MoveHttpStepUpButton.IsEnabled = editable && index > 0;
        MoveHttpStepDownButton.IsEnabled = editable && index >= 0 && index + 1 < _steps.Count;
    }

    private void HttpStepSelected(object sender, SelectionChangedEventArgs e) => UpdateHttpEditingUi();

    private void CommitHttpStep()
    {
        if (!HttpSteps.CommitEdit(DataGridEditingUnit.Cell, true) ||
            !HttpSteps.CommitEdit(DataGridEditingUnit.Row, true) || HasValidationError(HttpSteps))
            throw new ArgumentException("请先修正步骤中的输入；等待时间须为 0–600000 的整数毫秒。");
    }

    private static bool HasValidationError(DependencyObject element)
    {
        if (Validation.GetHasError(element)) return true;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            if (HasValidationError(VisualTreeHelper.GetChild(element, i))) return true;
        return false;
    }

    private void EditHttpSteps(Action edit)
    {
        if (!_httpEditing || _selectedHttp == null || _operation != null) return;
        try
        {
            CommitHttpStep();
            edit();
            UpdateHttpEditingUi();
            HttpResult.Text = "步骤已修改；保存动作或关闭编辑时保存。";
        }
        catch (Exception error) { HttpResult.Text = error.Message; }
    }

    private void AddHttpStepClick(object sender, RoutedEventArgs e) => EditHttpSteps(() =>
    {
        var index = HttpSteps.SelectedIndex < 0 ? _steps.Count : HttpSteps.SelectedIndex + 1;
        var step = new HttpStep();
        _steps.Insert(index, step);
        HttpSteps.SelectedItem = step;
        HttpSteps.ScrollIntoView(step);
    });

    private void DeleteHttpStepClick(object sender, RoutedEventArgs e) => EditHttpSteps(() =>
    {
        var index = HttpSteps.SelectedIndex;
        if (index < 0) return;
        _steps.RemoveAt(index);
        HttpSteps.SelectedIndex = Math.Min(index, _steps.Count - 1);
    });

    private void MoveHttpStep(int direction) => EditHttpSteps(() =>
    {
        var index = HttpSteps.SelectedIndex;
        var destination = index + direction;
        if (index < 0 || destination < 0 || destination >= _steps.Count) return;
        var step = _steps[index];
        _steps.Move(index, destination);
        HttpSteps.SelectedItem = step;
        HttpSteps.ScrollIntoView(step);
    });
    private void MoveHttpStepUpClick(object sender, RoutedEventArgs e) => MoveHttpStep(-1);
    private void MoveHttpStepDownClick(object sender, RoutedEventArgs e) => MoveHttpStep(1);

    private void SavePendingHttp() { if (_httpEditing && _operation == null) SaveHttp(); }

    private void SaveHttp()
    {
        if (_project == null || _selectedHttp == null || _operation != null) return;
        CommitHttpStep();
        if (_steps.Count == 0)
            throw new ArgumentException("至少需要一个请求步骤。可用“删除动作”移除整个动作。");
        var index = _project.HttpActions.IndexOf(_selectedHttp);
        if (index < 0) throw new InvalidOperationException("当前动作已变化，请重新选择动作。");
        var clone = ProjectStore.Clone(_project);
        var changed = clone.HttpActions[index];
        changed.Name = HttpName.Text.Trim();
        changed.FunctionName = HttpFunction.Text.Trim();
        changed.Steps = _steps.Select(ProjectStore.Clone).ToList();
        for (var i = 0; i < changed.Steps.Count; i++)
        {
            var step = changed.Steps[i];
            step.Method = step.Method.Trim().ToUpperInvariant();
            if (step.WaitAfterMs < 0 || step.WaitAfterMs > 600000)
                throw new ArgumentException($"第 {i + 1} 步：等待时间须为 0–600000 ms。");
            try { HttpRequestAddress.ValidateTarget(step.Path); }
            catch (ArgumentException error) { throw new ArgumentException($"第 {i + 1} 步：{error.Message}"); }
            if (!new[] { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD" }.Contains(step.Method))
                throw new ArgumentException($"第 {i + 1} 步：请选择有效的 HTTP 方法。");
            if ((step.Body.Length > 0 || step.Method is "POST" or "PUT" or "PATCH") && !MediaTypeHeaderValue.TryParse(step.ContentType, out _))
                throw new ArgumentException($"第 {i + 1} 步：Content-Type 无效。");
        }
        // Save the validated clone first. A failed disk write must not mark the
        // draft as saved or change the action that the execution path can see.
        _store.Save(_directory, clone);
        _selectedHttp.Name = changed.Name;
        _selectedHttp.FunctionName = changed.FunctionName;
        _selectedHttp.Steps = changed.Steps;
        HttpList.Items.Refresh();
        RefreshEditorContext();
        HttpResult.Text = "已保存。主脚本调用：CALL " + _selectedHttp.FunctionName;
    }

    private void NewHttpClick(object sender, RoutedEventArgs e)
    {
        if (_project == null || !_httpEditing || _operation != null) return;
        try
        {
            SavePendingHttp();
            var suffix = Guid.NewGuid().ToString("N")[..8];
            var action = new HttpAction { Name = "新动作_" + suffix, FunctionName = "http_" + suffix };
            var clone = ProjectStore.Clone(_project); clone.HttpActions.Add(action); _store.Save(_directory, clone);
            _project.HttpActions.Add(action); RefreshHttpActions(); HttpList.SelectedItem = action; RefreshEditorContext();
        }
        catch (Exception error) { HttpResult.Text = error.Message; }
    }

    private void SaveHttpClick(object sender, RoutedEventArgs e)
    {
        if (!_httpEditing) return;
        try { SaveHttp(); } catch (Exception error) { HttpResult.Text = error.Message; }
    }
    private void InsertHttpClick(object sender, RoutedEventArgs e)
    {
        try { SavePendingHttp(); if (_selectedHttp != null) InsertCode("CALL " + _selectedHttp.FunctionName); }
        catch (Exception error) { HttpResult.Text = error.Message; }
    }
    private async void RunHttpClick(object sender, RoutedEventArgs e)
    {
        if (_operation != null) return;
        try { SavePendingHttp(); if (_selectedHttp != null) await RunAction(_selectedHttp.FunctionName, false); }
        catch (Exception error) { HttpResult.Text = error.Message; }
    }
    private void HttpActionDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || _operation != null ||
            e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(HttpList, source) is not ListBoxItem item ||
            item.Content is not HttpAction action) return;
        HttpList.SelectedItem = action;
        // Invalid drafts may reject a selection change; never run the previous row.
        if (!ReferenceEquals(HttpList.SelectedItem, action)) return;
        e.Handled = true;
        RunHttpClick(sender, e);
    }
    private void DeleteHttpClick(object sender, RoutedEventArgs e)
    {
        if (_project == null || _selectedHttp == null || !_httpEditing || _operation != null) return;
        try
        {
            var index = _project.HttpActions.IndexOf(_selectedHttp);
            var clone = ProjectStore.Clone(_project); clone.HttpActions.RemoveAt(index); _store.Save(_directory, clone);
            HttpSteps.CancelEdit(DataGridEditingUnit.Cell); HttpSteps.CancelEdit(DataGridEditingUnit.Row);
            _project.HttpActions.RemoveAt(index); _selectedHttp = null; RefreshHttpActions(); RefreshEditorContext();
        }
        catch (Exception error) { HttpResult.Text = error.Message; }
    }
}
