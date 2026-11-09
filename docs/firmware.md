# ESP32-S3 固件与连接

适用硬件：ESP32-S3、16 MB Flash、原生 USB OTG 接口。电脑通过 Wi-Fi 控制；原生 USB 数据线接 Switch 底座，UART 接口使用独立稳定电源。主机开启 Pro 手柄有线通信。

## 首次连接

1. 刷入 `TextSenderBridge` 固件后连接热点 **TextSender-Setup**，密码 **TextSender32**。
2. 在程序设置填写 ESP32 IP `192.168.4.1`、端口 `80`，点击首次 Wi-Fi 配网，填写路由器的 SSID/密码。也可以浏览器打开 `http://192.168.4.1` 完成配置。
3. SSID/密码保存在板子 NVS，重新上电自动连接。`GET /status` 的 `station_ip` 为加入路由器后的地址；电脑切回同一 Wi-Fi 后，将程序中的 ESP32 IP 改为该地址。可在路由器设置 DHCP 地址保留。
4. 原生 USB 枚举后有约 1 秒稳定等待。键盘与手柄是同时存在的两个 USB HID 接口，无需重新枚举切换。

热点在路由器连接后仍保留，便于重新配置。控制接口按需求免令牌验证。Wi-Fi 密码仅提交给板子，不保存在程序配置或日志。

## 构建

使用 Arduino CLI 和 ESP32 core **3.3.8**：

```powershell
arduino-cli core install esp32:esp32@3.3.8
powershell -NoProfile -ExecutionPolicy Bypass -File .\firmware\build.ps1
```

可使用 `-ArduinoCli '完整路径\arduino-cli.exe'`。构建选择 ESP32-S3、16 MB、DIO、USB OTG、CDC 关闭、PSRAM 关闭。结果在 `firmware/binaries/`，不会自动刷写。

发布包包含合并固件 `TextSenderBridge.ino.merged.bin`，在解压后的固件文件夹中执行（将 COM 端口换为自己的板子）：

```powershell
python -m esptool --chip esp32s3 --port COM15 write-flash --flash-mode dio --flash-size 16MB 0x0 TextSenderBridge.ino.merged.bin
```

合并固件已经包含引导程序、分区表及应用，只写一次 `0x0` 即可。不要再把它写到 `0x10000`。自源码构建后，也可以使用构建目录中的独立分区文件刷写：

```powershell
python -m esptool --chip esp32s3 --port COM15 write-flash --flash-mode dio --flash-size 16MB 0x0 firmware/binaries/TextSenderBridge.ino.bootloader.bin 0x8000 firmware/binaries/TextSenderBridge.ino.partitions.bin 0xe000 firmware/binaries/boot_app0.bin 0x10000 firmware/binaries/TextSenderBridge.ino.bin
```

按住 BOOT、短按 RESET、松开 BOOT 可进入下载模式。此版本已完成本机编译，Switch 端 USB/按键效果仍需连接实机验证。

## 协议

所有 POST 使用 `application/x-www-form-urlencoded`。执行请求失败后客户端不会自动重发；输入任务使用随机 `X-Job`，状态轮询确认同一启动实例及任务。

| 接口 | 参数 / 用途 |
| --- | --- |
| `GET /status` | 固件版本、boot_id、键盘任务、gamepad、USB 状态、station_ip |
| `POST /wifi` | `ssid,password`，保存并加入路由器 |
| `POST /text` | `plain,gap,line,layout=us`；1–16384 个 ASCII 字符，Tab/换行可用；gap/line 为 0–60000 ms |
| `POST /tap` | `key,mod,gap,line=0`；单键加修饰键 |
| `POST /pad` | `buttons,dpad,lx,ly,rx,ry,hold,epoch`；独立有限按压任务 |
| `POST /pad/stream` | `session,sequence,epoch,frames`；保持手柄状态及续租 |
| `POST /keyboard/report` | `session,sequence,mod,k0..k5`；最多六个普通键及八个修饰键 |
| `POST /cancel` | 停止任务、释放键盘和手柄，保留 USB 连接 |
| `POST /disconnect`,`POST /connect` | 手动断开、恢复原生 USB |

手柄帧：`buttons,dpad,lx,ly,rx,ry,duration_ms`，分号分隔，最多 8 帧；空 frames 续租。序号从 0 连续递增，session 为 32 位十六进制随机值。摇杆中心 128，十字键中立 15，上/右上/右/右下/下/左下/左/左上为 0–7。按钮位：Y=1、B=2、A=4、X=8、L=16、R=32、ZL=64、ZR=128、Minus=256、Plus=512、L3=1024、R3=2048、Home=4096、Capture=8192。

主机每 150 ms 续租；固件在 600 ms 无有效续租或 USB 失联时释放保持状态。输入停止/窗口失焦必须显式释放，租期作为断网后的兜底。零等待表示不添加人为延迟，USB 调度与网络传输仍占用时间。键盘文本使用美式布局；主机键盘布局须对应。

## 来源

主要固件实现为本项目新代码；USB 适配使用 Espressif 官方 API 与 TinyUSB 官方 HID 驱动流程。参考仓库中没有明确许可声明的实现文件已移除，仅保留明确 MIT 的手柄描述符。逐文件来源、原始版权声明及许可参见 `firmware/NOTICE.md`、`firmware/LICENSE` 和 `firmware/TINYUSB-LICENSE.txt`。
