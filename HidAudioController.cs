using HidLibrary;
using NAudio.CoreAudioApi;

namespace UsbAudioControl;

/// <summary>
/// HID 音频设备控制器
/// 使用 HidLibrary 控制 LED，使用 Core Audio 监听系统静音状态
/// </summary>
public class HidAudioController : IAudioMuteController
{
    private HidDevice? _hidDevice;
    private HidDeviceInfo? _connectedDeviceInfo;
    private MMDevice? _audioDevice;
    private bool _disposed;
    private bool _isMonitoring;
    private bool _lastMuteState;
    private float _lastVolume;
    private readonly object _lock = new();
    private System.Threading.Timer? _hidPollingTimer;
    private readonly HidAudioConfig _config;

    /// <summary>
    /// 当前配置
    /// </summary>
    public HidAudioConfig Config => _config;

    public AudioDeviceInfo? ConnectedDevice => _connectedDeviceInfo?.ToAudioDeviceInfo();
    public bool IsConnected => _hidDevice != null && _hidDevice.IsConnected;
    public bool SupportsMute => true;
    public bool SupportsVolume => false;
    public bool SupportsLed => true;

    public event EventHandler<AudioStateChangedEventArgs>? StateChanged;
    public event EventHandler<PhysicalButtonEventArgs>? PhysicalButtonPressed;

    /// <summary>
    /// 创建控制器 (使用默认配置)
    /// </summary>
    public HidAudioController() : this(HidAudioConfig.Default)
    {
    }

    /// <summary>
    /// 创建控制器 (使用自定义配置)
    /// </summary>
    public HidAudioController(HidAudioConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// 查找所有 HID 音频设备 (使用默认配置的 VID/PID)
    /// </summary>
    public static List<HidDeviceInfo> FindAllHidAudioDevices()
    {
        return FindAllHidAudioDevices(HidAudioConfig.Default.VendorId, HidAudioConfig.Default.ProductId);
    }

    /// <summary>
    /// 查找所有 HID 音频设备
    /// </summary>
    public static List<HidDeviceInfo> FindAllHidAudioDevices(int vendorId, int productId)
    {
        var devices = new List<HidDeviceInfo>();
        
        var hidDevices = HidDevices.Enumerate(vendorId, productId)
            .Where(d => d.Capabilities.OutputReportByteLength > 0);
        
        foreach (var device in hidDevices)
        {
            try
            {
                var info = new HidDeviceInfo
                {
                    VendorId = (int)device.Attributes.VendorId,
                    ProductId = (int)device.Attributes.ProductId,
                    ProductName = device.Description ?? "Hamedal Speak A20",
                    DevicePath = device.DevicePath,
                    UsagePage = (ushort)device.Capabilities.UsagePage,
                    Usage = (ushort)device.Capabilities.Usage,
                    InputReportLength = device.Capabilities.InputReportByteLength,
                    OutputReportLength = device.Capabilities.OutputReportByteLength,
                    FeatureReportLength = device.Capabilities.FeatureReportByteLength
                };
                
                devices.Add(info);
            }
            finally
            {
                device.CloseDevice();
            }
        }
        
        return devices;
    }

    public IReadOnlyList<AudioDeviceInfo> EnumerateDevices()
    {
        return FindAllHidAudioDevices(_config.VendorId, _config.ProductId)
            .Select(d => d.ToAudioDeviceInfo())
            .ToList();
    }

    public bool Connect(AudioDeviceInfo device)
    {
        return ConnectByVidPid(device.VendorId, device.ProductId);
    }

    public bool ConnectByVidPid(int vendorId, int productId)
    {
        Disconnect();
        
        // 连接 HID 设备
        var devices = HidDevices.Enumerate(vendorId, productId)
            .Where(d => d.Capabilities.OutputReportByteLength > 0)
            .ToList();
        
        int selectedIndex = devices.Count > 1 ? 1 : 0;
        _hidDevice = devices.Count > selectedIndex ? devices[selectedIndex] : devices.FirstOrDefault();
        
        if (_hidDevice == null)
            return false;
        
        _hidDevice.OpenDevice();
        
        if (!_hidDevice.IsConnected)
        {
            _hidDevice.Dispose();
            _hidDevice = null;
            return false;
        }
        
        _connectedDeviceInfo = new HidDeviceInfo
        {
            VendorId = (int)_hidDevice.Attributes.VendorId,
            ProductId = (int)_hidDevice.Attributes.ProductId,
            ProductName = _hidDevice.Description ?? "Hamedal Speak A20",
            DevicePath = _hidDevice.DevicePath,
            UsagePage = (ushort)_hidDevice.Capabilities.UsagePage,
            Usage = (ushort)_hidDevice.Capabilities.Usage,
            InputReportLength = _hidDevice.Capabilities.InputReportByteLength,
            OutputReportLength = _hidDevice.Capabilities.OutputReportByteLength
        };
        
        // 连接对应的 Core Audio 设备
        ConnectCoreAudioDevice();
        
        // 初始化状态 - 默认启用
        _lastMuteState = false;
        _lastVolume = GetVolumeFromSystem() ?? 1f;
        
        
        return true;
    }

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
    /// 自动检测当前系统默认输入麦克风，匹配配置并连接
    /// </summary>
    public static HidAudioController? ConnectAuto()
    {
        // 方式1: 根据当前系统默认麦克风匹配配置
        var defaultConfig = HidAudioConfigRegistry.GetDefaultMicrophoneConfig();
        if (defaultConfig != null)
        {
            Console.WriteLine($"使用配置: {defaultConfig.Name}");
            var controller = new HidAudioController(defaultConfig);
            if (controller.ConnectToFirst())
                return controller;
            Console.WriteLine("方式1连接失败，尝试方式2...");
            controller.Dispose();
        }
        
        // 方式2: 扫描所有已注册设备
        var devices = HidAudioConfigRegistry.ScanDevices();
        
        if (devices.Count == 0)
        {
            Console.WriteLine("未找到任何已注册的 HID 设备");
            return null;
        }
        
        var (config, devicePath) = devices[0];
        Console.WriteLine($"使用配置: {config.Name}");
        var controller2 = new HidAudioController(config);
        
        if (controller2.ConnectByPath(devicePath))
            return controller2;
        
        Console.WriteLine("方式2连接失败");
        controller2.Dispose();
        return null;
    }

    public bool ConnectToFirst()
    {
        return ConnectByVidPid(_config.VendorId, _config.ProductId);
    }

    public bool ConnectByPath(string? devicePath)
    {
        if (string.IsNullOrEmpty(devicePath))
            return false;
        
        Disconnect();
        
        var allDevices = HidDevices.Enumerate()
            .Where(d => d.Capabilities.OutputReportByteLength > 0);
        _hidDevice = allDevices.FirstOrDefault(d => d.DevicePath == devicePath);
        
        if (_hidDevice == null)
            return false;
        
        _hidDevice.OpenDevice();
        
        if (!_hidDevice.IsConnected)
        {
            _hidDevice.Dispose();
            _hidDevice = null;
            return false;
        }
        
        _connectedDeviceInfo = new HidDeviceInfo
        {
            VendorId = (int)_hidDevice.Attributes.VendorId,
            ProductId = (int)_hidDevice.Attributes.ProductId,
            ProductName = _hidDevice.Description ?? "HID Audio Device",
            DevicePath = _hidDevice.DevicePath,
            UsagePage = (ushort)_hidDevice.Capabilities.UsagePage,
            Usage = (ushort)_hidDevice.Capabilities.Usage,
            InputReportLength = _hidDevice.Capabilities.InputReportByteLength,
            OutputReportLength = _hidDevice.Capabilities.OutputReportByteLength
        };
        
        ConnectCoreAudioDevice();
        _lastMuteState = false;  // 默认启用状态
        
        
        return true;
    }

    /// <summary>
    /// 连接对应的 Core Audio 设备用于状态监听
    /// </summary>
    private void ConnectCoreAudioDevice()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var captureDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            
            var keywords = _config.DeviceNameKeywords;
            
            // 查找匹配的设备
            if (keywords != null && keywords.Length > 0)
            {
                foreach (var device in captureDevices)
                {
                    if (keywords.Any(k => device.FriendlyName?.Contains(k) == true))
                    {
                        _audioDevice = device;
                        break;
                    }
                }
            }
            
            // 如果没找到，使用默认设备
            _audioDevice ??= enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
        }
        catch
        {
            // Core Audio 连接失败不影响 HID 控制
        }
    }

    public void Disconnect()
    {
        lock (_lock)
        {
            StopMonitoring();
            _audioDevice?.Dispose();
            _audioDevice = null;
            
            if (_hidDevice != null)
            {
                _hidDevice.CloseDevice();
                _hidDevice.Dispose();
                _hidDevice = null;
            }
            _connectedDeviceInfo = null;
        }
    }

    /// <summary>
    /// 设置静音状态
    /// </summary>
    public bool SetMute(bool mute)
    {
        if (_hidDevice == null || !_hidDevice.IsConnected)
            return false;
        
        if (SendMuteCommand(mute))
        {
            _lastMuteState = mute;
            
            // 同步设置系统音频静音
            SetSystemMute(mute);
            
            return true;
        }
        
        return false;
    }

    /// <summary>
    /// 设置静音 LED（仅控制 LED，不影响系统音频）
    /// </summary>
    public bool SetMuteLed(bool muted)
    {
        return SendMuteCommand(muted);
    }

    /// <summary>
    /// 获取静音状态（从系统读取）
    /// </summary>
    public bool? GetMute()
    {
        return GetMuteFromSystem() ?? _lastMuteState;
    }

    /// <summary>
    /// 切换静音状态
    /// </summary>
    public bool? ToggleMute()
    {
        var current = GetMute();
        if (current == null)
            return null;
        
        var newState = !current.Value;
        return SetMute(newState) ? newState : null;
    }

    public bool SetVolume(float volume)
    {
        return false;
    }

    public float? GetVolume()
    {
        return GetVolumeFromSystem();
    }

    /// <summary>
    /// 从系统获取静音状态
    /// </summary>
    private bool? GetMuteFromSystem()
    {
        if (_audioDevice == null)
            return null;
        
        try
        {
            return _audioDevice.AudioEndpointVolume.Mute;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 从系统获取音量
    /// </summary>
    private float? GetVolumeFromSystem()
    {
        if (_audioDevice == null)
            return null;
        
        try
        {
            return _audioDevice.AudioEndpointVolume.MasterVolumeLevelScalar;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 设置系统静音状态
    /// </summary>
    private void SetSystemMute(bool mute)
    {
        if (_audioDevice == null)
            return;
        
        try
        {
            _audioDevice.AudioEndpointVolume.Mute = mute;
        }
        catch
        {
            // 忽略错误
        }
    }

    /// <summary>
    /// 发送静音命令到 HID 设备
    /// </summary>
    private bool SendMuteCommand(bool mute)
    {
        if (_hidDevice == null || !_hidDevice.IsConnected)
            return false;
        
        try
        {
            var reportData = new byte[_hidDevice.Capabilities.OutputReportByteLength];
            reportData[0] = _config.ReportId;
            reportData[1] = mute ? _config.MuteOnData : _config.MuteOffData;
            
            _hidDevice.Write(reportData);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 开始监听状态变化
    /// </summary>
    public void StartMonitoring()
    {
        lock (_lock)
        {
            if (_isMonitoring || _hidDevice == null)
                return;
            
            _isMonitoring = true;
            _lastMuteState = GetMuteFromSystem() ?? false;
            _lastVolume = GetVolumeFromSystem() ?? 0f;
            
            // 使用轮询线程监听 HID 输入报告（监听物理按键）
            _hidPollingTimer = new System.Threading.Timer(_ =>
            {
                PollHidDevice();
            }, null, TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50));
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
            
            _hidPollingTimer?.Dispose();
            _hidPollingTimer = null;
        }
    }

    /// <summary>
    /// 轮询 HID 设备读取输入报告
    /// </summary>
    private void PollHidDevice()
    {
        if (!_isMonitoring || _hidDevice == null || !_hidDevice.IsConnected)
            return;
        
        try
        {
            // 非阻塞读取
            var report = _hidDevice.ReadReport(0); // 0 = 非阻塞
            
            if (report != null && report.Data != null && report.Data.Length > 0)
            {
                ParseHidReport(report.ReportId, report.Data);
            }
        }
        catch
        {
            // 忽略读取错误
        }
    }

    /// <summary>
    /// 解析 HID 报告，检测物理按键
    /// </summary>
    private void ParseHidReport(byte reportId, byte[] data)
    {
        
        // 使用配置中的按键报告 ID 和按键数据
        if (reportId == _config.ButtonReportId && data.Length >= 1 && data[0] == _config.ButtonPressData)
        {
            // 硬件静音状态切换 - 维护本地状态
            _lastMuteState = !_lastMuteState;
            _lastVolume = GetVolumeFromSystem() ?? _lastVolume;
            
            // 触发事件，告知用户当前状态
            StateChanged?.Invoke(this, new AudioStateChangedEventArgs
            {
                IsMuted = _lastMuteState,
                Volume = _lastVolume,
                Device = ConnectedDevice
            });
            
            // 触发物理按钮事件
            PhysicalButtonPressed?.Invoke(this, new PhysicalButtonEventArgs
            {
                ButtonType = PhysicalButtonType.Mute,
                IsPressed = true,
                NewMuteState = _lastMuteState
            });
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        
        Disconnect();
        _disposed = true;
    }
}

/// <summary>
/// HID 设备信息
/// </summary>
public class HidDeviceInfo
{
    public int VendorId { get; init; }
    public int ProductId { get; init; }
    public string? ProductName { get; init; }
    public string? DevicePath { get; init; }
    public ushort UsagePage { get; init; }
    public ushort Usage { get; init; }
    public int InputReportLength { get; init; }
    public int OutputReportLength { get; init; }
    public int FeatureReportLength { get; init; }
    
    public AudioDeviceInfo ToAudioDeviceInfo()
    {
        return new AudioDeviceInfo
        {
            Name = ProductName ?? $"HID Audio Device ({VendorId:X4}:{ProductId:X4})",
            DeviceId = DevicePath,
            VendorId = VendorId,
            ProductId = ProductId,
            ControllerType = AudioControllerType.HidAudio,
            SupportsMute = true,
            SupportsVolume = false
        };
    }
}
