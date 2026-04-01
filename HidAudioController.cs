using HidLibrary;
using NAudio.CoreAudioApi;
using System.Collections.Concurrent;

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
    private CancellationTokenSource? _pollingCts;
    private Task? _pollingTask;
    private readonly HidAudioConfig _config;
    private readonly ConcurrentQueue<byte[]> _writeQueue = new();
    private ManualResetEventSlim? _writeSignal = new(false);
    private Task? _writeTask;
    private CancellationTokenSource? _writeCts;

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
        
        // 初始化状态
        _lastMuteState = false;
        _lastVolume = GetVolumeFromSystem() ?? 1f;
        
        // 启动写入线程
        StartWriteThread();
        
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
        _lastMuteState = false;
        _lastVolume = GetVolumeFromSystem() ?? 0.5f;
        
        // 启动写入线程
        StartWriteThread();
        
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

    /// <summary>
    /// 启动异步写入线程
    /// </summary>
    private void StartWriteThread()
    {
        _writeCts = new CancellationTokenSource();
        _writeTask = Task.Run(async () => WriteThread(_writeCts.Token));
    }

    /// <summary>
    /// 写入线程
    /// </summary>
    private async Task WriteThread(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _hidDevice?.IsConnected == true)
        {
            try
            {
                // 等待写入信号或取消
                await Task.Delay(10, cancellationToken);
                
                if (_writeQueue.TryDequeue(out var data))
                {
                    _hidDevice.Write(data);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // 写入失败，继续尝试
            }
        }
    }

    public void Disconnect()
    {
        lock (_lock)
        {
            try
            {
                StopMonitoring();
                
                // 停止写入线程
                _writeCts?.Cancel();
                if (_writeTask != null)
                {
                    try
                    {
                        if (!_writeTask.Wait(500))
                        {
                            _writeTask.Dispose();
                        }
                    }
                    catch
                    {
                        // 忽略异常
                    }
                }
                _writeCts?.Dispose();
                _writeCts = null;
                _writeTask = null;
                
                _audioDevice?.Dispose();
                _audioDevice = null;
                
                if (_hidDevice != null)
                {
                    var device = _hidDevice;
                    _hidDevice = null; // 立即清空引用
                    
                    // 在后台线程中安全关闭设备
                    Task.Run(() =>
                    {
                        try
                        {
                            if (device.IsConnected)
                            {
                                // 设置超时关闭
                                var closeTask = Task.Run(() =>
                                {
                                    try
                                    {
                                        device.CloseDevice();
                                    }
                                    catch
                                    {
                                        // 忽略关闭错误
                                    }
                                });
                                
                                if (!closeTask.Wait(200)) // 最多等待200ms
                                {
                                    // 超时，强制继续
                                }
                            }
                        }
                        catch
                        {
                            // 忽略错误
                        }
                        finally
                        {
                            try
                            {
                                device.Dispose();
                            }
                            catch
                            {
                                // 忽略释放错误
                            }
                        }
                    }).Wait(1000); // 最多等待1秒完成整个清理
                }
                
                _connectedDeviceInfo = null;
                
                // 清空写入队列
                while (_writeQueue.TryDequeue(out _)) { }
            }
            catch
            {
                // 确保无论如何都要清空引用
                _hidDevice = null;
                _audioDevice = null;
                _connectedDeviceInfo = null;
            }
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
            
            // 通过队列发送，避免阻塞
            _writeQueue.Enqueue(reportData);
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
            _lastVolume = GetVolumeFromSystem() ?? 0.5f;
            
            // 使用 CancellationToken 控制轮询
            _pollingCts = new CancellationTokenSource();
            _pollingTask = Task.Run(async () => await PollHidDeviceAsync(_pollingCts.Token));
        }
    }

    /// <summary>
    /// 异步轮询 HID 设备
    /// </summary>
    private async Task PollHidDeviceAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && 
               _isMonitoring && 
               _hidDevice != null && 
               _hidDevice.IsConnected)
        {
            try
            {
                await Task.Delay(50, cancellationToken);
                
                // 非阻塞读取
                var report = _hidDevice.ReadReport(0);
                
                if (report != null && report.Data != null && report.Data.Length > 0)
                {
                    ParseHidReport(report.ReportId, report.Data);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // 忽略读取错误
            }
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
            
            // 取消轮询任务
            _pollingCts?.Cancel();
            
            try
            {
                if (_pollingTask != null && !_pollingTask.IsCompleted)
                {
                    if (!_pollingTask.Wait(200)) // 等待最多200ms
                    {
                        // 超时，继续执行
                    }
                }
            }
            catch
            {
                // 忽略异常
            }
            
            _pollingCts?.Dispose();
            _pollingCts = null;
            _pollingTask = null;
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
            // 硬件静音状态切换
            _lastMuteState = !_lastMuteState;
            _lastVolume = GetVolumeFromSystem() ?? _lastVolume;
            
            // 触发事件
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
            
            // 同步更新 LED
            SetMuteLed(_lastMuteState);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        
        try
        {
            Disconnect();
            
            // 确保所有资源都被清理
            _writeSignal?.Dispose();
            _writeSignal = null;
            
            _pollingCts?.Dispose();
            _writeCts?.Dispose();
            
            _pollingTask?.Dispose();
            _writeTask?.Dispose();
        }
        finally
        {
            _disposed = true;
        }
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