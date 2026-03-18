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
    private System.Threading.Timer? _pollingTimer;
    private bool _lastMuteState;
    private float _lastVolume;
    private readonly object _lock = new();

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
        lock (_lock)
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
            
            // 初始化状态
            _lastMuteState = _device.AudioEndpointVolume.Mute;
            _lastVolume = _device.AudioEndpointVolume.MasterVolumeLevelScalar;
            
            // 如果之前已经在监听，重新启动
            if (_isMonitoring)
            {
                StartPolling();
            }
            
            return true;
        }
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
        lock (_lock)
        {
            StopMonitoring();
            _connectedDevice = null;
            _device?.Dispose();
            _device = null;
        }
    }

    /// <summary>
    /// 设置静音状态
    /// </summary>
    public bool SetMute(bool mute)
    {
        lock (_lock)
        {
            if (_device == null)
                return false;
            
            try
            {
                _device.AudioEndpointVolume.Mute = mute;
                
                // 立即更新缓存状态并触发事件
                _lastMuteState = mute;
                RaiseStateChanged(mute, _lastVolume);
                
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// 获取静音状态
    /// </summary>
    public bool? GetMute()
    {
        lock (_lock)
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
    }

    /// <summary>
    /// 切换静音状态
    /// </summary>
    public bool? ToggleMute()
    {
        lock (_lock)
        {
            if (_device == null)
                return null;
            
            try
            {
                var current = _device.AudioEndpointVolume.Mute;
                var newState = !current;
                _device.AudioEndpointVolume.Mute = newState;
                
                // 更新缓存并触发事件
                _lastMuteState = newState;
                RaiseStateChanged(newState, _lastVolume);
                
                return newState;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// 设置音量 (0.0 - 1.0)
    /// </summary>
    public bool SetVolume(float volume)
    {
        lock (_lock)
        {
            if (_device == null)
                return false;
            
            try
            {
                volume = Math.Clamp(volume, 0f, 1f);
                _device.AudioEndpointVolume.MasterVolumeLevelScalar = volume;
                
                // 更新缓存并触发事件
                _lastVolume = volume;
                RaiseStateChanged(_lastMuteState, volume);
                
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// 获取音量 (0.0 - 1.0)
    /// </summary>
    public float? GetVolume()
    {
        lock (_lock)
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
    }

    /// <summary>
    /// 开始监听状态变化
    /// </summary>
    public void StartMonitoring()
    {
        lock (_lock)
        {
            if (_device == null || _isMonitoring)
                return;
            
            _isMonitoring = true;
            
            // 初始化状态
            _lastMuteState = _device.AudioEndpointVolume.Mute;
            _lastVolume = _device.AudioEndpointVolume.MasterVolumeLevelScalar;
            
            StartPolling();
        }
    }

    /// <summary>
    /// 停止监听状态变化
    /// </summary>
    public void StopMonitoring()
    {
        lock (_lock)
        {
            if (!_isMonitoring)
                return;
            
            _isMonitoring = false;
            StopPolling();
        }
    }

    private void StartPolling()
    {
        // 使用轮询方式，每 100ms 检查一次状态
        _pollingTimer = new System.Threading.Timer(_ =>
        {
            PollDeviceState();
        }, null, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));
    }

    private void StopPolling()
    {
        _pollingTimer?.Dispose();
        _pollingTimer = null;
    }

    private void PollDeviceState()
    {
        lock (_lock)
        {
            if (!_isMonitoring || _device == null)
                return;
            
            try
            {
                var currentMute = _device.AudioEndpointVolume.Mute;
                var currentVolume = _device.AudioEndpointVolume.MasterVolumeLevelScalar;
                
                // 检测状态变化
                if (currentMute != _lastMuteState || Math.Abs(currentVolume - _lastVolume) > 0.001f)
                {
                    _lastMuteState = currentMute;
                    _lastVolume = currentVolume;
                    
                    // 在线程池上触发事件，避免阻塞轮询
                    Task.Run(() => RaiseStateChanged(currentMute, currentVolume));
                }
            }
            catch
            {
                // 设备可能已断开，忽略错误
            }
        }
    }

    private void RaiseStateChanged(bool muted, float volume)
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
}
