using HidLibrary;
using NAudio.CoreAudioApi;

namespace UsbAudioControl;

/// <summary>
/// HID 音频设备配置
/// </summary>
public class HidAudioConfig
{
    /// <summary>
    /// 配置名称
    /// </summary>
    public string Name { get; init; } = "Default";
    
    /// <summary>
    /// USB 厂商 ID (VID)
    /// </summary>
    public int VendorId { get; init; } = 0x1FC9;
    
    /// <summary>
    /// USB 产品 ID (PID)
    /// </summary>
    public int ProductId { get; init; } = 0x826B;
    
    /// <summary>
    /// HID 报告 ID
    /// </summary>
    public byte ReportId { get; init; } = 3;
    
    /// <summary>
    /// 静音命令数据
    /// </summary>
    public byte MuteOnData { get; init; } = 0x05;
    
    /// <summary>
    /// 取消静音命令数据
    /// </summary>
    public byte MuteOffData { get; init; } = 0x01;
    
    /// <summary>
    /// 按键报告 ID (用于监听物理按键)
    /// </summary>
    public byte ButtonReportId { get; init; } = 1;
    
    /// <summary>
    /// 按键按下数据
    /// </summary>
    public byte ButtonPressData { get; init; } = 0x07;
    
    /// <summary>
    /// 设备名称关键词 (用于匹配 Core Audio 设备)
    /// </summary>
    public string[]? DeviceNameKeywords { get; init; }
    
    /// <summary>
    /// 默认配置
    /// </summary>
    public static HidAudioConfig Default => new();
    
    /// <summary>
    /// 生成唯一标识 (VID:PID)
    /// </summary>
    public string DeviceId => $"{VendorId:X4}:{ProductId:X4}";
}

/// <summary>
/// HID 音频设备配置注册表
/// 添加新设备配置到这里
/// </summary>
public static class HidAudioConfigRegistry
{
    /// <summary>
    /// 所有已注册的设备配置
    /// 添加新麦克风时，在这里添加配置
    /// </summary>
    public static readonly Dictionary<string, HidAudioConfig> Configs = new()
    {
        // Hamedal Speak A20
        ["1FC9:826B"] = new HidAudioConfig
        {
            Name = "Hamedal Speak A20",
            VendorId = 0x1FC9,
            ProductId = 0x826B,
            ReportId = 3,
            MuteOnData = 0x05,
            MuteOffData = 0x01,
            ButtonReportId = 1,
            ButtonPressData = 0x07,
            DeviceNameKeywords = new[] { "Hamedal", "Speak", "A20", "回音消除" }
        },
        
        // 添加更多设备配置示例:
        // ["1234:5678"] = new HidAudioConfig
        // {
        //     Name = "其他品牌麦克风",
        //     VendorId = 0x1234,
        //     ProductId = 0x5678,
        //     ReportId = 2,
        //     MuteOnData = 0x01,
        //     MuteOffData = 0x00,
        //     ButtonReportId = 1,
        //     ButtonPressData = 0x01,
        //     DeviceNameKeywords = new[] { "Other", "Mic" }
        // },
    };
    
    /// <summary>
    /// 根据 VID/PID 获取配置
    /// </summary>
    public static HidAudioConfig? GetConfig(int vendorId, int productId)
    {
        var key = $"{vendorId:X4}:{productId:X4}";
        return Configs.TryGetValue(key, out var config) ? config : null;
    }
    
    /// <summary>
    /// 根据 VID/PID 获取配置
    /// </summary>
    public static HidAudioConfig? GetConfig(string deviceId)
    {
        return Configs.TryGetValue(deviceId.ToUpper(), out var config) ? config : null;
    }
    
    /// <summary>
    /// 添加或更新配置
    /// </summary>
    public static void Register(HidAudioConfig config)
    {
        Configs[config.DeviceId] = config;
    }
    
    /// <summary>
    /// 获取所有已注册的设备 ID
    /// </summary>
    public static IEnumerable<string> GetRegisteredDeviceIds() => Configs.Keys;
    
    /// <summary>
    /// 获取当前系统默认输入麦克风，返回匹配的配置
    /// </summary>
    public static HidAudioConfig? GetDefaultMicrophoneConfig()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            
            if (defaultDevice == null)
                return null;
            
            var deviceName = defaultDevice.FriendlyName;
            Console.WriteLine($"当前默认麦克风: {deviceName}");
            
            // 方式1: 通过设备路径提取 VID/PID
            var (vid, pid) = ExtractVidPidFromDevicePath(defaultDevice.ID);
            
            if (vid.HasValue && pid.HasValue)
            {
                Console.WriteLine($"提取 VID/PID: {vid.Value:X4}:{pid.Value:X4}");
                var config = GetConfig(vid.Value, pid.Value);
                if (config != null)
                    return config;
            }
            
            // 方式2: 通过设备名称关键词匹配
            foreach (var cfg in Configs.Values)
            {
                if (cfg.DeviceNameKeywords != null)
                {
                    if (cfg.DeviceNameKeywords.Any(k => deviceName?.Contains(k, StringComparison.OrdinalIgnoreCase) == true))
                    {
                        Console.WriteLine($"通过名称匹配: {cfg.Name}");
                        return cfg;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"获取默认麦克风失败: {ex.Message}");
        }
        
        return null;
    }
    
    /// <summary>
    /// 从设备路径中提取 VID 和 PID
    /// </summary>
    private static (int? Vid, int? Pid) ExtractVidPidFromDevicePath(string devicePath)
    {
        if (string.IsNullOrEmpty(devicePath))
            return (null, null);
        
        var vidMatch = System.Text.RegularExpressions.Regex.Match(
            devicePath, @"VID_([0-9a-fA-F]{4})", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var pidMatch = System.Text.RegularExpressions.Regex.Match(
            devicePath, @"PID_([0-9a-fA-F]{4})", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        if (!vidMatch.Success || !pidMatch.Success)
            return (null, null);
        
        return (
            Convert.ToInt32(vidMatch.Groups[1].Value, 16),
            Convert.ToInt32(pidMatch.Groups[1].Value, 16)
        );
    }
    
    /// <summary>
    /// 扫描系统中的 HID 设备，返回匹配的配置
    /// </summary>
    public static List<(HidAudioConfig Config, string DevicePath)> ScanDevices()
    {
        var result = new List<(HidAudioConfig, string)>();
        
        foreach (var config in Configs.Values)
        {
            var devices = HidDevices.Enumerate(config.VendorId, config.ProductId)
                .Where(d => d.Capabilities.OutputReportByteLength > 0)
                .ToList();
            
            foreach (var device in devices)
            {
                result.Add((config, device.DevicePath));
                device.CloseDevice();
            }
        }
        
        return result;
    }
    
    /// <summary>
    /// 判断当前系统默认麦克风是否为已注册的 HID 麦克风
    /// </summary>
    public static bool IsDefaultMicrophoneHid()
    {
        return GetDefaultMicrophoneConfig() != null;
    }
    
    /// <summary>
    /// 通过麦克风名称判断是否为已注册的 HID 麦克风
    /// </summary>
    /// <param name="deviceName">麦克风设备名称</param>
    /// <returns>是否为已注册的 HID 麦克风</returns>
    public static bool IsHidMicrophone(string? deviceName)
    {
        if (string.IsNullOrEmpty(deviceName))
            return false;
        
        // 通过名称关键词匹配
        foreach (var cfg in Configs.Values)
        {
            if (cfg.DeviceNameKeywords != null)
            {
                if (cfg.DeviceNameKeywords.Any(k => deviceName.Contains(k, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// 通过麦克风名称获取设备的 VendorId 和 ProductId
    /// </summary>
    /// <param name="deviceName">麦克风设备名称</param>
    /// <returns>(VendorId, ProductId) 或 null</returns>
    public static (int VendorId, int ProductId)? GetVidPidByName(string? deviceName)
    {
        if (string.IsNullOrEmpty(deviceName))
            return null;
        
        // 通过名称关键词匹配
        foreach (var cfg in Configs.Values)
        {
            if (cfg.DeviceNameKeywords != null)
            {
                if (cfg.DeviceNameKeywords.Any(k => deviceName.Contains(k, StringComparison.OrdinalIgnoreCase)))
                {
                    return (cfg.VendorId, cfg.ProductId);
                }
            }
        }
        
        return null;
    }
    
    /// <summary>
    /// 通过 Core Audio 设备 ID 获取设备的 VendorId 和 ProductId
    /// </summary>
    /// <param name="deviceId">Core Audio 设备 ID (如 MMDevice.ID)</param>
    /// <returns>(VendorId, ProductId) 或 null</returns>
    public static (int VendorId, int ProductId)? GetVidPidByDeviceId(string? deviceId)
    {
        if (string.IsNullOrEmpty(deviceId))
            return null;
        
        var (vid, pid) = ExtractVidPidFromDevicePath(deviceId);
        if (vid.HasValue && pid.HasValue)
        {
            return (vid.Value, pid.Value);
        }
        
        return null;
    }
    
    /// <summary>
    /// 获取当前系统默认麦克风的信息（包含 VID/PID）
    /// </summary>
    /// <returns>麦克风信息或 null</returns>
    public static (string Name, int? VendorId, int? ProductId, bool IsHid)? GetDefaultMicrophoneInfo()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            
            if (defaultDevice == null)
                return null;
            
            var deviceName = defaultDevice.FriendlyName;
            var (vid, pid) = ExtractVidPidFromDevicePath(defaultDevice.ID);
            
            // 判断是否为已注册的 HID 设备
            bool isHid = false;
            if (vid.HasValue && pid.HasValue)
            {
                isHid = GetConfig(vid.Value, pid.Value) != null;
            }
            else
            {
                // 通过名称匹配判断
                isHid = IsHidMicrophone(deviceName);
            }
            
            return (deviceName ?? "Unknown", vid, pid, isHid);
        }
        catch
        {
            return null;
        }
    }
}
