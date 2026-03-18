using System.Collections.ObjectModel;
using LibUsbDotNet;
using LibUsbDotNet.Info;
using LibUsbDotNet.Main;
using LibUsbDotNet.Descriptors;

namespace UsbAudioControl;

/// <summary>
/// USB Audio 设备控制器，通过 USB Audio Class 协议直接控制设备
/// 
/// 协议格式（根据实际抓包）：
/// - wValue = (ControlSelector << 8) | Channel
/// - wIndex 取决于 UAC 版本：
///   - UAC 1.0: wIndex = InterfaceNumber
///   - UAC 2.0: wIndex = (UnitID << 8) | InterfaceNumber
/// </summary>
public class UsbAudioController : IAudioMuteController
{
    private UsbDevice? _device;
    private UsbAudioDeviceInfo? _connectedDeviceInfo;
    private bool _disposed;

    /// <summary>
    /// 当前连接的设备信息
    /// </summary>
    public AudioDeviceInfo? ConnectedDevice => _connectedDeviceInfo != null
        ? new AudioDeviceInfo
        {
            Name = _connectedDeviceInfo.Product ?? "USB Audio Device",
            DeviceId = $"{_connectedDeviceInfo.VendorId:X4}:{_connectedDeviceInfo.ProductId:X4}",
            VendorId = _connectedDeviceInfo.VendorId,
            ProductId = _connectedDeviceInfo.ProductId,
            Manufacturer = _connectedDeviceInfo.Manufacturer,
            Product = _connectedDeviceInfo.Product,
            SerialNumber = _connectedDeviceInfo.SerialNumber,
            SupportsMute = _connectedDeviceInfo.FeatureUnits.Any(f => f.SupportsMute),
            SupportsVolume = _connectedDeviceInfo.FeatureUnits.Any(f => f.SupportsVolume),
            ControllerType = AudioControllerType.UsbAudio
        }
        : null;

    public bool IsConnected => _device != null;
    public bool SupportsMute => _connectedDeviceInfo?.FeatureUnits.Any(f => f.SupportsMute) ?? false;
    public bool SupportsVolume => _connectedDeviceInfo?.FeatureUnits.Any(f => f.SupportsVolume) ?? false;

    /// <summary>
    /// 音频状态变化事件
    /// </summary>
    public event EventHandler<AudioStateChangedEventArgs>? StateChanged;
    
    private System.Threading.Timer? _monitoringTimer;
    private bool _isMonitoring;
    private bool _lastMuteState;
    private float _lastVolume;

    /// <summary>
    /// 查找所有 USB Audio 设备
    /// </summary>
    public static List<UsbAudioDeviceInfo> FindAllUsbAudioDevices()
    {
        var devices = new List<UsbAudioDeviceInfo>();
        
        foreach (UsbRegistry reg in UsbDevice.AllDevices)
        {
            try
            {
                if (!reg.Open(out UsbDevice? usbDev) || usbDev == null)
                    continue;
                
                try
                {
                    var info = ParseAudioDevice(usbDev);
                    if (info != null)
                    {
                        devices.Add(info);
                    }
                }
                finally
                {
                    usbDev.Close();
                }
            }
            catch
            {
                // 忽略无法访问的设备
            }
        }
        
        return devices;
    }

    /// <summary>
    /// 解析 USB Audio 设备信息
    /// </summary>
    private static UsbAudioDeviceInfo? ParseAudioDevice(UsbDevice device)
    {
        var configs = device.Configs;
        if (configs == null || configs.Count == 0)
            return null;
        
        int? audioControlInterface = null;
        var featureUnits = new List<FeatureUnitInfo>();
        var inputTerminals = new List<TerminalInfo>();
        
        foreach (UsbConfigInfo config in configs)
        {
            foreach (UsbInterfaceInfo iface in config.InterfaceInfoList)
            {
                var desc = iface.Descriptor;
                
                // 检查是否为 Audio Control Interface
                if (desc.Class == ClassCodeType.Audio &&
                    desc.SubClass == UsbAudioConstants.USB_SUBCLASS_AUDIOCONTROL)
                {
                    audioControlInterface = desc.InterfaceID;
                    ParseCustomDescriptors(config.CustomDescriptors, desc.InterfaceID, featureUnits, inputTerminals);
                }
            }
        }
        
        if (audioControlInterface == null)
            return null;
        
        var deviceInfo = device.Info;
        var deviceDesc = deviceInfo?.Descriptor;
        
        return new UsbAudioDeviceInfo
        {
            VendorId = deviceDesc?.VendorID ?? 0,
            ProductId = deviceDesc?.ProductID ?? 0,
            Manufacturer = deviceInfo?.ManufacturerString,
            Product = deviceInfo?.ProductString,
            SerialNumber = deviceInfo?.SerialString,
            AudioControlInterfaceNumber = audioControlInterface.Value,
            FeatureUnits = featureUnits,
            InputTerminals = inputTerminals
        };
    }

    /// <summary>
    /// 解析类特定描述符
    /// </summary>
    private static void ParseCustomDescriptors(
        ReadOnlyCollection<byte[]> customDescriptors,
        int targetInterface,
        List<FeatureUnitInfo> featureUnits,
        List<TerminalInfo> inputTerminals)
    {
        foreach (var desc in customDescriptors)
        {
            if (desc.Length < 3)
                continue;
            
            if (desc[1] != UsbAudioConstants.CS_INTERFACE)
                continue;
            
            byte subType = desc[2];
            
            switch (subType)
            {
                case UsbAudioConstants.AC_INPUT_TERMINAL:
                    ParseInputTerminal(desc, targetInterface, inputTerminals);
                    break;
                
                case UsbAudioConstants.AC_FEATURE_UNIT:
                    ParseFeatureUnit(desc, targetInterface, featureUnits);
                    break;
            }
        }
    }

    private static void ParseInputTerminal(byte[] desc, int interfaceNumber, List<TerminalInfo> inputTerminals)
    {
        try
        {
            if (desc.Length < 12)
                return;
            
            inputTerminals.Add(new TerminalInfo
            {
                TerminalId = desc[3],
                TerminalType = (ushort)(desc[4] | (desc[5] << 8)),
                AssociatedTerminal = desc[6],
                NumberOfChannels = desc[7],
                InterfaceNumber = interfaceNumber
            });
        }
        catch { }
    }

    private static void ParseFeatureUnit(byte[] desc, int interfaceNumber, List<FeatureUnitInfo> featureUnits)
    {
        try
        {
            if (desc.Length < 7)
                return;
            
            byte unitId = desc[3];
            byte sourceId = desc[4];
            byte controlSize = desc[5];
            
            uint controls = 0;
            int controlOffset = 6;
            
            if (controlSize == 1 && controlOffset + controlSize <= desc.Length)
                controls = desc[controlOffset];
            else if (controlSize == 2 && controlOffset + controlSize <= desc.Length)
                controls = (uint)(desc[controlOffset] | (desc[controlOffset + 1] << 8));
            else if (controlSize == 4 && controlOffset + controlSize <= desc.Length)
                controls = (uint)(desc[controlOffset] | (desc[controlOffset + 1] << 8) | 
                                  (desc[controlOffset + 2] << 16) | (desc[controlOffset + 3] << 24));
            
            featureUnits.Add(new FeatureUnitInfo
            {
                UnitId = unitId,
                SourceId = sourceId,
                ControlSize = controlSize,
                Controls = controls,
                InterfaceNumber = interfaceNumber
            });
        }
        catch { }
    }

    /// <summary>
    /// 枚举所有 USB Audio 设备
    /// </summary>
    public IReadOnlyList<AudioDeviceInfo> EnumerateDevices()
    {
        return FindAllUsbAudioDevices()
            .Select(d => new AudioDeviceInfo
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
            })
            .ToList();
    }

    /// <summary>
    /// 连接到指定设备
    /// </summary>
    public bool Connect(AudioDeviceInfo device)
    {
        return Connect(device.VendorId, device.ProductId, device.SerialNumber);
    }

    /// <summary>
    /// 连接到指定 USB Audio 设备
    /// </summary>
    public bool Connect(int vendorId, int productId, string? serialNumber = null)
    {
        Disconnect();
        
        foreach (UsbRegistry reg in UsbDevice.AllDevices)
        {
            if (reg.Vid != vendorId || reg.Pid != productId)
                continue;
            
            if (!reg.Open(out _device) || _device == null)
                continue;
            
            if (!string.IsNullOrEmpty(serialNumber))
            {
                var info = _device.Info;
                if (info?.SerialString != serialNumber)
                {
                    _device.Close();
                    _device = null;
                    continue;
                }
            }
            
            _connectedDeviceInfo = ParseAudioDevice(_device);
            return _connectedDeviceInfo != null;
        }
        
        return false;
    }

    /// <summary>
    /// 连接到第一个可用的 USB Audio 设备
    /// </summary>
    public bool ConnectToFirst()
    {
        var devices = FindAllUsbAudioDevices();
        if (devices.Count == 0)
            return false;
        
        var first = devices[0];
        return Connect(first.VendorId, first.ProductId, first.SerialNumber);
    }

    public void Disconnect()
    {
        _device?.Close();
        _device = null;
        _connectedDeviceInfo = null;
    }

    /// <summary>
    /// 设置静音状态
    /// </summary>
    public bool SetMute(bool mute)
    {
        if (_device == null || _connectedDeviceInfo == null)
            return false;
        
        var muteUnit = _connectedDeviceInfo.FeatureUnits.FirstOrDefault(f => f.SupportsMute);
        if (muteUnit == null)
            return false;
        
        return SendFeatureControl(
            UsbAudioConstants.SET_CUR,
            UsbAudioConstants.FEATURE_MUTE,
            (byte)_connectedDeviceInfo.AudioControlInterfaceNumber,
            muteUnit.UnitId,
            channel: 0,
            new byte[] { (byte)(mute ? 1 : 0) }
        );
    }

    /// <summary>
    /// 获取静音状态
    /// </summary>
    public bool? GetMute()
    {
        if (_device == null || _connectedDeviceInfo == null)
            return null;
        
        var muteUnit = _connectedDeviceInfo.FeatureUnits.FirstOrDefault(f => f.SupportsMute);
        if (muteUnit == null)
            return null;
        
        var result = GetFeatureControl(
            UsbAudioConstants.GET_CUR,
            UsbAudioConstants.FEATURE_MUTE,
            (byte)_connectedDeviceInfo.AudioControlInterfaceNumber,
            muteUnit.UnitId,
            channel: 0
        );
        
        return result?.Mute;
    }

    /// <summary>
    /// 切换静音状态
    /// </summary>
    public bool? ToggleMute()
    {
        var current = GetMute();
        if (current == null)
            return null;
        
        var newValue = !current.Value;
        return SetMute(newValue) ? newValue : null;
    }

    /// <summary>
    /// 设置音量 (0.0 - 1.0)
    /// </summary>
    public bool SetVolume(float volume)
    {
        if (_device == null || _connectedDeviceInfo == null)
            return false;
        
        var volumeUnit = _connectedDeviceInfo.FeatureUnits.FirstOrDefault(f => f.SupportsVolume);
        if (volumeUnit == null)
            return false;
        
        // 将 0.0-1.0 转换为 dB 值（简化实现，实际需要查询设备的音量范围）
        short volumeRaw = (short)(volume * 32767 - 16384);
        
        return SendFeatureControl(
            UsbAudioConstants.SET_CUR,
            UsbAudioConstants.FEATURE_VOLUME,
            (byte)_connectedDeviceInfo.AudioControlInterfaceNumber,
            volumeUnit.UnitId,
            channel: 0,
            BitConverter.GetBytes(volumeRaw)
        );
    }

    /// <summary>
    /// 获取音量 (0.0 - 1.0)
    /// </summary>
    public float? GetVolume()
    {
        if (_device == null || _connectedDeviceInfo == null)
            return null;
        
        var volumeUnit = _connectedDeviceInfo.FeatureUnits.FirstOrDefault(f => f.SupportsVolume);
        if (volumeUnit == null)
            return null;
        
        var result = GetFeatureControl(
            UsbAudioConstants.GET_CUR,
            UsbAudioConstants.FEATURE_VOLUME,
            (byte)_connectedDeviceInfo.AudioControlInterfaceNumber,
            volumeUnit.UnitId,
            channel: 0
        );
        
        if (result == null)
            return null;
        
        // 将 dB 值转换为 0.0-1.0（简化实现）
        return (result.Value + 16384f) / 32767f;
    }

    /// <summary>
    /// 发送 Feature Unit 控制命令
    /// 
    /// 根据抓包数据分析：
    /// - wValue = (ControlSelector << 8) | Channel
    /// - wIndex: UAC 1.0 = InterfaceNumber, UAC 2.0 = (UnitID << 8) | InterfaceNumber
    /// </summary>
    private bool SendFeatureControl(
        byte request,
        byte controlSelector,
        byte interfaceNumber,
        byte unitId,
        byte channel,
        byte[] data)
    {
        if (_device == null)
            return false;
        
        // wValue = (ControlSelector << 8) | Channel (根据抓包数据)
        ushort wValue = (ushort)((controlSelector << 8) | channel);
        
        // 尝试 UAC 1.0 格式: wIndex = InterfaceNumber
        // 如果失败，可以尝试 UAC 2.0 格式: wIndex = (UnitID << 8) | InterfaceNumber
        ushort wIndexUac1 = interfaceNumber;
        ushort wIndexUac2 = (ushort)((unitId << 8) | interfaceNumber);
        
        // 首先尝试 UAC 1.0 格式
        byte bmRequestType = 0x21; // Class, Interface, Host-to-Device
        
        var setupPacket = new UsbSetupPacket(bmRequestType, request, wValue, wIndexUac1, (short)data.Length);
        int transferred;
        bool result = _device.ControlTransfer(ref setupPacket, data, data.Length, out transferred);
        
        if (result && transferred == data.Length)
            return true;
        
        // 如果 UAC 1.0 失败，尝试 UAC 2.0 格式
        setupPacket = new UsbSetupPacket(bmRequestType, request, wValue, wIndexUac2, (short)data.Length);
        result = _device.ControlTransfer(ref setupPacket, data, data.Length, out transferred);
        
        return result && transferred == data.Length;
    }

    /// <summary>
    /// 获取 Feature Unit 控制值
    /// </summary>
    private FeatureControlResult? GetFeatureControl(
        byte request,
        byte controlSelector,
        byte interfaceNumber,
        byte unitId,
        byte channel)
    {
        if (_device == null)
            return null;
        
        ushort wValue = (ushort)((controlSelector << 8) | channel);
        ushort wIndexUac1 = interfaceNumber;
        ushort wIndexUac2 = (ushort)((unitId << 8) | interfaceNumber);
        
        byte bmRequestType = 0xA1; // Class, Interface, Device-to-Host
        var data = new byte[4];
        
        // 尝试 UAC 1.0
        var setupPacket = new UsbSetupPacket(bmRequestType, request, wValue, wIndexUac1, (short)data.Length);
        int transferred;
        bool result = _device.ControlTransfer(ref setupPacket, data, data.Length, out transferred);
        
        if (!result || transferred == 0)
        {
            // 尝试 UAC 2.0
            setupPacket = new UsbSetupPacket(bmRequestType, request, wValue, wIndexUac2, (short)data.Length);
            result = _device.ControlTransfer(ref setupPacket, data, data.Length, out transferred);
        }
        
        if (!result || transferred == 0)
            return null;
        
        return new FeatureControlResult
        {
            RawData = data.Take(transferred).ToArray(),
            Mute = transferred > 0 && data[0] != 0,
            Value = transferred >= 2 ? (short)(data[0] | (data[1] << 8)) : (short)0
        };
    }

    /// <summary>
    /// 开始监听状态变化（通过轮询实现）
    /// </summary>
    public void StartMonitoring()
    {
        if (_isMonitoring || _device == null)
            return;
        
        _isMonitoring = true;
        _lastMuteState = GetMute() ?? false;
        _lastVolume = GetVolume() ?? 0f;
        
        // 每 200ms 轮询一次状态
        _monitoringTimer = new System.Threading.Timer(_ =>
        {
            if (!_isMonitoring || _device == null)
                return;
            
            try
            {
                var currentMute = GetMute() ?? _lastMuteState;
                var currentVolume = GetVolume() ?? _lastVolume;
                
                if (currentMute != _lastMuteState || Math.Abs(currentVolume - _lastVolume) > 0.001f)
                {
                    _lastMuteState = currentMute;
                    _lastVolume = currentVolume;
                    
                    StateChanged?.Invoke(this, new AudioStateChangedEventArgs
                    {
                        IsMuted = currentMute,
                        Volume = currentVolume,
                        Device = ConnectedDevice
                    });
                }
            }
            catch { }
        }, null, TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(200));
    }

    /// <summary>
    /// 停止监听状态变化
    /// </summary>
    public void StopMonitoring()
    {
        if (!_isMonitoring)
            return;
        
        _isMonitoring = false;
        _monitoringTimer?.Dispose();
        _monitoringTimer = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        
        StopMonitoring();
        Disconnect();
        _disposed = true;
        UsbDevice.Exit();
    }
}

/// <summary>
/// USB Audio 设备详细信息
/// </summary>
public class UsbAudioDeviceInfo
{
    public int VendorId { get; init; }
    public int ProductId { get; init; }
    public string? Manufacturer { get; init; }
    public string? Product { get; init; }
    public string? SerialNumber { get; init; }
    public int AudioControlInterfaceNumber { get; init; }
    public List<FeatureUnitInfo> FeatureUnits { get; init; } = new();
    public List<TerminalInfo> InputTerminals { get; init; } = new();
}

/// <summary>
/// Feature Unit 信息
/// </summary>
public class FeatureUnitInfo
{
    public byte UnitId { get; init; }
    public byte SourceId { get; init; }
    public byte ControlSize { get; init; }
    public uint Controls { get; init; }
    public bool SupportsMute => (Controls & 0x01) != 0;
    public bool SupportsVolume => (Controls & 0x02) != 0;
    public int InterfaceNumber { get; init; }
}

/// <summary>
/// Terminal 信息
/// </summary>
public class TerminalInfo
{
    public byte TerminalId { get; init; }
    public ushort TerminalType { get; init; }
    public byte AssociatedTerminal { get; init; }
    public byte NumberOfChannels { get; init; }
    public int InterfaceNumber { get; init; }
    
    public bool IsMicrophone => TerminalType is 
        UsbAudioConstants.TERMINAL_MICROPHONE or 
        UsbAudioConstants.TERMINAL_DESKTOP_MICROPHONE or 
        UsbAudioConstants.TERMINAL_PERSONAL_MICROPHONE or
        UsbAudioConstants.TERMINAL_OMNI_DIR_MICROPHONE or
        UsbAudioConstants.TERMINAL_MIC_ARRAY or
        UsbAudioConstants.TERMINAL_PROCESSING_MIC_ARRAY;
}

/// <summary>
/// Feature 控制结果
/// </summary>
public class FeatureControlResult
{
    public byte[] RawData { get; init; } = Array.Empty<byte>();
    public bool Mute { get; init; }
    public short Value { get; init; }
}