using HidLibrary;
using UsbAudioControl;

Console.WriteLine("=== Hamedal Speak A20 静音控制 ===\n");

using var controller = new HidAudioController();

Console.WriteLine("连接设备...");
if (!controller.ConnectToFirst())
{
    Console.WriteLine("连接失败!");
    return;
}

Console.WriteLine($"已连接: {controller.ConnectedDevice?.Name}\n");

// 订阅状态变化事件
controller.StateChanged += (sender, e) =>
{
    Console.WriteLine($"\n[状态变化] 静音={e.IsMuted}, 音量={e.Volume:P0}");
};

// 开始监听
controller.StartMonitoring();
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
            return;

        default:
            Console.WriteLine($"未知命令: {cmd}");
            break;
    }
    
    Console.WriteLine();
}
