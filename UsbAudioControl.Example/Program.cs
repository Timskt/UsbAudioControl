using UsbAudioControl;
using NAudio.CoreAudioApi;

Console.WriteLine("=== 音频静音控制测试 ===\n");

// 先用 Windows 系统设置验证
Console.WriteLine("=== 方案1: Windows Core Audio API ===");
using var controller = AudioControllerFactory.CreateWindowsCoreAudio();

var devices = controller.EnumerateDevices();
Console.WriteLine($"找到 {devices.Count} 个音频输入设备:");
foreach (var d in devices)
{
    Console.WriteLine($"  - {d.Name}");
}
Console.WriteLine();

if (!controller.ConnectToFirst())
{
    Console.WriteLine("连接失败！");
    return;
}

Console.WriteLine($"已连接: {controller.ConnectedDevice?.Name}\n");

// 读取初始状态
var initialMute = controller.GetMute();
var initialVolume = controller.GetVolume();
Console.WriteLine("[初始状态]");
Console.WriteLine($"  静音: {(initialMute == true ? "是" : initialMute == false ? "否" : "未知")}");
Console.WriteLine($"  音量: {(initialVolume.HasValue ? $"{initialVolume.Value:P0}" : "未知")}");
Console.WriteLine();

// 使用 NAudio 直接验证设备状态
Console.WriteLine("[直接验证 - 使用 NAudio]");
using var enumerator = new MMDeviceEnumerator();
var mmDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
if (mmDevice != null)
{
    Console.WriteLine($"  设备名称: {mmDevice.FriendlyName}");
    Console.WriteLine($"  静音状态: {mmDevice.AudioEndpointVolume.Mute}");
    Console.WriteLine($"  音量标量: {mmDevice.AudioEndpointVolume.MasterVolumeLevelScalar:P0}");
    Console.WriteLine($"  音量 dB: {mmDevice.AudioEndpointVolume.MasterVolumeLevel} dB");
    
    // 音量范围
    var volRange = mmDevice.AudioEndpointVolume.VolumeRange;
    Console.WriteLine($"  音量范围: {volRange.MinDecibels} dB ~ {volRange.MaxDecibels} dB");
}
Console.WriteLine();

// 测试静音
Console.WriteLine("按任意键测试静音...");
Console.ReadKey(true);

Console.WriteLine("\n>>> 设置静音 = true");
controller.SetMute(true);

// 等待一下让系统处理
Thread.Sleep(100);

// 再次验证
var afterMute = controller.GetMute();
var afterMuteNAudio = mmDevice?.AudioEndpointVolume.Mute;
Console.WriteLine("[静音后]");
Console.WriteLine($"  Controller.GetMute(): {(afterMute == true ? "是" : "否")}");
Console.WriteLine($"  NAudio 直接读取: {afterMuteNAudio}");
Console.WriteLine();

Console.WriteLine(">>> 请打开 Windows 设置 -> 系统 -> 声音 -> 查看麦克风是否静音");
Console.WriteLine("按任意键取消静音...");
Console.ReadKey(true);

Console.WriteLine("\n>>> 设置静音 = false");
controller.SetMute(false);
Thread.Sleep(100);

var finalMute = controller.GetMute();
var finalMuteNAudio = mmDevice?.AudioEndpointVolume.Mute;
Console.WriteLine("[取消静音后]");
Console.WriteLine($"  Controller.GetMute(): {(finalMute == true ? "是" : "否")}");
Console.WriteLine($"  NAudio 直接读取: {finalMuteNAudio}");
Console.WriteLine();

// 测试音量
Console.WriteLine("按任意键测试音量...");
Console.ReadKey(true);

Console.WriteLine("\n>>> 设置音量 = 50%");
controller.SetVolume(0.5f);
Thread.Sleep(100);

var afterVolume = controller.GetVolume();
var afterVolumeNAudio = mmDevice?.AudioEndpointVolume.MasterVolumeLevelScalar;
Console.WriteLine("[设置音量后]");
Console.WriteLine($"  Controller.GetVolume(): {(afterVolume.HasValue ? $"{afterVolume.Value:P0}" : "未知")}");
Console.WriteLine($"  NAudio 直接读取: {afterVolumeNAudio:P0}");
Console.WriteLine();

// 恢复
Console.WriteLine("按任意键恢复原状态并退出...");
Console.ReadKey(true);

if (initialMute.HasValue)
    controller.SetMute(initialMute.Value);
if (initialVolume.HasValue)
    controller.SetVolume(initialVolume.Value);

Console.WriteLine("已恢复原状态，测试完成！");
