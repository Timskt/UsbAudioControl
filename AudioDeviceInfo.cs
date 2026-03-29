namespace UsbAudioControl;

/// <summary>
/// 音频设备信息
/// </summary>
public record AudioDeviceInfo
{
    public string? Name { get; init; }
    public string? DeviceId { get; init; }
    public int VendorId { get; init; }
    public int ProductId { get; init; }
    public string? Manufacturer { get; init; }
    public string? Product { get; init; }
    public string? SerialNumber { get; init; }
    public bool SupportsMute { get; init; }
    public bool SupportsVolume { get; init; }
    public AudioControllerType ControllerType { get; init; }
    
    /// <summary>
    /// USB 设备实例路径 (如: USB\VID_1FC9&PID_826B&MI_00\6&360496AE&0&0000)
    /// </summary>
    public string? UsbDeviceInstanceId { get; init; }
    
    /// <summary>
    /// 获取 VID:PID 格式的标识符
    /// </summary>
    public string? VidPid => VendorId > 0 ? $"{VendorId:X4}:{ProductId:X4}" : null;
}

/// <summary>
/// 控制器类型
/// </summary>
public enum AudioControllerType
{
    /// <summary>
    /// USB Audio 协议直接控制
    /// </summary>
    UsbAudio,
    
    /// <summary>
    /// Windows Core Audio API
    /// </summary>
    WindowsCoreAudio,
    
    /// <summary>
    /// Linux PulseAudio
    /// </summary>
    LinuxPulseAudio,
    
    /// <summary>
    /// macOS CoreAudio
    /// </summary>
    MacOSCoreAudio,
    
    /// <summary>
    /// HID 设备控制（物理按钮、LED 指示灯）
    /// </summary>
    HidAudio
}
