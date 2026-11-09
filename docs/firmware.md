# ESP32-S3 固件与连接

适用硬件：ESP32-S3、16 MB Flash、原生 USB OTG 接口。电脑通过 Wi-Fi 控制；原生 USB 数据线接 Switch 底座，UART 接口使用独立稳定电源。主机开启 Pro 手柄有线通信。

## 首次连接

1. 固件 **0.1.1** 未配网时开启 **TextSender-Setup** 热点，**无需密码**。如果手机记住了旧版有密码的热点，请先忘记该网络，再重新连接。
2. 连接热点后，支持网络登录检测的手机/电脑会提示打开配网页面；未出现提示时，手动打开 `http://192.168.4.1`。页面无需互联网，自动搜索附近的 **2.4 GHz Wi-Fi**，选择名称并填写密码即可。隐藏网络可展开手动填写名称。
3. 保存后页面显示连接进度、失败原因或成功获得的 IP。成功后记下 IP，将电脑切回同一 Wi-Fi，在程序设置中填写该 ESP32 IP、端口 `80`。也可查看 `http://192.168.4.1/wifi/status` 或 `/status` 的 `station_ip`。可在路由器中设置 DHCP 地址保留。
4. 原生 USB 枚举后有约 1 秒稳定等待。键盘与手柄是同时存在的两个 USB HID 接口，无需重新枚举切换。

已有配置时，重新上电会直接连接路由器，正常连接成功不开放配网热点。30 秒内未连上时开放热点供排查，并继续尝试重连。配网热点开启期间连接成功，会保留 120 秒供查看 IP，然后自动关闭；也可以在页面点击结束配网。系统是否自动弹出页面取决于其网络检测行为，手动 HTTP 入口始终可用于配网热点。

Wi-Fi 配置保存在板子 NVS，使用一个记录整体写入，避免 Wi-Fi 名称与密码分开保存时出现不匹配；支持读取旧版 `tsstudio` 配置。控制接口按需求免令牌验证，Wi-Fi 密码不返回到网页、不写入程序配置或日志。

## 更换 Wi-Fi / 清除配网

板子正常启动后，先松开 **BOOT**，再持续按住 **BOOT 5 秒**，清除已保存的 Wi-Fi 并重新开放无密码配网热点。该操作只清除 Wi-Fi 配置，不擦除固件。**RESET/EN 只负责重启，长按不会清除配置。** 不要在按住 BOOT 时重新上电或按 RESET，那会进入下载模式。

连接失败时，页面会显示状态与断开原因。确认路由器开启 2.4 GHz 网络；ESP32-S3 不支持连接仅 5 GHz 的网络。0.1.0 的热点一直开启，单凭看到旧热点不能判断是否连上路由器。

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

已经刷过本项目 0.1.0 固件、分区布局不变时，可只更新应用以保留 NVS 配网记录：

```powershell
python -m esptool --chip esp32s3 --port COM15 write-flash 0x10000 firmware/binaries/TextSenderBridge.ino.bin
```

这里使用的是应用文件 `.ino.bin`，不是 `.ino.merged.bin`；写入完整合并镜像或擦除闪存后可能需要重新配网。

按住 BOOT、短按 RESET、松开 BOOT 可进入下载模式。此版本已完成本机编译，Switch 端 USB/按键效果仍需连接实机验证。

## 协议

所有 POST 使用 `application/x-www-form-urlencoded`。执行请求失败后客户端不会自动重发；输入任务使用随机 `X-Job`，状态轮询确认同一启动实例及任务。

| 接口 | 参数 / 用途 |
| --- | --- |
| `GET /status` | 固件版本、boot_id、键盘任务、gamepad、USB 状态、station_ip |
| `POST /wifi` | `ssid,password`，保存并加入路由器 |
| `GET /wifi/status` | 配网状态、当前 SSID、station_ip、失败原因、重试与热点关闭倒计时；不含密码 |
| `GET /wifi/scan` | 异步搜索附近网络；`?refresh=1` 重新搜索；扫描中返回 202 |
| `POST /wifi/finish` | 已连接路由器时提前结束配网热点 |
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
