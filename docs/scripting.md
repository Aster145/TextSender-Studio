# 脚本与函数

TextSender Studio 使用 EasyCon 的实际词法分析器、编译器和 ECX 托管解释器。等待和输入时长均为**毫秒**；`WAIT 0` 立即继续。脚本在后台执行，“停止”取消等待与运行，手柄保持状态在结束时释放。

## 已接入的语法与能力

- EasyCon 变量、算术/比较/逻辑表达式、数组、字符串、类型和函数语法。
- `IF / ELIF / ELSE / ENDIF`、`FOR / NEXT`、`WHILE / UNTIL`、`BREAK / CONTINUE`。
- `FUNC / ENDFUNC / RETURN` 和 `CALL`，参数及返回值按 EasyCon 规则解析。
- `WAIT`、普通手柄按键、`DOWN / UP` 保持状态、左右摇杆及定时回中。
- `PRINT` / `ALERT` 输出到程序日志；`TIME`、`RAND` 和解释器的内建运算。
- `@标签名` 调用当前项目的图像标签，返回整数匹配度 **0–100**。标签的图片、搜索范围和匹配方式在“图像标签”中设置。
- 当前项目的常用短语、启动脚本和 HTTP 动作作为无参数函数，用 `CALL 函数名` 调用。

这是语言语法兼容和上述宿主能力的接入，并不等于支持 EasyCon 的所有硬件与扩展。Amiibo、OCR、ONNX 推理、`FRAME` / `ROI` 图像对象函数、脚本文件句柄 API、交互式标准输入和外部 DLL 的原生函数尚未接入；执行到它们会报告不支持。多文件模块和旧版 `.IL` 标签格式不在本版导入范围内。

## 基础脚本

```text
# 单位为毫秒
WAIT 500
A 100

$score = @完成提示
PRINT 匹配度: & $score
IF $score >= 90
    PRINT 找到目标图片
ELSE
    PRINT 未找到目标图片
ENDIF
```

按住并同时组合多个键：

```text
L DOWN
R DOWN
WAIT 100
L UP
R UP
```

摇杆使用原 EasyCon 语法，例如 `LS UP,100`，表示向上推左摇杆 100 ms 后回中。状态保持需要当前设备连接支持完整手柄状态；不支持时会报告错误。

## 项目动作函数

在相应管理页面设置唯一的函数名，再通过“插入函数”插入调用：

```text
CALL phrase_hello
CALL start_tools
CALL robot_tap
```

这些函数不需要在脚本中重复写 `FUNC`。运行前由 Studio 注册为当前项目的外部动作，按顺序执行。启动脚本是否等待退出由对应快捷脚本设置决定。未配置的函数名会在编译时报告错误。

函数名使用字母、数字、下划线，并以字母或下划线开头；三个动作分类共用命名空间。不要与脚本中已定义的函数、`SEND_TEXT`、`KEYBOARD` 或语言内建函数重名。

## 文字和键盘扩展

Studio 新增两个使用 EasyCon 函数调用语法的宿主函数：

```text
SEND_TEXT "Hello"
KEYBOARD "CTRL+S", 50
```

`SEND_TEXT` 使用全局设置的发送等待、逐字间隔和换行等待。`KEYBOARD` 第二个参数为按键持续毫秒数。输入内容和按键能否被目标接受，取决于固件支持的字符映射与目标应用。

## 保存与错误

主脚本保存在当前项目目录（默认 `main.ecs`）；启动脚本可放在项目的 `scripts` 子目录。可以加载、编辑、保存，并在运行前检查语法。错误信息尽可能显示原始脚本行号；缺失标签、设备错误和 HTTP 错误会停止当前执行并写入日志，不会自行重试不确定的设备动作。

## 来源

编译器与解释器来自 [EasyCon](https://github.com/EasyConNS/EasyCon)，快照和许可证见 [vendor/EasyCon.Script/NOTICE.md](../vendor/EasyCon.Script/NOTICE.md)。Studio 宿主适配位于 `src/TextSenderStudio.Core/Scripting/`。本项目使用自己的项目和标签存储格式，不读写旧 TextSender 配置。
