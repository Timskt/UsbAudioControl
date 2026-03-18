namespace UsbAudioControl;

/// <summary>
/// USB Audio Class 常量定义 (UAC 1.0 / UAC 2.0)
/// </summary>
public static class UsbAudioConstants
{
    #region USB Class Codes
    
    /// <summary>
    /// Audio设备类代码
    /// </summary>
    public const byte USB_CLASS_AUDIO = 0x01;
    
    /// <summary>
    /// Audio Control 子类
    /// </summary>
    public const byte USB_SUBCLASS_AUDIOCONTROL = 0x01;
    
    /// <summary>
    /// Audio Streaming 子类
    /// </summary>
    public const byte USB_SUBCLASS_AUDIOSTREAMING = 0x02;
    
    #endregion

    #region USB Audio Class Specific Descriptor Types
    
    /// <summary>
    /// CS_INTERFACE 描述符类型
    /// </summary>
    public const byte CS_INTERFACE = 0x24;
    
    /// <summary>
    /// CS_ENDPOINT 描述符类型
    /// </summary>
    public const byte CS_ENDPOINT = 0x25;
    
    #endregion

    #region Audio Class-Specific Descriptor Subtypes (UAC 1.0)

    /// <summary>
    /// AC Header 描述符子类型
    /// </summary>
    public const byte AC_HEADER = 0x01;
    
    /// <summary>
    /// Input Terminal 描述符子类型
    /// </summary>
    public const byte AC_INPUT_TERMINAL = 0x02;
    
    /// <summary>
    /// Output Terminal 描述符子类型
    /// </summary>
    public const byte AC_OUTPUT_TERMINAL = 0x03;
    
    /// <summary>
    /// Mixer Unit 描述符子类型
    /// </summary>
    public const byte AC_MIXER_UNIT = 0x04;
    
    /// <summary>
    /// Selector Unit 描述符子类型
    /// </summary>
    public const byte AC_SELECTOR_UNIT = 0x05;
    
    /// <summary>
    /// Feature Unit 描述符子类型
    /// </summary>
    public const byte AC_FEATURE_UNIT = 0x06;
    
    /// <summary>
    /// Processing Unit 描述符子类型
    /// </summary>
    public const byte AC_PROCESSING_UNIT = 0x07;
    
    /// <summary>
    /// Extension Unit 描述符子类型
    /// </summary>
    public const byte AC_EXTENSION_UNIT = 0x08;
    
    #endregion

    #region Terminal Types

    /// <summary>
    /// 麦克风终端类型
    /// </summary>
    public const ushort TERMINAL_MICROPHONE = 0x0201;
    
    /// <summary>
    /// 桌面麦克风终端类型
    /// </summary>
    public const ushort TERMINAL_DESKTOP_MICROPHONE = 0x0202;
    
    /// <summary>
    /// 个人麦克风终端类型
    /// </summary>
    public const ushort TERMINAL_PERSONAL_MICROPHONE = 0x0203;
    
    /// <summary>
    /// Omni-directional microphone
    /// </summary>
    public const ushort TERMINAL_OMNI_DIR_MICROPHONE = 0x0204;
    
    /// <summary>
    /// Microphone array
    /// </summary>
    public const ushort TERMINAL_MIC_ARRAY = 0x0205;
    
    /// <summary>
    /// Processing microphone array
    /// </summary>
    public const ushort TERMINAL_PROCESSING_MIC_ARRAY = 0x0206;

    #endregion

    #region Feature Unit Control Selectors

    /// <summary>
    /// 静音控制
    /// </summary>
    public const byte FEATURE_MUTE = 0x01;
    
    /// <summary>
    /// 音量控制
    /// </summary>
    public const byte FEATURE_VOLUME = 0x02;
    
    /// <summary>
    /// 低音控制
    /// </summary>
    public const byte FEATURE_BASS = 0x03;
    
    /// <summary>
    /// 中音控制
    /// </summary>
    public const byte FEATURE_MID = 0x04;
    
    /// <summary>
    /// 高音控制
    /// </summary>
    public const byte FEATURE_TREBLE = 0x05;
    
    /// <summary>
    /// 图形均衡器控制
    /// </summary>
    public const byte FEATURE_GRAPHIC_EQUALIZER = 0x06;
    
    /// <summary>
    /// 自动增益控制
    /// </summary>
    public const byte FEATURE_AUTOMATIC_GAIN = 0x07;
    
    /// <summary>
    /// 延迟控制
    /// </summary>
    public const byte FEATURE_DELAY = 0x08;
    
    /// <summary>
    /// 低音增强控制
    /// </summary>
    public const byte FEATURE_BASS_BOOST = 0x09;
    
    /// <summary>
    /// 响度控制
    /// </summary>
    public const byte FEATURE_LOUDNESS = 0x0A;

    #endregion

    #region USB Audio Control Requests

    /// <summary>
    /// 设置当前值
    /// </summary>
    public const byte SET_CUR = 0x01;
    
    /// <summary>
    /// 获取当前值
    /// </summary>
    public const byte GET_CUR = 0x81;
    
    /// <summary>
    /// 获取最小值
    /// </summary>
    public const byte GET_MIN = 0x82;
    
    /// <summary>
    /// 获取最大值
    /// </summary>
    public const byte GET_MAX = 0x83;
    
    /// <summary>
    /// 获取分辨率
    /// </summary>
    public const byte GET_RES = 0x84;
    
    /// <summary>
    /// 设置内存
    /// </summary>
    public const byte SET_MEM = 0x05;
    
    /// <summary>
    /// 获取内存
    /// </summary>
    public const byte GET_MEM = 0x85;
    
    /// <summary>
    /// 获取状态
    /// </summary>
    public const byte GET_STAT = 0xFF;

    #endregion

    #region Request Type Building Helpers

    /// <summary>
    /// 构建 bmRequestType (Host-to-Device, Class, Interface)
    /// </summary>
    public const byte BMREQUEST_TYPE_CLASS_INTERFACE_OUT = 0x21;
    
    /// <summary>
    /// 构建 bmRequestType (Device-to-Host, Class, Interface)
    /// </summary>
    public const byte BMREQUEST_TYPE_CLASS_INTERFACE_IN = 0xA1;
    
    /// <summary>
    /// 构建 bmRequestType (Host-to-Device, Class, Endpoint)
    /// </summary>
    public const byte BMREQUEST_TYPE_CLASS_ENDPOINT_OUT = 0x22;
    
    /// <summary>
    /// 构建 bmRequestType (Device-to-Host, Class, Endpoint)
    /// </summary>
    public const byte BMREQUEST_TYPE_CLASS_ENDPOINT_IN = 0xA2;

    /// <summary>
    /// 构建 wValue (UAC 1.0 格式)
    /// 格式: (ControlSelector << 8) | Channel
    /// 
    /// 根据抓包数据:
    /// wValue: 0x0100 表示:
    ///   - ControlSelector: 0x01 (MUTE_CONTROL)
    ///   - Channel: 0x00 (Master Channel)
    /// </summary>
    public static ushort BuildWValue(byte controlSelector, byte channel = 0)
    {
        return (ushort)((controlSelector << 8) | channel);
    }

    /// <summary>
    /// 构建 wIndex (UAC 1.0 格式)
    /// 格式: InterfaceNumber
    /// </summary>
    public static ushort BuildWIndexUac1(byte interfaceNumber)
    {
        return interfaceNumber;
    }

    /// <summary>
    /// 构建 wIndex (UAC 2.0 格式)
    /// 格式: (UnitID << 8) | InterfaceNumber
    /// </summary>
    public static ushort BuildWIndexUac2(byte unitId, byte interfaceNumber)
    {
        return (ushort)((unitId << 8) | interfaceNumber);
    }

    #endregion
}