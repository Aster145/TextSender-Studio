using System.IO;
using System.Threading;
using System.Windows;

namespace TextSenderStudio;

public partial class App : Application
{
    private Mutex? _instance;
    public static string DataRoot { get; private set; } = AppContext.BaseDirectory;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Explicit test data location prevents UI checks from creating personal projects.
        var dataArgument = Array.IndexOf(e.Args, "--data-dir");
        if (dataArgument >= 0 && dataArgument + 1 < e.Args.Length) DataRoot = Path.GetFullPath(e.Args[dataArgument + 1]);
        var identity = "Local\\TextSenderStudio.v1" + (dataArgument >= 0 ? "." + DataRoot.GetHashCode() : "");
        _instance = new Mutex(true, identity, out var created);
        if (!created) { MessageBox.Show("TextSender Studio 已经在运行。", "TextSender Studio"); Shutdown(); return; }
        DispatcherUnhandledException += (_, args) => { MessageBox.Show(args.Exception.Message, "操作未完成", MessageBoxButton.OK, MessageBoxImage.Error); args.Handled = true; };
        try { var window = new MainWindow(); MainWindow = window; window.Show(); }
        catch (Exception error) { MessageBox.Show(error.Message, "启动失败", MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(1); }
    }
    protected override void OnExit(ExitEventArgs e) { _instance?.Dispose(); base.OnExit(e); }
}
