namespace UsbAudioControl;

/// <summary>
/// 复合音频控制器
/// 同时控制 Windows Core Audio 和 HID 设备（物理按钮、LED 指示灯）
/// </summary>
public class CompositeAudioController : IAudioMuteController
{
    private readonly WindowsCoreAudioController _coreAudioController;
    private HidAudioController? _hidController;
    private bool _disposed;
    private bool _isMonitoring;

    public AudioDeviceInfo? ConnectedDevice => _coreAudioController.ConnectedDevice;
    public bool IsConnected => _coreAudioController.IsConnected;
    public bool SupportsMute => true;
    public bool SupportsVolume => _coreAudioController.SupportsVolume;
    public bool SupportsLed => _hidController?.SupportsLed ?? false;
    public bool HasHidSupport => _hidController != null && _hidController.IsConnected;

    /// <summary>
    /// HID 设备信息（如果有）
    /// </summary>
    public HidDeviceInfo? HidDeviceInfo { get; private set; }

    /// <summary>
    /// 音频状态变化事件
    /// </summary>
    public event EventHandler<AudioStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// 物理按钮事件（当用户按下设备上的按钮时触发）
    /// </summary>
    public event EventHandler<PhysicalButtonEventArgs>? PhysicalButtonPressed;

    /// <summary>
    /// 创建复合控制器
    /// </summary>
    public CompositeAudioController()
    {
        _coreAudioController = new WindowsCoreAudioController();
        _coreAudioController.StateChanged += OnCoreAudioStateChanged;
    }

    /// <summary>
    /// 创建复合控制器并连接指定设备
    /// </summary>
    public CompositeAudioController(int vendorId, int productId) : this()
    {
        ConnectByVidPid(vendorId, productId);
    }

    private void OnCoreAudioStateChanged(object? sender, AudioStateChangedEventArgs e)
    {
        // 同步 LED 状态
        if (_hidController != null && _hidController.IsConnected)
        {
            _hidController.SetMuteLed(e.IsMuted);
        }
        
        StateChanged?.Invoke(this, e);
    }

    /// <summary>
    /// 枚举所有音频输入设备
    /// </summary>
    public IReadOnlyList<AudioDeviceInfo> EnumerateDevices()
    {
        return _coreAudioController.EnumerateDevices();
    }

    /// <summary>
    /// 连接到指定设备
    /// </summary>
    public bool Connect(AudioDeviceInfo device)
    {
        var result = _coreAudioController.Connect(device);
        
        if (result && device.VendorId > 0)
        {
            // 尝试连接对应的 HID 设备
            ConnectHidDevice(device.VendorId, device.ProductId);
        }
        
        return result;
    }

    /// <summary>
    /// 通过 VID/PID 连接设备
    /// </summary>
    public bool ConnectByVidPid(int vendorId, int productId)
    {
        // 连接 Core Audio 设备
        var device = _coreAudioController.FindDevice(vendorId, productId);
        if (device == null)
            return false;
        
        return Connect(device);
    }

    /// <summary>
    /// 通过 VID:PID 字符串连接设备 (如 "1FC9:826B")
    /// </summary>
    public bool ConnectByVidPid(string vidPid)
    {
        if (string.IsNullOrEmpty(vidPid))
            return false;
        
        var parts = vidPid.Split(':');
        if (parts.Length != 2)
            return false;
        
        try
        {
            int vid = Convert.ToInt32(parts[0], 16);
            int pid = Convert.ToInt32(parts[1], 16);
            return ConnectByVidPid(vid, pid);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 连接 HID 设备
    /// </summary>
    private void ConnectHidDevice(int vendorId, int productId)
    {
        try
        {
            // 清理旧的 HID 控制器
            if (_hidController != null)
            {
                _hidController.PhysicalButtonPressed -= OnPhysicalButtonPressed;
                _hidController.Dispose();
                _hidController = null;
            }
            
            var hidDevices = HidAudioController.FindAllHidAudioDevices();
            var hidDevice = hidDevices.FirstOrDefault(d => d.VendorId == vendorId && d.ProductId == productId);
            
            if (hidDevice != null && !string.IsNullOrEmpty(hidDevice.DevicePath))
            {
                var controller = new HidAudioController();
                if (controller.ConnectByPath(hidDevice.DevicePath))
                {
                    controller.PhysicalButtonPressed += OnPhysicalButtonPressed;
                    _hidController = controller;
                    HidDeviceInfo = hidDevice;
                    
                    Console.WriteLine($"已连接 HID 设备: {hidDevice.ProductName}");
                    
                    // 如果已经在监听，启动 HID 监听
                    if (_isMonitoring)
                    {
                        _hidController.StartMonitoring();
                    }
                }
                else
                {
                    controller.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"连接 HID 设备失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 物理按钮事件处理
    /// </summary>
    private void OnPhysicalButtonPressed(object? sender, PhysicalButtonEventArgs e)
    {
        // 当物理静音按钮被按下时，同步 Windows 音频静音状态
        if (e.ButtonType == PhysicalButtonType.Mute && e.NewMuteState.HasValue)
        {
            _coreAudioController.SetMute(e.NewMuteState.Value);
        }
        
        // 触发外部事件
        PhysicalButtonPressed?.Invoke(this, e);
    }

    /// <summary>
    /// 连接到第一个可用设备
    /// </summary>
    public bool ConnectToFirst()
    {
        var result = _coreAudioController.ConnectToFirst();
        
        if (result && _coreAudioController.ConnectedDevice?.VendorId > 0)
        {
            ConnectHidDevice(
                _coreAudioController.ConnectedDevice.VendorId,
                _coreAudioController.ConnectedDevice.ProductId);
        }
        
        return result;
    }

    public void Disconnect()
    {
        _coreAudioController.Disconnect();
        _hidController?.Disconnect();
    }

    /// <summary>
    /// 设置静音状态
    /// </summary>
    public bool SetMute(bool mute)
    {
        // 同时设置 Windows 音频和 LED
        bool audioResult = _coreAudioController.SetMute(mute);
        bool ledResult = _hidController?.SetMuteLed(mute) ?? true;
        
        return audioResult && ledResult;
    }

    /// <summary>
    /// 设置静音 LED 指示灯（仅控制 LED，不影响音频）
    /// </summary>
    public bool SetMuteLed(bool muted)
    {
        return _hidController?.SetMuteLed(muted) ?? false;
    }

    /// <summary>
    /// 获取静音状态
    /// </summary>
    public bool? GetMute()
    {
        return _coreAudioController.GetMute();
    }

    /// <summary>
    /// 切换静音状态
    /// </summary>
    public bool? ToggleMute()
    {
        var result = _coreAudioController.ToggleMute();
        
        // 同步 LED
        if (result.HasValue && _hidController != null)
        {
            _hidController.SetMuteLed(result.Value);
        }
        
        return result;
    }

    /// <summary>
    /// 设置音量
    /// </summary>
    public bool SetVolume(float volume)
    {
        return _coreAudioController.SetVolume(volume);
    }

    /// <summary>
    /// 获取音量
    /// </summary>
    public float? GetVolume()
    {
        return _coreAudioController.GetVolume();
    }

    /// <summary>
    /// 开始监听状态变化
    /// </summary>
    public void StartMonitoring()
    {
        if (_isMonitoring)
            return;
        
        _isMonitoring = true;
        _coreAudioController.StartMonitoring();
        _hidController?.StartMonitoring();
    }

    /// <summary>
    /// 停止监听状态变化
    /// </summary>
    public void StopMonitoring()
    {
        if (!_isMonitoring)
            return;
        
        _isMonitoring = false;
        _coreAudioController.StopMonitoring();
        _hidController?.StopMonitoring();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        
        StopMonitoring();
        _coreAudioController.Dispose();
        _hidController?.Dispose();
        _disposed = true;
    }
}
