# 画面与图像标签

## 选择画面来源

- **采集卡 / 摄像头**：刷新设备列表，选择设备后开始采集。设备名由 FlashCap 提供；设备列表中的索引仅用于本机选择。拔插设备后请刷新并重新选择。
- **桌面**：可使用整个虚拟桌面，或在桌面上框选范围。多显示器放在主屏左侧或上方时，桌面 X/Y 可以为负数。
- 开始采集后才获取画面；停止后清除服务内最后一帧，识图不会继续使用停采前的旧画面。

采集工作在后台进行，预览最多更新约 10 次/秒。采集卡优先选择最接近设置宽高的格式，实际画面尺寸以收到的帧为准。首次打开需在 12 秒内收到一帧，否则报告错误并停止。设备被 OBS 或其他软件独占时可能无法打开，请选择可共享的采集来源或桌面选区。

## 两种区域

**桌面采集范围**决定从电脑桌面的哪部分取图，使用桌面的物理像素坐标。

**图像标签搜索区域**决定在已经采集的画面内的哪里寻找目标，原点始终是该画面的左上角。X/Y 不能为负，宽高必须大于零，且选区须完整位于画面内。全画面对应没有指定搜索区域。

目标模板图片和搜索区域是不同数据：模板是要找的图片，搜索区域是允许它出现的位置。缩放、分辨率或游戏 UI 大小变化后，可能需要重新截取模板。

## 匹配

实现使用 OpenCV `TM_CCOEFF_NORMED` 模板匹配，可选择彩色或灰度。结果分数归一到 0–1；界面显示百分比。分数达到标签阈值时 `IsMatch` 为真，返回位置是相对于完整采集画面的坐标。

纯色或几乎无细节的模板会明确报错，避免相关性算法对退化模板给出错误的满分。模板大于搜索区域、区域越界和无效阈值也会报告错误，不会把错误当作“匹配成功”。

## 开发接口

项目：`src/TextSenderStudio.Vision/TextSenderStudio.Vision.csproj`；命名空间：`TextSenderStudio.Vision`。

```csharp
var cameras = await CaptureService.EnumerateCamerasAsync(cancellationToken);
await using var capture = new CaptureService();
capture.FrameReady += frame => { /* 将 frame.Png 调度给 UI；不在此执行耗时操作 */ };
capture.Error += message => { /* 将错误调度给 UI */ };
await capture.StartAsync(project.Capture, cancellationToken);
var frame = capture.GetLatestFrame();
if (frame is not null)
{
    var template = ImageTools.Load(templatePath);
    MatchResult result = TemplateMatcher.Match(frame, template, label);
}
await capture.StopAsync();
```

`FrameReady` 和 `Error` 来自工作线程。`GetLatestFrame()` 和事件帧持有独立的 PNG 字节副本。`ImageTools.Crop`、`Load`、`Save` 处理模板；保存格式为 PNG，支持中文路径。

纯合成图测试（不会启动采集卡或桌面采集）：

```powershell
dotnet run --project tests/TextSenderStudio.Vision.Checks -c Release
```

依赖固定为 OpenCvSharp5 / Windows runtime `5.0.0.20260905`、FlashCap `1.11.0`。这些依赖使用 Apache-2.0 许可证；原生依赖的通知文件随运行时 NuGet 包分发。

官方参考：[OpenCvSharp](https://github.com/shimat/opencvsharp)、[FlashCap](https://github.com/kekyo/FlashCap)、[OpenCV 模板匹配](https://docs.opencv.org/4.x/df/dfb/group__imgproc__object.html)。
