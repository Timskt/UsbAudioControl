namespace UsbAudioControl;

/// <summary>
/// 音频静音控制器接口
/// </summary>
public interface IAudioMuteController : IDisposable
{
    /// <summary>
    /// 当前连接的设备信息
    /// </summary>
    AudioDeviceInfo? ConnectedDevice { get; }
    
    /// <summary>
    /// 是否已连接
    /// </summary>
    bool IsConnected { get; }
    
    /// <summary>
    /// 是否支持静音控制
    /// </summary>
    bool SupportsMute { get; }
    
    /// <summary>
    /// 是否支持音量控制
    /// </summary>
    bool SupportsVolume { get; }
    
    /// <summary>
    /// 枚举所有可用的音频输入设备
    /// </summary>
    IReadOnlyList<AudioDeviceInfo> EnumerateDevices();
    
    /// <summary>
    /// 连接到指定设备
    /// </summary>
    bool Connect(AudioDeviceInfo device);
    
    /// <summary>
    /// 连接到第一个可用设备
    /// </summary>
    bool ConnectToFirst();
    
    /// <summary>
    /// 断开连接
    /// </summary>
    void Disconnect();
    
    /// <summary>
    /// 设置静音状态
    /// </summary>
    bool SetMute(bool mute);
    
    /// <summary>
    /// 获取静音状态
    /// </summary>
    bool? GetMute();
    
    /// <summary>
    /// 切换静音状态
    /// </summary>
    bool? ToggleMute();
    
    /// <summary>
    /// 静音
    /// </summary>
    bool Mute() => SetMute(true);
    
    /// <summary>
    /// 取消静音
    /// </summary>
    bool Unmute() => SetMute(false);
    
    /// <summary>
    /// 设置音量 (0.0 - 1.0)
    /// </summary>
    bool SetVolume(float volume);
    
    /// <summary>
    /// 获取音量 (0.0 - 1.0)
    /// </summary>
    float? GetVolume();
}
