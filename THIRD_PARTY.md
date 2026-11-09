# 第三方来源

- **EasyCon.Script**：来自 [EasyConNS/EasyCon](https://github.com/EasyConNS/EasyCon)，GPL-3.0。完整所用源码、许可证和来源提交记录在 `vendor/EasyCon.Script/`。宿主适配由 TextSender Studio 实现。
- **EasyCon 手柄参考**：默认键盘映射、Pro 按键和实体手柄位置映射参考同一提交 `76cdb216d00fc5e0fb8510dede85390a6c5f3d40` 的 `KeyMappingConfig.cs`、`SwitchCommand.cs`、`GamepadMapper.cs`；录制流程参考 `OperationRecords.cs`。Studio 的传输队列、WPF 控制窗口和完整状态录制另行实现，沿用本仓库 GPL-3.0 许可。
- **EasyCon 标签制作与搜索参考**：同一提交的 `TagEditorView.axaml`、`TagEditorViewModel.cs`、`Search.cs` 与 `CVSearch.cs`。Studio 用 WPF 实现双选区编辑和本地标签存储，沿用六种图像搜索方法；灰度、二值化、高斯模糊在 Studio 中接入实际预览与匹配，OCR 未接入。相关实现按本仓库 GPL-3.0 许可提供。
- **OpenCvSharp5**：来自 [shimat/opencvsharp](https://github.com/shimat/opencvsharp)，Apache-2.0；本项目使用 NuGet 包及对应 Windows 运行库。
- **FlashCap**：来自 [kekyo/FlashCap](https://github.com/kekyo/FlashCap)，Apache-2.0；用于采集设备枚举和帧读取。
- **AvalonEdit 6.3.1.120**：来自 [icsharpcode/AvalonEdit](https://github.com/icsharpcode/AvalonEdit)，MIT；用于 WPF 代码编辑、行号和折叠。许可证保存在 `licenses/AvalonEdit-MIT.txt`。
- **.NET / WPF / System.IO.Ports**：来自 [dotnet](https://github.com/dotnet)，各 NuGet 和运行时包保留其上游许可证。
- **TextSender**：键盘映射、焦点捕获和具名局域网接口由本项目的前身 `Aster145/ESP32S3-TextSender` 延续、适配至独立新程序。

固件不使用 EasyCon 代码，其来源与许可证单独记录于 [firmware/NOTICE.md](firmware/NOTICE.md)。
