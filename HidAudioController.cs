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
}

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
    private DateTime _lastButtonPressTime = DateTime.MinValue;
    private const int ButtonDebounceMs = 300; // 按键防抖时间 (毫秒)
    private DateTime _connectTime = DateTime.MinValue;
    private const int IgnoreReportsAfterConnectMs = 500; // 连接后忽略报告的时间 (毫秒)

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
        
        // 记录连接时间，用于忽略连接后的残留 HID 报告
        _connectTime = DateTime.Now;
        
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
        
        // 记录连接时间，用于忽略连接后的残留 HID 报告
        _connectTime = DateTime.Now;
        
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
        // 忽略连接后短时间内的报告（可能是缓冲区残留数据）
        var timeSinceConnect = (DateTime.Now - _connectTime).TotalMilliseconds;
        if (timeSinceConnect < IgnoreReportsAfterConnectMs)
        {
            return;
        }
        
        // 使用配置中的按键报告 ID 和按键数据
        if (reportId == _config.ButtonReportId && data.Length >= 1 && data[0] == _config.ButtonPressData)
        {
            // 防抖：检查距离上次按键的时间
            var now = DateTime.Now;
            var elapsed = (now - _lastButtonPressTime).TotalMilliseconds;
            
            if (elapsed < ButtonDebounceMs)
            {
                // 忽略太快的重复按键
                return;
            }
            
            _lastButtonPressTime = now;
            
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
