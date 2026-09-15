using System.Runtime.InteropServices;

namespace BatteryHealthChecker.Native;

/// <summary>
/// Constants, structures and P/Invoke signatures for the Windows battery device
/// interface (SRS 4: Windows battery APIs / battery device interfaces / ACPI).
///
/// This is the most authoritative source available without elevation: it surfaces
/// designed capacity, full-charged capacity, cycle count, chemistry, serial number
/// and temperature straight from the battery miniport driver.
/// </summary>
internal static class BatteryNative
{
    // {72631E54-78A4-11D0-BCF7-00AA00B7B32A}
    internal static Guid GUID_DEVICE_BATTERY = new("72631e54-78a4-11d0-bcf7-00aa00b7b32a");

    internal const uint DIGCF_PRESENT = 0x00000002;
    internal const uint DIGCF_DEVICEINTERFACE = 0x00000010;

    internal const uint GENERIC_READ = 0x80000000;
    internal const uint GENERIC_WRITE = 0x40000000;
    internal const uint FILE_SHARE_READ = 0x00000001;
    internal const uint FILE_SHARE_WRITE = 0x00000002;
    internal const uint OPEN_EXISTING = 3;
    internal const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

    internal const int ERROR_INSUFFICIENT_BUFFER = 122;
    internal const int ERROR_NO_MORE_ITEMS = 259;
    internal const int ERROR_ACCESS_DENIED = 5;
    internal const int ERROR_FILE_NOT_FOUND = 2;
    internal const int ERROR_NOT_SUPPORTED = 50;
    internal const int ERROR_INVALID_FUNCTION = 1;
    internal const int ERROR_GEN_FAILURE = 31;

    // CTL_CODE(FILE_DEVICE_BATTERY=0x29, function, METHOD_BUFFERED=0, FILE_READ_ACCESS=1)
    // = (0x29 << 16) | (1 << 14) | (function << 2)
    internal const uint IOCTL_BATTERY_QUERY_TAG = 0x294040;          // function 0x10
    internal const uint IOCTL_BATTERY_QUERY_INFORMATION = 0x294044;  // function 0x11
    internal const uint IOCTL_BATTERY_QUERY_STATUS = 0x29404C;       // function 0x13

    internal const uint BATTERY_TAG_INVALID = 0;
    internal const uint BATTERY_UNKNOWN_CAPACITY = 0xFFFFFFFF;
    internal const uint BATTERY_UNKNOWN_VOLTAGE = 0xFFFFFFFF;
    internal const uint BATTERY_UNKNOWN_TIME = 0xFFFFFFFF;
    internal const int BATTERY_UNKNOWN_RATE = unchecked((int)0x80000000);

    // BATTERY_INFORMATION.Capabilities
    internal const uint BATTERY_SYSTEM_BATTERY = 0x80000000;
    internal const uint BATTERY_CAPACITY_RELATIVE = 0x40000000;
    internal const uint BATTERY_IS_SHORT_TERM = 0x20000000;

    // BATTERY_STATUS.PowerState
    internal const uint BATTERY_POWER_ON_LINE = 0x00000001;
    internal const uint BATTERY_DISCHARGING = 0x00000002;
    internal const uint BATTERY_CHARGING = 0x00000004;
    internal const uint BATTERY_CRITICAL = 0x00000008;

    internal enum BatteryQueryInformationLevel
    {
        BatteryInformation = 0,
        BatteryGranularityInformation = 1,
        BatteryTemperature = 2,
        BatteryEstimatedTime = 3,
        BatteryDeviceName = 4,
        BatteryManufactureDate = 5,
        BatteryManufactureName = 6,
        BatteryUniqueID = 7,
        BatterySerialNumber = 8,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SP_DEVICE_INTERFACE_DATA
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BATTERY_QUERY_INFORMATION
    {
        public uint BatteryTag;
        public BatteryQueryInformationLevel InformationLevel;
        public int AtRate;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BATTERY_INFORMATION
    {
        public uint Capabilities;
        public byte Technology;
        public byte Reserved0;
        public byte Reserved1;
        public byte Reserved2;
        public byte Chemistry0;
        public byte Chemistry1;
        public byte Chemistry2;
        public byte Chemistry3;
        public uint DesignedCapacity;
        public uint FullChargedCapacity;
        public uint DefaultAlert1;
        public uint DefaultAlert2;
        public uint CriticalBias;
        public uint CycleCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BATTERY_WAIT_STATUS
    {
        public uint BatteryTag;
        public uint Timeout;
        public uint PowerState;
        public uint LowCapacity;
        public uint HighCapacity;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BATTERY_STATUS
    {
        public uint PowerState;
        public uint Capacity;
        public uint Voltage;
        public int Rate;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BATTERY_MANUFACTURE_DATE
    {
        public byte Day;
        public byte Month;
        public ushort Year;
    }

    /// <summary>SYSTEM_POWER_STATUS, the always-available floor-level source (SRS 4).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;        // 0 offline, 1 online, 255 unknown
        public byte BatteryFlag;         // bit field; 128 = no system battery, 255 = unknown
        public byte BatteryLifePercent;  // 0-100, 255 unknown
        public byte SystemStatusFlag;
        public int BatteryLifeTime;      // seconds, -1 unknown
        public int BatteryFullLifeTime;  // seconds, -1 unknown
    }

    internal const byte AC_LINE_OFFLINE = 0;
    internal const byte AC_LINE_ONLINE = 1;
    internal const byte AC_LINE_UNKNOWN = 255;
    internal const byte BATTERY_FLAG_CHARGING = 8;
    internal const byte BATTERY_FLAG_NO_BATTERY = 128;
    internal const byte BATTERY_FLAG_UNKNOWN = 255;
    internal const byte BATTERY_PERCENTAGE_UNKNOWN = 255;

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr SetupDiGetClassDevsW(
        ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid,
        uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiGetDeviceInterfaceDetailW(
        IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
        IntPtr deviceInterfaceDetailData, uint detailDataSize,
        out uint requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(
        string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeviceIoControl(
        Microsoft.Win32.SafeHandles.SafeFileHandle device, uint controlCode,
        IntPtr inBuffer, uint inBufferSize, IntPtr outBuffer, uint outBufferSize,
        out uint bytesReturned, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
}
