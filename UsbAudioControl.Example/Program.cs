using UsbAudioControl;
using NAudio.CoreAudioApi;

Console.WriteLine("=== 麦克风静音控制 ===\n");

using var controller = new WindowsCoreAudioController();

if (!controller.ConnectToFirst())
{
    Console.WriteLine("连接失败，没有找到音频输入设备");
    return;
}

Console.WriteLine($"设备: {controller.ConnectedDevice?.Name}");
Console.WriteLine($"当前静音: {(controller.GetMute() == true ? "是" : "否")}");
Console.WriteLine($"当前音量: {controller.GetVolume():P0}");
Console.WriteLine();

// 启动状态监听
controller.StateChanged += (sender, e) =>
{
    Console.WriteLine($"\n[事件] 状态变化: 静音={e.IsMuted}, 音量={e.Volume:P0}, 时间={e.Timestamp:HH:mm:ss.fff}");
};

controller.StartMonitoring();
Console.WriteLine("已启动状态监听 (可在 Windows 设置中手动切换静音来触发事件)");
Console.WriteLine();

Console.WriteLine("命令:");
Console.WriteLine("  m = 静音");
Console.WriteLine("  u = 取消静音");
Console.WriteLine("  t = 切换");
Console.WriteLine("  v = 设置音量 50%");
Console.WriteLine("  s = 查看状态");
Console.WriteLine("  q = 退出");
Console.WriteLine();

while (true)
{
    Console.Write("> ");
    var key = Console.ReadKey(true);
    var c = char.ToLower(key.KeyChar);

    switch (c)
    {
        case 'm':
            if (controller.SetMute(true))
            {
                Console.WriteLine($"设置静音成功，当前状态: {(controller.GetMute() == true ? "静音" : "未静音")}");
            }
            else
            {
                Console.WriteLine("设置静音失败");
            }
            break;

        case 'u':
            if (controller.SetMute(false))
            {
                Console.WriteLine($"取消静音成功，当前状态: {(controller.GetMute() == true ? "静音" : "未静音")}");
            }
            else
            {
                Console.WriteLine("取消静音失败");
            }
            break;

        case 't':
            var toggled = controller.ToggleMute();
            if (toggled.HasValue)
            {
                Console.WriteLine($"切换成功，当前状态: {(toggled.Value ? "静音" : "未静音")}");
            }
            else
            {
                Console.WriteLine("切换失败");
            }
            break;

        case 'v':
            if (controller.SetVolume(0.5f))
            {
                Console.WriteLine($"音量设置为 50%，当前音量: {controller.GetVolume():P0}");
            }
            else
            {
                Console.WriteLine("设置音量失败");
            }
            break;

        case 's':
            Console.WriteLine($"设备: {controller.ConnectedDevice?.Name}");
            Console.WriteLine($"静音: {(controller.GetMute() == true ? "是" : "否")}");
            Console.WriteLine($"音量: {controller.GetVolume():P0}");
            break;

        case 'q':
            Console.WriteLine("退出");
            controller.StopMonitoring();
            return;

        default:
            Console.WriteLine($"未知命令: {c}");
            break;
    }

    Console.WriteLine();
}
