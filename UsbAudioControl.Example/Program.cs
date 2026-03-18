using UsbAudioControl;
using NAudio.CoreAudioApi;

Console.WriteLine("=== 麦克风静音控制 ===");
Console.WriteLine();

using var enumerator = new MMDeviceEnumerator();
var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);

Console.WriteLine("设备: " + (device?.FriendlyName ?? "Unknown"));
Console.WriteLine("当前静音: " + device?.AudioEndpointVolume.Mute);
Console.WriteLine("当前音量: " + (int)((device?.AudioEndpointVolume.MasterVolumeLevelScalar ?? 0) * 100) + "%");
Console.WriteLine();

Console.WriteLine("命令: m=静音  u=取消静音  t=切换  q=退出");
Console.WriteLine();

while (true)
{
    Console.Write("> ");
    var key = Console.ReadKey(true);
    var c = char.ToLower(key.KeyChar);
    
    if (device == null)
    {
        Console.WriteLine("设备未连接");
        continue;
    }
    
    switch (c)
    {
        case 'm':
            device.AudioEndpointVolume.Mute = true;
            Console.WriteLine("静音 = ON (实际: " + device.AudioEndpointVolume.Mute + ")");
            break;
        
        case 'u':
            device.AudioEndpointVolume.Mute = false;
            Console.WriteLine("静音 = OFF (实际: " + device.AudioEndpointVolume.Mute + ")");
            break;
        
        case 't':
            var current = device.AudioEndpointVolume.Mute;
            device.AudioEndpointVolume.Mute = !current;
            Console.WriteLine("静音切换 -> 实际: " + device.AudioEndpointVolume.Mute);
            break;
        
        case 'q':
            Console.WriteLine("退出");
            return;
        
        default:
            Console.WriteLine("未知命令: " + c);
            break;
    }
    
    Console.WriteLine("  请在 Windows 设置 -> 系统 -> 声音 -> 输入 中查看变化");
    Console.WriteLine();
}