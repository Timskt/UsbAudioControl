using UsbAudioControl;

Console.WriteLine("=== 音频静音控制测试 (USB Audio 优先) ===\n");

// 先尝试 USB Audio 直连
Console.WriteLine("=== 尝试 USB Audio 直连 ===");
using var usbController = AudioControllerFactory.CreateUsbAudio();

var usbDevices = usbController.EnumerateDevices();
Console.WriteLine($"USB Audio 设备数量: {usbDevices.Count}");

if (usbDevices.Count > 0)
{
    for (int i = 0; i < usbDevices.Count; i++)
    {
        var d = usbDevices[i];
        Console.WriteLine($"  [{i}] {d.Name ?? "Unknown"}");
        Console.WriteLine($"      VID:PID: 0x{d.VendorId:X4}:0x{d.ProductId:X4}");
    }
    
    if (usbController.ConnectToFirst())
    {
        Console.WriteLine($"\nUSB Audio 连接成功: {usbController.ConnectedDevice?.Name}");
        TestController(usbController);
        return;
    }
}

Console.WriteLine("USB Audio 直连失败，尝试系统 API...\n");

// 回退到系统 API
Console.WriteLine("=== 使用系统音频 API ===");
using var systemController = AudioControllerFactory.CreateWindowsCoreAudio();

var devices = systemController.EnumerateDevices();
Console.WriteLine($"系统音频设备数量: {devices.Count}");

for (int i = 0; i < devices.Count; i++)
{
    var d = devices[i];
    Console.WriteLine($"  [{i}] {d.Name ?? "Unknown"}");
}

if (systemController.ConnectToFirst())
{
    Console.WriteLine($"\n系统 API 连接成功: {systemController.ConnectedDevice?.Name}");
    TestController(systemController);
}
else
{
    Console.WriteLine("没有找到任何可用的音频输入设备！");
}

static void TestController(IAudioMuteController controller)
{
    Console.WriteLine("\n=== 测试控制 ===");
    
    var currentMute = controller.GetMute();
    var currentVolume = controller.GetVolume();
    Console.WriteLine($"当前静音: {(currentMute == true ? "是" : currentMute == false ? "否" : "未知")}");
    Console.WriteLine($"当前音量: {(currentVolume.HasValue ? $"{currentVolume.Value:P0}" : "未知")}");
    
    Console.WriteLine("\n切换静音...");
    var newMute = controller.ToggleMute();
    Console.WriteLine($"切换后: {(newMute == true ? "静音" : newMute == false ? "未静音" : "失败")}");
    
    // 恢复
    if (currentMute.HasValue && newMute != currentMute)
    {
        controller.SetMute(currentMute.Value);
        Console.WriteLine($"已恢复: {(currentMute.Value ? "静音" : "未静音")}");
    }
    
    Console.WriteLine("\n测试完成！");
}