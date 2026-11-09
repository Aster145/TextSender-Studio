# 固件文件来源与许可

此目录固件独立于桌面端所使用的 EasyCon 代码。适用许可按文件列出；没有将未声明许可的第三方文件改标为 MIT 或 GPL。

| 文件 | 当前来源 | 许可与保留声明 |
| --- | --- | --- |
| `TextSenderBridge/TextSenderBridge.ino` | 本项目新实现：HTTP 表单接口、任务状态机、输入租期及独立 USB 工作任务 | MIT，见本目录 `LICENSE` |
| `TextSenderBridge/StudioWifi.h`、`StudioWifi.cpp` | 本项目新实现：Wi-Fi 配网、NVS 保存、启动重连、配网热点与 BOOT 重置 | MIT，见本目录 `LICENSE` |
| `TextSenderBridge/StudioWifiTiming.h`、`tests/wifi_timing_checks.cpp` | 本项目新实现：可离线验证的配网时序与按键策略 | MIT，见本目录 `LICENSE` |
| `TextSenderBridge/WifiPortalPage.h` | 本项目新实现的离线配网页面 | MIT，见本目录 `LICENSE` |
| `TextSenderBridge/StudioInput.h` | 本项目新实现：USB 八字节报告结构、美式 ASCII 到标准 HID usage 的映射 | MIT，见本目录 `LICENSE` |
| `TextSenderBridge/StudioUsb.h` | 本项目新实现的 USB 适配接口 | MIT，见本目录 `LICENSE` |
| `TextSenderBridge/StudioUsb.cpp` | 本项目基于 Espressif 扩展接口与 TinyUSB 官方 HID 驱动请求/端点流程实现的双接口适配 | 本项目部分 MIT；TinyUSB 派生部分保留 Ha Thach 的 MIT，见 `TINYUSB-LICENSE.txt` |
| `TextSenderBridge/SwitchDescriptor.h` | 来自 `esp32beans/switch_ESP32` 的手柄 HID 描述符，由参考项目携带 | 文件内完整保留 `Copyright (c) 2023 esp32beans@gmail.com` 与 MIT 条款；二进制发行附 `SWITCH-DESCRIPTOR-LICENSE.txt` |
| `build.ps1` | 本项目构建入口 | MIT，见本目录 `LICENSE` |

官方 API/源文件：

- [TinyUSB hid_device.c](https://github.com/hathach/tinyusb/blob/master/src/class/hid/hid_device.c)，MIT。
- [Espressif Arduino ESP32 3.3.8 USB 扩展接口](https://github.com/espressif/arduino-esp32/blob/3.3.8/cores/esp32/esp32-hal-tinyusb.h)。代码仅调用安装工具链提供的 API，未复制 Espressif 头文件。
- [esp32beans/switch_ESP32](https://github.com/esp32beans/switch_ESP32)，描述符内的原始声明已保留。

## 参考仓库处理记录

设计曾参考 [easyworld/switch2-dual-bridge](https://github.com/easyworld/switch2-dual-bridge) 的公开协议和用户提供的源码，提交 `14490631165177c358dde68421af0ce1e8d90e2b`。其根目录没有统一 LICENSE；`DualUSB` 仅说明参考 TinyUSB，`ControllerLogic.h`、`PadStream.h`、`KeyboardMode.h`、`keymap.h` 和主要 sketch 没有许可声明。

因此本发行版不包含上述无明确许可声明的实现文件。主逻辑、队列、键盘映射与 USB 适配已在本项目重新实现；只保留带明确 MIT 声明的 `SwitchDescriptor.h` 和 TinyUSB MIT 许可文本。HTTP 参数形状用于协议互通，不意味着沿用参考仓库未声明许可的实现。

固件构建会链接已安装的 Arduino ESP32、ESP-IDF、FreeRTOS、TinyUSB 等工具链组件；这些组件保持各自的上游许可。发行时应连同本目录的来源与许可文件提供。
