using UsbAudioControl;

Console.WriteLine("=== HID 音频设备静音控制 ===\n");

// 显示已注册的设备
Console.WriteLine("已注册的设备配置:");
foreach (var id in HidAudioConfigRegistry.GetRegisteredDeviceIds())
{
    var cfg = HidAudioConfigRegistry.GetConfig(id);
    Console.WriteLine($"  {id} - {cfg?.Name}");
}
Console.WriteLine();

// 自动检测当前系统麦克风并连接
Console.WriteLine("检测当前系统麦克风...");
var controller = HidAudioController.ConnectAuto();
if (controller == null)
{
    Console.WriteLine("未找到匹配的设备配置!");
    Console.WriteLine("请确保你的麦克风已在 HidAudioConfigRegistry 中注册");
    return;
}
Console.WriteLine($"已连接: {controller.ConnectedDevice?.Name}");
Console.WriteLine($"配置: {controller.Config.Name} ({controller.Config.DeviceId})\n");

// 订阅状态变化事件
controller.StateChanged += (sender, e) =>
{
    Console.WriteLine($"\n[状态变化] 静音={e.IsMuted}, 音量={e.Volume:P0}");
};

// 开始监听
controller.StartMonitoring();
controller.SetMute(true);
Console.WriteLine("已开始监听物理按键\n");

Console.WriteLine("命令:");
Console.WriteLine("  m = 静音");
Console.WriteLine("  u = 取消静音");
Console.WriteLine("  t = 切换");
Console.WriteLine("  s = 查看状态");
Console.WriteLine("  q = 退出");
Console.WriteLine();

while (true)
{
    Console.Write("> ");
    var cmd = Console.ReadLine()?.Trim().ToLower();
    
    if (string.IsNullOrEmpty(cmd))
        continue;

    switch (cmd)
    {
        case "m":
            if (controller.SetMute(true))
                Console.WriteLine("已静音");
            else
                Console.WriteLine("静音失败");
            break;

        case "u":
            if (controller.SetMute(false))
                Console.WriteLine("已取消静音");
            else
                Console.WriteLine("取消静音失败");
            break;

        case "t":
            var result = controller.ToggleMute();
            if (result.HasValue)
                Console.WriteLine($"切换成功: {(result.Value ? "静音" : "启用")}");
            else
                Console.WriteLine("切换失败");
            break;

        case "s":
            Console.WriteLine($"设备: {controller.ConnectedDevice?.Name}");
            Console.WriteLine($"状态: {(controller.GetMute() == true ? "静音" : "启用")}");
            break;

        case "q":
            Console.WriteLine("退出");
            controller.Dispose();
            controller = HidAudioController.ConnectAuto();
            if (controller == null)
            {
                Console.WriteLine("未找到匹配的设备配置!");
                Console.WriteLine("请确保你的麦克风已在 HidAudioConfigRegistry 中注册");
                return;
            }
            Console.WriteLine($"已连接: {controller.ConnectedDevice?.Name}");
            Console.WriteLine($"配置: {controller.Config.Name} ({controller.Config.DeviceId})\n");

// 订阅状态变化事件
            controller.StateChanged += (sender, e) =>
            {
                Console.WriteLine($"\n[状态变化] 静音={e.IsMuted}, 音量={e.Volume:P0}");
            };

// 开始监听
            controller.StartMonitoring();
            controller.SetMute(true);
            // return;
            break;

        default:
            Console.WriteLine($"未知命令: {cmd}");
            break;
    }
    
    Console.WriteLine();
}


