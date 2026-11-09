# TextSender Studio

Windows 桌面输入与图像脚本工具。通过 ESP32-S3 的 Wi-Fi / USB HID 桥接输入文字和手柄按键，管理图像标签、项目脚本与 HTTP 设备动作。

这是独立新版：程序名、单例标识、配置和默认局域网端口均与旧 TextSender 分开，不迁移旧项目。两款程序可以保留在不同文件夹中；同一串口或设备输入会话由正在使用的程序占用。

## 下载与启动

当前 `0.2.0` 更新以源码提供。按下方“单文件 EXE”步骤构建后，将 `TextSenderStudio.exe` 放在可写目录运行；生成的程序自带 .NET 运行环境。历史版本可在 [Releases](https://github.com/Aster145/TextSender-Studio/releases) 查看。

首次启动点击“新增项目”。界面分为三个标签页：

- **主标签**：文字发送、发送延迟、项目短语和启动脚本、声音提醒、EasyCon 语法编辑与执行、画面预览和识图区域、执行日志。编辑器支持语法说明、补全、行号、自动缩进和函数跳转。
- **图像标签**：从采集画面框选目标，保存图片、搜索范围、匹配阈值；加载/导出 `.tslabel` 文件。
- **HTTP 动作**：默认只读，打开“允许编辑”后新增、删除、上移或下移步骤；每行可填写不同设备的完整 URL，通过 `CALL 函数名` 在主脚本中调用。

右上角“设置”保存 ESP32、旧相对路径使用的默认 HTTP 设备、HTTP 超时、本机局域网接口和推送服务。完整 HTTP URL 无需填写默认设备。“Pro 手柄 / 键盘与文字”手动栏默认隐藏，可以在设置中打开。窗口默认置顶。

## 项目与设置

```text
TextSenderStudio.exe
settings.json                 全局设置，运行时生成
projects/
  我的项目/
    project.json              短语、脚本快捷方式、HTTP 动作、声音、标签配置
    main.ecs                  主脚本
    draft.txt                 普通文字草稿
    labels/                   标签图片
    scripts/                  可存放启动脚本
```

切换项目时保存编辑器内容并载入对应资源。项目可自定义显示名称；删除项目会将其文件夹移入回收站，不删除引用的外部脚本。

短语、启动脚本、HTTP 动作和声音提醒的函数名在当前项目中唯一。脚本等待单位全部为毫秒；发送前等待为 `0` 时立即开始发送，USB 和网络本身仍有传输时间。

## 设备连接

- 新版 Wi-Fi 固件：ESP32-S3、16 MB Flash，原生 USB/OTG 接目标设备，UART 口使用独立稳定电源。
- 固件 0.1.1 首次连接无密码热点 `TextSender-Setup`，在弹出的配网页面选择 Wi-Fi 并填写密码；未自动弹出时访问 `http://192.168.4.1`。成功后显示设备 IP，之后重新上电自动连接。正常启动后长按 BOOT 5 秒可清除配网。
- [固件说明](docs/firmware.md)包含热点信息、构建/刷写步骤与协议。
- 串口模式供原有 TextKeyboard TYPE2/HID 固件使用；手柄和 Wi-Fi 配网需新版固件。
- [键盘直通](docs/input.md)仅在直通窗口前台启用，支持多键，并在失焦、停止或错误时释放输入。

电脑端只在用户发起操作时连接设备。设备 IP、Wi-Fi 密码、用户项目和日志不随源码或发布包分发。

## 画面与脚本

支持采集卡/摄像头和桌面选区。先开始画面预览，再选择全画面或指定识图范围；可以拖动框选或输入像素坐标。制作标签时另外框选目标图片，避免将“目标模板”与“搜索范围”混淆。

模板匹配支持彩色或灰度。标签尺寸必须不超过搜索范围，纯色模板会提示重新选择。详情见 [采集与识图](docs/vision.md)。

脚本使用 EasyCon 的编译器和 ECX 解释器，支持核心条件、循环、函数、按键、摇杆与图像标签语法。Studio 提供配置动作函数、`SEND_TEXT` 和 `KEYBOARD` 扩展。具体语法与尚未接入的宿主能力见 [脚本说明](docs/scripting.md)。

编辑器中 `Ctrl+Space` 打开补全，`Enter` 自动缩进，`Tab` / `Shift+Tab` 调整多行缩进，双击函数或按 `F12` 跳转。语法说明可展开、复制并插入示例。声音提醒可选择 Windows 提示音或音乐文件；`ALERT` 使用设置中启用的 PushPlus、Bark 或自定义 Webhook。

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

### 单文件 EXE

双击 `build-singlefile.cmd`，或运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./build-singlefile.ps1
```

生成 `artifacts/TextSenderStudio-singlefile-win-x64/TextSenderStudio.exe`，包含 .NET、WPF 和识图运行库，复制这一个 EXE 到可写目录即可运行，无需另装 .NET 或携带 DLL。构建脚本只生成本地文件，不上传发布包。

运行时会按需生成 `settings.json` 和 `projects/`，仍保存在 EXE 同目录；首次启动时原生运行库由 .NET 自动解压到用户临时缓存。该行为遵循 [.NET 单文件部署](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)。更新 EXE 不会删除已有项目和设置。源码中的 `LICENSE`、`THIRD_PARTY.md` 和 `licenses/` 保留对应许可信息。

## 当前验证范围

桌面版 `0.2.0`：已进行桌面构建、项目/设置与脚本测试、合成图匹配、模拟 HTTP/串口输入、声音调用与取消、推送模板及界面交互检查。界面按 1280 × 900 和 1000 × 720 检查。声音检查使用模拟播放器，推送检查使用模拟服务器；真实音频设备、外部推送服务和目标硬件效果需使用自己的配置验证。此更新不改动单片机固件。

## 许可证

桌面程序采用 GPL-3.0，包含 EasyCon 编译器/解释器对应源码。第三方来源和许可证见 [THIRD_PARTY.md](THIRD_PARTY.md)。固件的独立来源及许可说明位于 `firmware/NOTICE.md`。
