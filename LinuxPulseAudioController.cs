using System.Diagnostics;

namespace UsbAudioControl;

/// <summary>
/// Linux PulseAudio 控制器
/// 通过 pactl 命令行工具控制麦克风静音
/// </summary>
public class LinuxPulseAudioController : IAudioMuteController
{
    private AudioDeviceInfo? _connectedDevice;
    private string? _deviceName;
    private bool _disposed;

    public AudioDeviceInfo? ConnectedDevice => _connectedDevice;
    public bool IsConnected => !string.IsNullOrEmpty(_deviceName);
    public bool SupportsMute => true;
    public bool SupportsVolume => true;

    /// <summary>
    /// 枚举所有音频输入设备
    /// </summary>
    public IReadOnlyList<AudioDeviceInfo> EnumerateDevices()
    {
        var result = new List<AudioDeviceInfo>();
        
        if (!OperatingSystem.IsLinux())
            return result;
        
        try
        {
            // 使用 pactl 列出所有输入源
            var output = RunCommand("pactl", "list short sources");
            if (output == null)
                return result;
            
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                    continue;
                
                var deviceId = parts[0];
                var name = parts[1];
                
                // 获取设备详细信息
                var deviceInfo = GetDeviceInfo(deviceId, name);
                if (deviceInfo != null)
                    result.Add(deviceInfo);
            }
        }
        catch
        {
            // 忽略错误
        }
        
        return result;
    }

    private AudioDeviceInfo? GetDeviceInfo(string index, string name)
    {
        try
        {
            // 获取设备状态
            var output = RunCommand("pactl", $"get-source-mute {index}");
            var isMuted = output?.Contains("yes", StringComparison.OrdinalIgnoreCase) == true;
            
            var volumeOutput = RunCommand("pactl", $"get-source-volume {index}");
            float volume = 0;
            if (volumeOutput != null)
            {
                // 解析 "Volume: front-left: 65536 / 100% / 0.00 dB"
                var match = System.Text.RegularExpressions.Regex.Match(volumeOutput, @"(\d+)%");
                if (match.Success && int.TryParse(match.Groups[1].Value, out var volPercent))
                {
                    volume = volPercent / 100f;
                }
            }
            
            return new AudioDeviceInfo
            {
                Name = name,
                DeviceId = index,
                ControllerType = AudioControllerType.LinuxPulseAudio,
                SupportsMute = true,
                SupportsVolume = true
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 连接到指定设备
    /// </summary>
    public bool Connect(AudioDeviceInfo device)
    {
        if (device.ControllerType != AudioControllerType.LinuxPulseAudio)
            return false;
        
        return ConnectByDeviceName(device.DeviceId);
    }

    /// <summary>
    /// 通过设备名称连接
    /// </summary>
    public bool ConnectByDeviceName(string? deviceName)
    {
        if (!OperatingSystem.IsLinux())
            return false;
        
        _deviceName = deviceName ?? GetDefaultSource();
        if (string.IsNullOrEmpty(_deviceName))
            return false;
        
        // 验证设备存在
        var devices = EnumerateDevices();
        var device = devices.FirstOrDefault(d => d.DeviceId == _deviceName || d.Name == _deviceName);
        
        if (device != null)
        {
            _connectedDevice = device;
            return true;
        }
        
        // 如果找不到设备，尝试使用默认
        _deviceName = GetDefaultSource();
        if (!string.IsNullOrEmpty(_deviceName))
        {
            _connectedDevice = new AudioDeviceInfo
            {
                Name = _deviceName,
                DeviceId = _deviceName,
                ControllerType = AudioControllerType.LinuxPulseAudio,
                SupportsMute = true,
                SupportsVolume = true
            };
            return true;
        }
        
        return false;
    }

    /// <summary>
    /// 连接到第一个可用设备（默认设备）
    /// </summary>
    public bool ConnectToFirst()
    {
        return ConnectByDeviceName(null);
    }

    private static string? GetDefaultSource()
    {
        var output = RunCommand("pactl", "get-default-source");
        return output?.Trim();
    }

    public void Disconnect()
    {
        _deviceName = null;
        _connectedDevice = null;
    }

    /// <summary>
    /// 设置静音状态
    /// </summary>
    public bool SetMute(bool mute)
    {
        if (string.IsNullOrEmpty(_deviceName))
            return false;
        
        var muteStr = mute ? "1" : "0";
        var output = RunCommand("pactl", $"set-source-mute {_deviceName} {muteStr}");
        return output != null || true; // pactl 成功时通常没有输出
    }

    /// <summary>
    /// 获取静音状态
    /// </summary>
    public bool? GetMute()
    {
        if (string.IsNullOrEmpty(_deviceName))
            return null;
        
        var output = RunCommand("pactl", $"get-source-mute {_deviceName}");
        if (output == null)
            return null;
        
        return output.Contains("yes", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 切换静音状态
    /// </summary>
    public bool? ToggleMute()
    {
        if (string.IsNullOrEmpty(_deviceName))
            return null;
        
        var output = RunCommand("pactl", $"set-source-mute {_deviceName} toggle");
        return GetMute();
    }

    /// <summary>
    /// 设置音量 (0.0 - 1.0)
    /// </summary>
    public bool SetVolume(float volume)
    {
        if (string.IsNullOrEmpty(_deviceName))
            return false;
        
        volume = Math.Clamp(volume, 0f, 1f);
        var volumePercent = (int)(volume * 100);
        var output = RunCommand("pactl", $"set-source-volume {_deviceName} {volumePercent}%");
        return true;
    }

    /// <summary>
    /// 获取音量 (0.0 - 1.0)
    /// </summary>
    public float? GetVolume()
    {
        if (string.IsNullOrEmpty(_deviceName))
            return null;
        
        var output = RunCommand("pactl", $"get-source-volume {_deviceName}");
        if (output == null)
            return null;
        
        var match = System.Text.RegularExpressions.Regex.Match(output, @"(\d+)%");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var volPercent))
        {
            return volPercent / 100f;
        }
        
        return null;
    }

    private static string? RunCommand(string command, string args)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(startInfo);
            if (process == null)
                return null;
            
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            
            return output;
        }
        catch
        {
            return null;
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
