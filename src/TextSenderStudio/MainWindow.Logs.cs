using System.Windows;

namespace TextSenderStudio;

public partial class MainWindow
{
    private readonly DiagnosticLogBuffer _diagnosticLog = new();

    private void AppendScriptOutput(string kind, string text)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => AppendScriptOutput(kind, text)); return; }
        ScriptOutputBox.AppendText($"{DateTime.Now:HH:mm:ss}  [{kind}] {text}\n");
        if (ScriptOutputBox.Text.Length > 24000) ScriptOutputBox.Text = ScriptOutputBox.Text[^16000..];
        ScriptOutputBox.ScrollToEnd();
    }

    private void ClearScriptOutputClick(object sender, RoutedEventArgs e) => ScriptOutputBox.Clear();
}

// UI-thread owned, bounded history. Opening settings subscribes to the existing
// buffer, so messages emitted before the dialog opens remain available.
public sealed class DiagnosticLogBuffer
{
    public string Text { get; private set; } = "";
    public event Action? Changed;
    public void Append(string message)
    {
        Text += $"{DateTime.Now:HH:mm:ss}  {message}\n";
        if (Text.Length > 24000) Text = Text[^16000..];
        Changed?.Invoke();
    }
    public void Clear() { Text = ""; Changed?.Invoke(); }
}
