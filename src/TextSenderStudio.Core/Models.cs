namespace TextSenderStudio.Core;

public sealed class AppSettings
{
    public bool ShowManualControls { get; set; }
    public bool AlwaysOnTop { get; set; } = true;
    public string EspHost { get; set; } = "192.168.4.1";
    public int EspPort { get; set; } = 80;
    public string RobotHost { get; set; } = "";
    public int RobotPort { get; set; } = 80;
    public string RobotScheme { get; set; } = "http";
    public int RequestTimeoutMs { get; set; } = 5000;
    public int SendDelayMs { get; set; }
    public int CharacterDelayMs { get; set; } = 30;
    public int LineDelayMs { get; set; } = 150;
    public bool ClearAutoIndent { get; set; }
    public bool PhraseAutoEnter { get; set; }
    public bool LanEnabled { get; set; }
    public int LanPort { get; set; } = 19533;
    public string LanBindAddress { get; set; } = "127.0.0.1";
    public string LastProject { get; set; } = "";
    public string Transport { get; set; } = "WiFi";
    public string SerialPort { get; set; } = "";
    public PushNotificationSettings PushNotifications { get; set; } = new();
    [System.Text.Json.Serialization.JsonIgnore] public string EspBaseUrl => $"http://{EspHost}:{EspPort}";
    [System.Text.Json.Serialization.JsonIgnore] public string RobotBaseUrl => $"{RobotScheme}://{RobotHost}:{RobotPort}";
}

public sealed class StudioProject
{
    public string Name { get; set; } = "新项目";
    public string MainScript { get; set; } = "main.ecs";
    public List<Phrase> Phrases { get; set; } = [];
    public List<ScriptShortcut> Shortcuts { get; set; } = [];
    public List<ImageLabel> Labels { get; set; } = [];
    public List<HttpAction> HttpActions { get; set; } = [];
    public List<SoundAction> Sounds { get; set; } = [];
    public CaptureSettings Capture { get; set; } = new();
}

public sealed class Phrase
{
    public string Name { get; set; } = "新短语";
    public string FunctionName { get; set; } = "phrase_new";
    public string Text { get; set; } = "";
}

public sealed class ScriptShortcut
{
    public string Name { get; set; } = "新脚本";
    public string FunctionName { get; set; } = "start_script";
    public string Path { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public bool WaitForExit { get; set; } = true;
}

public sealed class HttpAction
{
    public string Name { get; set; } = "新动作";
    public string FunctionName { get; set; } = "http_action";
    public List<HttpStep> Steps { get; set; } = [new()];
}

public sealed class SoundAction
{
    public string Name { get; set; } = "声音提醒";
    public string FunctionName { get; set; } = "sound_notice";
    public string Source { get; set; } = "System";
    public string SystemSound { get; set; } = "Asterisk";
    public string FilePath { get; set; } = "";
}

public sealed class HttpStep
{
    public string Method { get; set; } = "GET";
    public string Path { get; set; } = "/";
    public string Body { get; set; } = "";
    public string ContentType { get; set; } = "application/json";
    public Dictionary<string, string> Headers { get; set; } = [];
    public int WaitAfterMs { get; set; }
}

public sealed class PixelRegion
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public override string ToString() => $"X {X}, Y {Y}, {Width} × {Height}";
}

public sealed class ImageLabel
{
    public string Name { get; set; } = "新标签";
    public string ImagePath { get; set; } = "";
    public PixelRegion? SearchRegion { get; set; }
    public double Threshold { get; set; } = 0.90;
    public bool Grayscale { get; set; }
    public override string ToString() => Name;
}

public sealed class CaptureSettings
{
    public string Source { get; set; } = "Desktop";
    public int CameraIndex { get; set; }
    public int CameraWidth { get; set; } = 1920;
    public int CameraHeight { get; set; } = 1080;
    public PixelRegion? DesktopRegion { get; set; }
    public PixelRegion? RecognitionRegion { get; set; }
}
