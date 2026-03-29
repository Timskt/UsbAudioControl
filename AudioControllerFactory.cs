namespace UsbAudioControl;

/// <summary>
/// 音频控制器工厂，自动选择最佳控制方案
/// </summary>
public static class AudioControllerFactory
{
    /// <summary>
    /// 创建最佳可用的音频控制器
    /// 
    /// 优先级：
    /// 1. Windows Core Audio (Windows 上开箱即用)
    /// 2. Linux PulseAudio (Linux 上开箱即用)
    /// 3. USB Audio 协议 (需要 libusb 驱动)
    /// </summary>
    public static IAudioMuteController CreateBest()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsCoreAudioController();
        }
        
        if (OperatingSystem.IsLinux())
        {
            return new LinuxPulseAudioController();
        }
        
        // 其他平台使用 USB Audio 协议
        return new UsbAudioController();
    }

    /// <summary>
    /// 创建 Windows Core Audio 控制器
    /// </summary>
    public static WindowsCoreAudioController CreateWindowsCoreAudio()
    {
        return new WindowsCoreAudioController();
    }

    /// <summary>
    /// 创建 USB Audio 协议控制器
    /// </summary>
    public static UsbAudioController CreateUsbAudio()
    {
        return new UsbAudioController();
    }

    /// <summary>
    /// 创建 Windows 原生 USB Audio 控制器（使用 Windows API 直接访问 USB 设备）
    /// </summary>
    public static WindowsUsbAudioController CreateWindowsUsbAudio()
    {
        return new WindowsUsbAudioController();
    }

    /// <summary>
    /// 创建 Linux PulseAudio 控制器
    /// </summary>
    public static LinuxPulseAudioController CreateLinuxPulseAudio()
    {
        return new LinuxPulseAudioController();
    }

    /// <summary>
    /// 创建 HID 音频控制器（用于控制物理按钮和 LED）
    /// </summary>
    public static HidAudioController CreateHidAudio(HidAudioConfig? config = null)
    {
        return new HidAudioController(config ?? HidAudioConfig.Default);
    }

    /// <summary>
    /// 自动检测并创建 HID 音频控制器
    /// </summary>
    public static HidAudioController? CreateHidAudioAuto()
    {
        return HidAudioController.ConnectAuto();
    }

    /// <summary>
    /// 获取所有可用的控制方案
    /// </summary>
    public static IReadOnlyList<(AudioControllerType Type, string Description, bool Available)> GetAvailableControllers()
    {
        var result = new List<(AudioControllerType, string, bool)>();
        
        // Windows Core Audio
        bool windowsCoreAudioAvailable = OperatingSystem.IsWindows();
        result.Add((AudioControllerType.WindowsCoreAudio, 
            "Windows Core Audio API - 无需额外驱动，开箱即用", 
            windowsCoreAudioAvailable));
        
        // Linux PulseAudio
        bool linuxPulseAudioAvailable = OperatingSystem.IsLinux();
        result.Add((AudioControllerType.LinuxPulseAudio,
            "Linux PulseAudio - 无需额外驱动，开箱即用",
            linuxPulseAudioAvailable));
        
        // USB Audio (需要检查是否有设备)
        result.Add((AudioControllerType.UsbAudio, 
            "USB Audio Class 协议 - 直接控制 USB 设备，需要 libusb 驱动", 
            true));
        
        // HID Audio (物理按钮和 LED 控制)
        result.Add((AudioControllerType.HidAudio,
            "HID 音频设备 - 控制物理按钮和 LED 指示灯",
            true));
        
        return result;
    }

    /// <summary>
    /// 枚举所有音频输入设备（使用最佳方案）
    /// </summary>
    public static IReadOnlyList<AudioDeviceInfo> EnumerateAllDevices()
    {
        var result = new List<AudioDeviceInfo>();
        
        // Windows 上使用 Core Audio 枚举
        if (OperatingSystem.IsWindows())
        {
            using var controller = new WindowsCoreAudioController();
            result.AddRange(controller.EnumerateDevices());
        }
        
        // Linux 上使用 PulseAudio 枚举
        if (OperatingSystem.IsLinux())
        {
            using var controller = new LinuxPulseAudioController();
            result.AddRange(controller.EnumerateDevices());
        }
        
        // 同时枚举 USB Audio 设备
        try
        {
            var usbDevices = UsbAudioController.FindAllUsbAudioDevices();
            foreach (var d in usbDevices)
            {
                result.Add(new AudioDeviceInfo
                {
                    Name = d.Product ?? "USB Audio Device",
                    DeviceId = $"{d.VendorId:X4}:{d.ProductId:X4}",
                    VendorId = d.VendorId,
                    ProductId = d.ProductId,
                    Manufacturer = d.Manufacturer,
                    Product = d.Product,
                    SerialNumber = d.SerialNumber,
                    SupportsMute = d.FeatureUnits.Any(f => f.SupportsMute),
                    SupportsVolume = d.FeatureUnits.Any(f => f.SupportsVolume),
                    ControllerType = AudioControllerType.UsbAudio
                });
            }
        }
        catch
        {
            // USB Audio 枚举失败时忽略
        }
        
        return result;
    }
}
