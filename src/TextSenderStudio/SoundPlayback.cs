using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Media;
using TextSenderStudio.Core;

namespace TextSenderStudio;

internal static class SoundPlayback
{
    public static Task PlayAsync(SoundAction sound, string projectPath, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (sound.Source == "System")
        {
            var preset = sound.SystemSound switch
            {
                "Asterisk" => SystemSounds.Asterisk,
                "Beep" => SystemSounds.Beep,
                "Exclamation" => SystemSounds.Exclamation,
                "Hand" => SystemSounds.Hand,
                "Question" => SystemSounds.Question,
                _ => throw new ArgumentException("系统提示音无效。")
            };
            preset.Play();
            return Task.CompletedTask;
        }
        if (sound.Source != "File") throw new ArgumentException("声音来源无效。");
        var path = Path.GetFullPath(sound.FilePath, projectPath);
        if (!File.Exists(path)) throw new FileNotFoundException("音乐文件不存在。", path);
        // MediaPlayer is dispatcher-affine. Script execution runs on a worker thread.
        return Application.Current.Dispatcher.InvokeAsync(() => PlayFileAsync(path, token)).Task.Unwrap();
    }

    private static async Task PlayFileAsync(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var player = new MediaPlayer();
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        player.MediaOpened += (_, _) => opened.TrySetResult();
        player.MediaEnded += (_, _) => ended.TrySetResult();
        player.MediaFailed += (_, e) =>
        {
            var error = new IOException("无法播放音乐文件，请检查格式或 Windows 音频组件。", e.ErrorException);
            // A load error belongs to opened; a playback error belongs to ended.
            if (opened.Task.IsCompletedSuccessfully) ended.TrySetException(error);
            else opened.TrySetException(error);
        };
        try
        {
            player.Open(new Uri(path));
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            token.ThrowIfCancellationRequested();
            player.Volume = 1;
            player.Play();
            await ended.Task.WaitAsync(token);
        }
        finally { player.Stop(); player.Close(); }
    }
}
