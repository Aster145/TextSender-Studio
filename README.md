# TextSender Studio

Windows 桌面输入与图像脚本工具。通过 ESP32-S3 的 Wi-Fi / USB HID 桥接输入文字和手柄按键，管理图像标签、项目脚本与 HTTP 设备动作。

这是独立新版：程序名、单例标识、配置和默认局域网端口均与旧 TextSender 分开，不迁移旧项目。两款程序可以保留在不同文件夹中；同一串口或设备输入会话由正在使用的程序占用。

## 下载与启动

从 [Releases](https://github.com/Aster145/TextSender-Studio/releases) 下载 Windows x64 压缩包，完整解压到可写目录，运行 `TextSenderStudio.exe`。发布包自带 .NET 运行环境，无需安装 .NET SDK。不要在 ZIP 内直接运行。

首次启动点击“新增项目”。界面分为三个标签页：

- **主标签**：文字发送、发送延迟、项目短语和启动脚本、EasyCon 语法编辑与执行、画面预览和识图区域、执行日志。
- **图像标签**：从采集画面框选目标，保存图片、搜索范围、匹配阈值；加载/导出 `.tslabel` 文件。
- **HTTP 动作**：按顺序配置请求及等待，通过 `CALL 函数名` 在主脚本中调用。

右上角“设置”保存 ESP32、机器人 IP/端口、HTTP 超时和本机局域网接口。“Pro 手柄 / 键盘与文字”手动栏默认隐藏，可以在设置中打开。窗口默认置顶。

## 项目与设置

```text
TextSenderStudio.exe
settings.json                 全局设置，运行时生成
projects/
  我的项目/
    project.json              短语、脚本快捷方式、HTTP 动作、标签配置
    main.ecs                  主脚本
    draft.txt                 普通文字草稿
    labels/                   标签图片
    scripts/                  可存放启动脚本
```

切换项目时保存编辑器内容并载入对应资源。项目可自定义显示名称；删除项目会将其文件夹移入回收站，不删除引用的外部脚本。

短语、启动脚本和 HTTP 动作的函数名在当前项目中唯一。脚本等待单位全部为毫秒；发送前等待为 `0` 时立即开始发送，USB 和网络本身仍有传输时间。

## 设备连接

- 新版 Wi-Fi 固件：ESP32-S3、16 MB Flash，原生 USB/OTG 接目标设备，UART 口使用独立稳定电源。
- 首次连接开发板热点，使用设置中的“首次 Wi-Fi 配网”，之后让电脑与开发板加入同一 Wi-Fi。
- [固件说明](docs/firmware.md)包含热点信息、构建/刷写步骤与协议。
- 串口模式供原有 TextKeyboard TYPE2/HID 固件使用；手柄和 Wi-Fi 配网需新版固件。
- [键盘直通](docs/input.md)仅在直通窗口前台启用，支持多键，并在失焦、停止或错误时释放输入。

电脑端只在用户发起操作时连接设备。设备 IP、Wi-Fi 密码、用户项目和日志不随源码或发布包分发。

## 画面与脚本

支持采集卡/摄像头和桌面选区。先开始画面预览，再选择全画面或指定识图范围；可以拖动框选或输入像素坐标。制作标签时另外框选目标图片，避免将“目标模板”与“搜索范围”混淆。

模板匹配支持彩色或灰度。标签尺寸必须不超过搜索范围，纯色模板会提示重新选择。详情见 [采集与识图](docs/vision.md)。

脚本使用 EasyCon 的编译器和 ECX 解释器，支持核心条件、循环、函数、按键、摇杆与图像标签语法。Studio 提供配置动作函数、`SEND_TEXT` 和 `KEYBOARD` 扩展。具体语法与尚未接入的宿主能力见 [脚本说明](docs/scripting.md)。

项目不会附带自动触发或重试漏洞利用的脚本。

## 本机局域网接口

默认关闭。设置中选择电脑的局域网 IP 并启用，默认端口 `19533`，按需求不要求令牌。接口只接受当前项目中已经配置的名称，不接收命令行、文件路径或任意脚本内容。

```http
GET /api/items

POST /api/run
Content-Type: application/json

{"kind":"phrase","name":"问候","project":"我的项目"}
```

`kind` 为 `phrase` 或 `script`，`project` 可以省略。`202` 表示已接受执行，执行结果查看程序日志；正在执行其他操作时返回 `409`。

## 从源码构建

需要 Windows、.NET 10 SDK。桌面程序为 WPF，识图依赖 OpenCvSharp / FlashCap。

```powershell
dotnet build src/TextSenderStudio/TextSenderStudio.csproj -c Release
powershell -NoProfile -File ./test.ps1
powershell -NoProfile -File ./build.ps1
```

也可双击 `build.cmd` / `test.cmd`。结果位于 `artifacts/`。固件独立构建，不自动刷写开发板。

## 当前验证范围

首版为 `0.1.0`：已进行桌面构建、项目/设置与脚本测试、合成图匹配测试、模拟 HTTP/串口输入测试及固件编译。目标设备的 USB 枚举、按键效果、真实采集卡以及手指机器人联动仍需实机验证。

## 许可证

桌面程序采用 GPL-3.0，包含 EasyCon 编译器/解释器对应源码。第三方来源和许可证见 [THIRD_PARTY.md](THIRD_PARTY.md)。固件的独立来源及许可说明位于 `firmware/NOTICE.md`。
