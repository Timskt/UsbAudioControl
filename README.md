# UsbAudioControl

USB 音频设备静音控制库，支持 HID 设备物理按键监听和 LED 控制。

## 支持设备

- **Hamedal Speak A20** (VID: 0x1FC9, PID: 0x826B)

## 功能特性

- 控制 LED 静音指示灯
- 监听物理按键状态变化
- 支持通过 VID/PID 连接设备
- 本地状态追踪（设备使用硬件静音，不通过 Windows Core Audio）

## 安装

```
dotnet add package UsbAudioControl
```

## 快速开始

```csharp
using UsbAudioControl;

// 创建控制器
using var controller = new HidAudioController();

// 连接设备
if (!controller.ConnectToFirst())
{
    Console.WriteLine("连接失败");
    return;
}

// 订阅状态变化事件
controller.StateChanged += (sender, e) =>
{
    var status = e.IsMuted ? "静音" : "启用";
    Console.WriteLine("状态变化: " + status);
};

// 开始监听物理按键
controller.StartMonitoring();

// 控制静音
controller.SetMute(true);   // 静音 (LED 亮)
controller.SetMute(false);  // 取消静音 (LED 灭)
controller.ToggleMute();    // 切换状态
```

## API 参考

### HidAudioController

| 方法 | 说明 |
|------|------|
| ConnectToFirst() | 连接第一个可用设备 |
| ConnectByVidPid(int vid, int pid) | 通过 VID/PID 连接 |
| SetMute(bool mute) | 设置静音状态 |
| ToggleMute() | 切换静音状态 |
| GetMute() | 获取当前静音状态 |
| StartMonitoring() | 开始监听物理按键 |
| StopMonitoring() | 停止监听 |

### 事件

| 事件 | 说明 |
|------|------|
| StateChanged | 静音状态变化时触发 |

### AudioStateChangedEventArgs

| 属性 | 类型 | 说明 |
|------|------|------|
| IsMuted | bool | 是否静音 |
| Volume | float | 音量 (0.0-1.0) |
| Device | AudioDeviceInfo | 设备信息 |

## 协议说明

Hamedal Speak A20 使用 HID 协议控制：

- Report ID: 3
- 静音命令: Data=0x05
- 取消静音: Data=0x01
- 按键事件: Report ID=1, Data=0x07 (按下), 0x03 (释放)

## 依赖

- .NET 8.0
- HidLibrary (3.3.40)
- NAudio (Core Audio API)

## 运行示例

```bash
cd UsbAudioControl.Example
dotnet run
```

命令:
- m = 静音
- u = 取消静音  
- t = 切换
- s = 查看状态
- q = 退出

## 许可证

MIT License
