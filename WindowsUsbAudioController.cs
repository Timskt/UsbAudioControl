using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace UsbAudioControl;

/// <summary>
/// Windows USB Audio 控制器
/// 使用 Windows 原生 API (SetupAPI) 枚举 USB 设备
/// 注意：直接发送 USB 控制传输需要 WinUSB 驱动或管理员权限
/// </summary>
public class WindowsUsbAudioController : IAudioMuteController
{
    private SafeFileHandle? _deviceHandle;
    private AudioDeviceInfo? _connectedDevice;
    private bool _disposed;

    private const int INVALID_HANDLE_VALUE = -1;

    public AudioDeviceInfo? ConnectedDevice => _connectedDevice;
    public bool IsConnected => _deviceHandle != null && !_deviceHandle.IsInvalid;
    public bool SupportsMute => true;
    public bool SupportsVolume => true;

    /// <summary>
    /// 音频状态变化事件
    /// </summary>
    public event EventHandler<AudioStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// 开始监听状态变化
    /// </summary>
    public void StartMonitoring()
    {
        // Windows USB 原生控制暂不支持状态监听
    }

    /// <summary>
    /// 停止监听状态变化
    /// </summary>
    public void StopMonitoring()
    {
        // Windows USB 原生控制暂不支持状态监听
    }

    /// <summary>
    /// 枚举所有 USB Audio 设备
    /// </summary>
    public IReadOnlyList<AudioDeviceInfo> EnumerateDevices()
    {
        var devices = new List<AudioDeviceInfo>();
        
        try
        {
            var usbDevices = EnumerateUsbDevices();
            foreach (var dev in usbDevices)
            {
                devices.Add(new AudioDeviceInfo
                {
                    Name = dev.Name ?? "USB Audio Device",
                    DeviceId = dev.DevicePath,
                    VendorId = dev.VendorId,
                    ProductId = dev.ProductId,
                    ControllerType = AudioControllerType.UsbAudio,
                    SupportsMute = true,
                    SupportsVolume = true
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"枚举错误: {ex.Message}");
        }
        
        return devices;
    }

    /// <summary>
    /// 使用 SetupAPI 枚举 USB 设备
    /// </summary>
    private List<UsbDeviceInfo> EnumerateUsbDevices()
    {
        var devices = new List<UsbDeviceInfo>();
        
        // GUID_DEVINTERFACE_USB_DEVICE - Windows 预定义的 USB 设备接口 GUID
        // 定义在 Windows SDK 的 wdm.h 中
        // 用于枚举系统中所有 USB 设备
        var usbGuid = NativeMethods.GUID_DEVINTERFACE_USB_DEVICE;
        
        IntPtr hDevInfo = NativeMethods.SetupDiGetClassDevs(
            ref usbGuid,
            IntPtr.Zero,
            IntPtr.Zero,
            NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_DEVICEINTERFACE);
        
        if (hDevInfo == new IntPtr(INVALID_HANDLE_VALUE) || hDevInfo == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            Console.WriteLine($"SetupDiGetClassDevs 失败, 错误码: {error}");
            return devices;
        }
        
        try
        {
            int index = 0;
            while (true)
            {
                var deviceInterfaceData = new NativeMethods.SP_DEVICE_INTERFACE_DATA();
                deviceInterfaceData.cbSize = Marshal.SizeOf(typeof(NativeMethods.SP_DEVICE_INTERFACE_DATA));
                
                if (!NativeMethods.SetupDiEnumDeviceInterfaces(
                    hDevInfo,
                    IntPtr.Zero,
                    ref usbGuid,
                    index,
                    ref deviceInterfaceData))
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err != 259) // ERROR_NO_MORE_ITEMS
                    {
                        // 其他错误
                    }
                    break;
                }
                
                index++;
                
                // 获取所需缓冲区大小
                uint requiredSize = 0;
                if (!NativeMethods.SetupDiGetDeviceInterfaceDetail(
                    hDevInfo,
                    ref deviceInterfaceData,
                    IntPtr.Zero,
                    0,
                    ref requiredSize,
                    IntPtr.Zero))
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err != 122) // ERROR_INSUFFICIENT_BUFFER - 这是预期的
                        continue;
                }
                
                if (requiredSize == 0)
                    continue;
                
                // 分配缓冲区
                IntPtr detailDataBuffer = IntPtr.Zero;
                try
                {
                    detailDataBuffer = Marshal.AllocHGlobal((int)requiredSize);
                    
                    // 设置 cbSize (x64: 8, x86: 5)
                    int cbSize = IntPtr.Size == 8 ? 8 : 5;
                    Marshal.WriteInt32(detailDataBuffer, cbSize);
                    
                    if (NativeMethods.SetupDiGetDeviceInterfaceDetail(
                        hDevInfo,
                        ref deviceInterfaceData,
                        detailDataBuffer,
                        requiredSize,
                        ref requiredSize,
                        IntPtr.Zero))
                    {
                        // 读取设备路径 (偏移 cbSize 字节)
                        int pathOffset = cbSize;
                        string? devicePath = Marshal.PtrToStringAuto(detailDataBuffer + pathOffset);
                        
                        if (!string.IsNullOrEmpty(devicePath))
                        {
                            var info = ParseDevicePath(devicePath);
                            if (info != null)
                            {
                                devices.Add(info);
                            }
                        }
                    }
                }
                finally
                {
                    if (detailDataBuffer != IntPtr.Zero)
                        Marshal.FreeHGlobal(detailDataBuffer);
                }
            }
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(hDevInfo);
        }
        
        return devices;
    }

    /// <summary>
    /// 从设备路径解析 VID/PID
    /// </summary>
    private UsbDeviceInfo? ParseDevicePath(string devicePath)
    {
        try
        {
            // 格式: \\?\usb#vid_xxxx&pid_xxxx&...
            var vidMatch = System.Text.RegularExpressions.Regex.Match(
                devicePath, @"vid_([0-9a-fA-F]{4})", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var pidMatch = System.Text.RegularExpressions.Regex.Match(
                devicePath, @"pid_([0-9a-fA-F]{4})", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            if (!vidMatch.Success || !pidMatch.Success)
                return null;
            
            return new UsbDeviceInfo
            {
                DevicePath = devicePath,
                VendorId = Convert.ToInt32(vidMatch.Groups[1].Value, 16),
                ProductId = Convert.ToInt32(pidMatch.Groups[1].Value, 16),
                Name = $"USB Device (VID:{vidMatch.Groups[1].Value}, PID:{pidMatch.Groups[1].Value})"
            };
        }
        catch
        {
            return null;
        }
    }

    public bool Connect(AudioDeviceInfo device)
    {
        if (!string.IsNullOrEmpty(device.DeviceId))
        {
            return ConnectByPath(device.DeviceId);
        }
        return Connect(device.VendorId, device.ProductId);
    }

    public bool Connect(int vendorId, int productId, string? serialNumber = null)
    {
        var devices = EnumerateUsbDevices();
        var device = devices.FirstOrDefault(d => d.VendorId == vendorId && d.ProductId == productId);
        
        if (device == null || string.IsNullOrEmpty(device.DevicePath))
            return false;
        
        return ConnectByPath(device.DevicePath);
    }

    public bool ConnectToFirst()
    {
        var devices = EnumerateUsbDevices();
        if (devices.Count == 0 || string.IsNullOrEmpty(devices[0].DevicePath))
            return false;
        
        return ConnectByPath(devices[0].DevicePath);
    }

    public bool ConnectByPath(string devicePath)
    {
        if (string.IsNullOrEmpty(devicePath))
            return false;
        
        Disconnect();
        
        _deviceHandle = NativeMethods.CreateFile(
            devicePath,
            NativeMethods.GENERIC_WRITE | NativeMethods.GENERIC_READ,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero,
            NativeMethods.OPEN_EXISTING,
            NativeMethods.FILE_ATTRIBUTE_NORMAL,
            IntPtr.Zero);
        
        if (_deviceHandle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            Console.WriteLine($"打开设备失败, 错误码: {error}");
            _deviceHandle.Dispose();
            _deviceHandle = null;
            return false;
        }
        
        var info = ParseDevicePath(devicePath);
        _connectedDevice = new AudioDeviceInfo
        {
            Name = info?.Name ?? "USB Device",
            DeviceId = devicePath,
            VendorId = info?.VendorId ?? 0,
            ProductId = info?.ProductId ?? 0,
            ControllerType = AudioControllerType.UsbAudio,
            SupportsMute = true,
            SupportsVolume = true
        };
        
        return true;
    }

    public void Disconnect()
    {
        _deviceHandle?.Dispose();
        _deviceHandle = null;
        _connectedDevice = null;
    }

    public bool SetMute(bool mute)
    {
        // 注意：标准 Windows USB Audio 驱动不允许直接发送控制传输
        // 需要使用 WinUSB 驱动或通过系统音频 API
        Console.WriteLine("警告: 标准 Windows USB Audio 驱动可能阻止此操作");
        return false;
    }

    public bool? GetMute()
    {
        return null;
    }

    public bool? ToggleMute()
    {
        return null;
    }

    public bool SetVolume(float volume)
    {
        return false;
    }

    public float? GetVolume()
    {
        return null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        
        Disconnect();
        _disposed = true;
    }

    private class UsbDeviceInfo
    {
        public string? DevicePath { get; set; }
        public int VendorId { get; set; }
        public int ProductId { get; set; }
        public string? Name { get; set; }
    }
}

internal static class NativeMethods
{
    public const uint DIGCF_PRESENT = 0x00000002;
    public const uint DIGCF_DEVICEINTERFACE = 0x00000010;
    
    public const uint GENERIC_READ = 0x80000000;
    public const uint GENERIC_WRITE = 0x40000000;
    public const uint FILE_SHARE_READ = 0x00000001;
    public const uint FILE_SHARE_WRITE = 0x00000002;
    public const uint OPEN_EXISTING = 3;
    public const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    /// <summary>
    /// GUID_DEVINTERFACE_USB_DEVICE
    /// Windows 预定义的 USB 设备接口 GUID
    /// 定义在 Windows SDK (wdm.h): {A5DCBF10-6530-11D2-901F-00C04FB951ED}
    /// 用于枚举所有 USB 设备
    /// </summary>
    public static readonly Guid GUID_DEVINTERFACE_USB_DEVICE = new("A5DCBF10-6530-11D2-901F-00C04FB951ED");
    
    /// <summary>
    /// GUID_DEVINTERFACE_AUDIO_CAPTURE
    /// 音频输入设备接口 GUID
    /// </summary>
    public static readonly Guid GUID_DEVINTERFACE_AUDIO_CAPTURE = new("2CB8C062-3F6F-4D8C-9D5E-4B0D7FC4D7BE");

    [StructLayout(LayoutKind.Sequential)]
    public class SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr SetupDiGetClassDevs(
        ref Guid ClassGuid,
        IntPtr Enumerator,
        IntPtr hwndParent,
        uint Flags);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr DeviceInfoSet,
        IntPtr DeviceInfoData,
        ref Guid InterfaceClassGuid,
        int MemberIndex,
        ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr DeviceInfoSet,
        ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData,
        IntPtr DeviceInterfaceDetailData,
        uint DeviceInterfaceDetailDataSize,
        ref uint RequiredSize,
        IntPtr DeviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);
}