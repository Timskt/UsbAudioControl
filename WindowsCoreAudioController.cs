using System.Runtime.CompilerServices;
using NAudio.CoreAudioApi;

namespace UsbAudioControl;

/// <summary>
/// Windows Core Audio 控制器
/// 通过 Windows 音频 API 控制麦克风静音，无需特殊驱动
/// </summary>
public class WindowsCoreAudioController : IAudioMuteController
{
    private MMDevice? _device;
    private AudioDeviceInfo? _connectedDevice;
    private bool _disposed;
    private bool _isMonitoring;
    private AudioEndpointVolumeCallback? _volumeCallback;

    public AudioDeviceInfo? ConnectedDevice => _connectedDevice;
    public bool IsConnected => _device != null;
    public bool SupportsMute => true;
    public bool SupportsVolume => true;

    /// <summary>
    /// 音频状态变化事件
    /// </summary>
    public event EventHandler<AudioStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// 枚举所有音频输入设备
    /// </summary>
    public IReadOnlyList<AudioDeviceInfo> EnumerateDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
        
        var result = new List<AudioDeviceInfo>();
        
        foreach (var device in devices)
        {
            try
            {
                var info = CreateDeviceInfo(device);
                result.Add(info);
            }
            catch
            {
                // 忽略无法访问的设备
            }
        }
        
        return result;
    }

    /// <summary>
    /// 获取默认音频输入设备
    /// </summary>
    public AudioDeviceInfo? GetDefaultDevice()
    {
        using var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
        return device != null ? CreateDeviceInfo(device) : null;
    }

    private static AudioDeviceInfo CreateDeviceInfo(MMDevice device)
    {
        return new AudioDeviceInfo
        {
            Name = device.FriendlyName,
            DeviceId = device.ID,
            ControllerType = AudioControllerType.WindowsCoreAudio,
            SupportsMute = true,
            SupportsVolume = true
        };
    }

    /// <summary>
    /// 连接到指定设备
    /// </summary>
    public bool Connect(AudioDeviceInfo device)
    {
        Disconnect();
        
        if (device.ControllerType != AudioControllerType.WindowsCoreAudio)
            return false;
        
        return ConnectByDeviceId(device.DeviceId);
    }

    /// <summary>
    /// 通过设备 ID 连接
    /// </summary>
    public bool ConnectByDeviceId(string? deviceId)
    {
        Disconnect();
        
        using var enumerator = new MMDeviceEnumerator();
        
        if (string.IsNullOrEmpty(deviceId))
        {
            _device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
        }
        else
        {
            _device = enumerator.GetDevice(deviceId);
        }
        
        if (_device == null)
            return false;
        
        _connectedDevice = CreateDeviceInfo(_device);
        
        // 如果之前已经在监听，重新注册
        if (_isMonitoring)
        {
            RegisterVolumeCallback();
        }
        
        return true;
    }

    /// <summary>
    /// 连接到第一个可用设备
    /// </summary>
    public bool ConnectToFirst()
    {
        var devices = EnumerateDevices();
        if (devices.Count == 0)
            return false;
        
        return Connect(devices[0]);
    }

    /// <summary>
    /// 断开连接
    /// </summary>
    public void Disconnect()
    {
        StopMonitoring();
        _connectedDevice = null;
        _device?.Dispose();
        _device = null;
    }

    /// <summary>
    /// 设置静音状态
    /// </summary>
    public bool SetMute(bool mute)
    {
        if (_device == null)
            return false;
        
        try
        {
            _device.AudioEndpointVolume.Mute = mute;
            // 强制刷新设备状态
            _ = _device.AudioEndpointVolume.Mute;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 获取静音状态
    /// </summary>
    public bool? GetMute()
    {
        if (_device == null)
            return null;
        
        try
        {
            return _device.AudioEndpointVolume.Mute;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 切换静音状态
    /// </summary>
    public bool? ToggleMute()
    {
        if (_device == null)
            return null;
        
        try
        {
            var current = _device.AudioEndpointVolume.Mute;
            _device.AudioEndpointVolume.Mute = !current;
            return !current;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 设置音量 (0.0 - 1.0)
    /// </summary>
    public bool SetVolume(float volume)
    {
        if (_device == null)
            return false;
        
        try
        {
            volume = Math.Clamp(volume, 0f, 1f);
            _device.AudioEndpointVolume.MasterVolumeLevelScalar = volume;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 获取音量 (0.0 - 1.0)
    /// </summary>
    public float? GetVolume()
    {
        if (_device == null)
            return null;
        
        try
        {
            return _device.AudioEndpointVolume.MasterVolumeLevelScalar;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 开始监听状态变化
    /// </summary>
    public void StartMonitoring()
    {
        if (_device == null || _isMonitoring)
            return;
        
        _isMonitoring = true;
        RegisterVolumeCallback();
    }

    /// <summary>
    /// 停止监听状态变化
    /// </summary>
    public void StopMonitoring()
    {
        if (!_isMonitoring)
            return;
        
        _isMonitoring = false;
        
        if (_device != null && _volumeCallback != null)
        {
            try
            {
                _device.AudioEndpointVolume.OnVolumeNotification -= _volumeCallback.HandleNotification;
            }
            catch { }
        }
        _volumeCallback = null;
    }

    private void RegisterVolumeCallback()
    {
        if (_device == null)
            return;
        
        _volumeCallback = new AudioEndpointVolumeCallback(this);
        _device.AudioEndpointVolume.OnVolumeNotification += _volumeCallback.HandleNotification;
    }

    internal void RaiseStateChanged(bool muted, float volume)
    {
        StateChanged?.Invoke(this, new AudioStateChangedEventArgs
        {
            IsMuted = muted,
            Volume = volume,
            Device = _connectedDevice
        });
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        
        Disconnect();
        _disposed = true;
    }

    /// <summary>
    /// 内部回调类，用于处理音量通知
    /// </summary>
    private class AudioEndpointVolumeCallback
    {
        private readonly WindowsCoreAudioController _controller;

        public AudioEndpointVolumeCallback(WindowsCoreAudioController controller)
        {
            _controller = controller;
        }

        public void HandleNotification(AudioVolumeNotificationData data)
        {
            _controller.RaiseStateChanged(data.Muted, data.MasterVolume);
        }
    }
}