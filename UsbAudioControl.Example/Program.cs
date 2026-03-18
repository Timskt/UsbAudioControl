using UsbAudioControl;

Console.WriteLine("=== 音频静音控制测试 ===\n");

// 显示可用的控制方案
Console.WriteLine("可用控制方案:");
var controllers = AudioControllerFactory.GetAvailableControllers();
foreach (var (type, desc, available) in controllers)
{
    Console.WriteLine($"  [{(available ? "Y" : "N")}] {type}: {desc}");
}
Console.WriteLine();

// 测试 Windows 原生 USB 枚举（需要管理员权限）
Console.WriteLine("=== 测试 Windows 原生 USB 枚举 ===");
try
{
    using var winUsbController = new WindowsUsbAudioController();
    var usbDevices = winUsbController.EnumerateDevices();
    Console.WriteLine($"找到 {usbDevices.Count} 个 USB 设备:");
    
    foreach (var d in usbDevices)
    {
        Console.WriteLine($"  - {d.Name ?? "Unknown"}");
        Console.WriteLine($"    DeviceId: {d.DeviceId}");
        if (d.VendorId != 0)
        {
            Console.WriteLine($"    VID:PID: 0x{d.VendorId:X4}:0x{d.ProductId:X4}");
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"USB 枚举错误: {ex.Message}");
    if (ex.InnerException != null)
    {
        Console.WriteLine($"  内部异常: {ex.InnerException.Message}");
    }
}

Console.WriteLine("\n=== 使用最佳方案测试 ===");
try
{
    using var controller = AudioControllerFactory.CreateBest();
    Console.WriteLine($"控制器类型: {controller.GetType().Name}");

    if (!controller.ConnectToFirst())
    {
        Console.WriteLine("连接失败！");
        return;
    }

    Console.WriteLine($"已连接: {controller.ConnectedDevice?.Name ?? "Unknown"}\n");

    // 测试控制
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
catch (Exception ex)
{
    Console.WriteLine($"错误: {ex.Message}");
}