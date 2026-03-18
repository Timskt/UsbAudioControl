namespace UsbAudioControl;

/// <summary>
/// 音频设备信息
/// </summary>
public class AudioDeviceInfo
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
    MacOSCoreAudio
}
