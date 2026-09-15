using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using static BatteryHealthChecker.Native.BatteryNative;

namespace BatteryHealthChecker.Native;

/// <summary>Raised when a battery device query fails in a way the caller should surface.</summary>
internal sealed class BatteryDeviceException : Exception
{
    public BatteryDeviceException(string message, int win32Error) : base(message)
    {
        Win32Error = win32Error;
    }

    public int Win32Error { get; }

    /// <summary>
    /// True only for genuine access failures. SRS 19: recommend administrator mode
    /// only when elevation may actually resolve the issue - a driver that simply does
    /// not implement an information level is not such a case.
    /// </summary>
    public bool ElevationMayHelp => Win32Error == ERROR_ACCESS_DENIED;
}

/// <summary>
/// An open handle to one battery device, scoped to a single scan.
///
/// All queries return null for "the driver does not expose this", and throw only for
/// failures worth telling the user about. Nothing here ever substitutes a default
/// value for missing data (SRS 2).
/// </summary>
internal sealed class BatteryDevice : IDisposable
{
    private readonly SafeFileHandle _handle;

    private BatteryDevice(SafeFileHandle handle, string devicePath, uint tag)
    {
        _handle = handle;
        DevicePath = devicePath;
        Tag = tag;
    }

    public string DevicePath { get; }

    public uint Tag { get; }

    /// <summary>
    /// Enumerates every present battery device interface and opens each one.
    /// Devices that cannot be opened or that have no tag (bay empty) are skipped.
    /// </summary>
    public static IReadOnlyList<BatteryDevice> OpenAll(Action<string, int>? onDeviceFailure = null)
    {
        var devices = new List<BatteryDevice>();
        IntPtr deviceInfoSet = SetupDiGetClassDevsW(
            ref GUID_DEVICE_BATTERY, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);

        if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1))
        {
            throw new BatteryDeviceException(
                "Windows did not return a battery device list.", Marshal.GetLastWin32Error());
        }

        try
        {
            for (uint index = 0; ; index++)
            {
                var interfaceData = new SP_DEVICE_INTERFACE_DATA
                {
                    cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>(),
                };

                if (!SetupDiEnumDeviceInterfaces(
                        deviceInfoSet, IntPtr.Zero, ref GUID_DEVICE_BATTERY, index, ref interfaceData))
                {
                    // ERROR_NO_MORE_ITEMS is the normal loop terminator.
                    break;
                }

                string? path = GetDevicePath(deviceInfoSet, ref interfaceData);
                if (path is null) continue;

                try
                {
                    BatteryDevice? device = Open(path);
                    if (device is not null) devices.Add(device);
                }
                catch (BatteryDeviceException ex)
                {
                    onDeviceFailure?.Invoke(path, ex.Win32Error);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }

        return devices;
    }

    private static string? GetDevicePath(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA interfaceData)
    {
        // First call sizes the buffer; ERROR_INSUFFICIENT_BUFFER is expected here.
        SetupDiGetDeviceInterfaceDetailW(
            deviceInfoSet, ref interfaceData, IntPtr.Zero, 0, out uint requiredSize, IntPtr.Zero);
        if (requiredSize == 0) return null;

        IntPtr buffer = Marshal.AllocHGlobal((int)requiredSize);
        try
        {
            // cbSize is the size of the fixed part of SP_DEVICE_INTERFACE_DETAIL_DATA_W,
            // not of the whole buffer: 8 on 64-bit, 6 on 32-bit.
            Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);

            if (!SetupDiGetDeviceInterfaceDetailW(
                    deviceInfoSet, ref interfaceData, buffer, requiredSize, out _, IntPtr.Zero))
            {
                return null;
            }

            return Marshal.PtrToStringUni(buffer + 4);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static BatteryDevice? Open(string devicePath)
    {
        SafeFileHandle handle = CreateFileW(
            devicePath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);

        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();

            // Some miniports refuse write access to a standard user; the query IOCTLs
            // only need read access, so retry before giving up.
            handle = CreateFileW(
                devicePath, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);

            if (handle.IsInvalid)
            {
                int retryError = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw new BatteryDeviceException(
                    $"Unable to open battery device: {new Win32Exception(retryError).Message}",
                    retryError == 0 ? error : retryError);
            }
        }

        try
        {
            uint? tag = QueryTag(handle);
            if (tag is null || tag.Value == BATTERY_TAG_INVALID)
            {
                // A present interface with no tag means the bay is empty. Not an error.
                handle.Dispose();
                return null;
            }

            return new BatteryDevice(handle, devicePath, tag.Value);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static uint? QueryTag(SafeFileHandle handle)
    {
        IntPtr inBuffer = Marshal.AllocHGlobal(sizeof(uint));
        IntPtr outBuffer = Marshal.AllocHGlobal(sizeof(uint));
        try
        {
            Marshal.WriteInt32(inBuffer, 0); // zero wait: do not block the scan
            bool ok = DeviceIoControl(
                handle, IOCTL_BATTERY_QUERY_TAG, inBuffer, sizeof(uint),
                outBuffer, sizeof(uint), out uint returned, IntPtr.Zero);

            if (!ok || returned < sizeof(uint))
            {
                int error = Marshal.GetLastWin32Error();
                if (error is ERROR_FILE_NOT_FOUND or ERROR_NO_MORE_ITEMS) return null;
                if (error == ERROR_ACCESS_DENIED)
                {
                    throw new BatteryDeviceException(
                        "Access to this battery device was denied.", ERROR_ACCESS_DENIED);
                }
                return null;
            }

            return unchecked((uint)Marshal.ReadInt32(outBuffer));
        }
        finally
        {
            Marshal.FreeHGlobal(inBuffer);
            Marshal.FreeHGlobal(outBuffer);
        }
    }

    /// <summary>Queries a fixed-size information level. Returns null when unsupported.</summary>
    public T? QueryInformation<T>(BatteryQueryInformationLevel level, int atRate = 0) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        IntPtr outBuffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!TryQuery(level, atRate, outBuffer, (uint)size, out uint returned) || returned < size)
            {
                return null;
            }
            return Marshal.PtrToStructure<T>(outBuffer);
        }
        finally
        {
            Marshal.FreeHGlobal(outBuffer);
        }
    }

    /// <summary>Queries a ULONG information level (temperature, estimated time, granularity).</summary>
    public uint? QueryUInt32(BatteryQueryInformationLevel level, int atRate = 0)
    {
        IntPtr outBuffer = Marshal.AllocHGlobal(sizeof(uint));
        try
        {
            if (!TryQuery(level, atRate, outBuffer, sizeof(uint), out uint returned)
                || returned < sizeof(uint))
            {
                return null;
            }
            return unchecked((uint)Marshal.ReadInt32(outBuffer));
        }
        finally
        {
            Marshal.FreeHGlobal(outBuffer);
        }
    }

    /// <summary>Queries a Unicode string information level (name, manufacturer, serial, unique id).</summary>
    public string? QueryString(BatteryQueryInformationLevel level)
    {
        const uint initialSize = 512;
        uint size = initialSize;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            IntPtr outBuffer = Marshal.AllocHGlobal((int)size);
            try
            {
                // Zero the buffer so a driver that under-fills it cannot leave us reading
                // stale heap bytes as if they were part of the string.
                for (int i = 0; i < size; i += sizeof(long))
                {
                    Marshal.WriteInt64(outBuffer, i, 0);
                }

                if (TryQuery(level, 0, outBuffer, size, out uint returned))
                {
                    if (returned == 0) return null;
                    string? value = Marshal.PtrToStringUni(outBuffer);
                    return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                }

                int error = Marshal.GetLastWin32Error();
                if (error == ERROR_INSUFFICIENT_BUFFER && returned > size && returned <= 64 * 1024)
                {
                    size = returned;
                    continue;
                }
                return null;
            }
            finally
            {
                Marshal.FreeHGlobal(outBuffer);
            }
        }

        return null;
    }

    /// <summary>Reads the live electrical status (power state, charge, voltage, rate).</summary>
    public BATTERY_STATUS? QueryStatus()
    {
        var wait = new BATTERY_WAIT_STATUS
        {
            BatteryTag = Tag,
            Timeout = 0,      // do not block; report whatever is current
            PowerState = 0,
            LowCapacity = 0,
            HighCapacity = 0,
        };

        int inSize = Marshal.SizeOf<BATTERY_WAIT_STATUS>();
        int outSize = Marshal.SizeOf<BATTERY_STATUS>();
        IntPtr inBuffer = Marshal.AllocHGlobal(inSize);
        IntPtr outBuffer = Marshal.AllocHGlobal(outSize);
        try
        {
            Marshal.StructureToPtr(wait, inBuffer, false);
            bool ok = DeviceIoControl(
                _handle, IOCTL_BATTERY_QUERY_STATUS, inBuffer, (uint)inSize,
                outBuffer, (uint)outSize, out uint returned, IntPtr.Zero);

            if (!ok || returned < outSize) return null;
            return Marshal.PtrToStructure<BATTERY_STATUS>(outBuffer);
        }
        finally
        {
            Marshal.DestroyStructure<BATTERY_WAIT_STATUS>(inBuffer);
            Marshal.FreeHGlobal(inBuffer);
            Marshal.FreeHGlobal(outBuffer);
        }
    }

    private bool TryQuery(
        BatteryQueryInformationLevel level, int atRate,
        IntPtr outBuffer, uint outSize, out uint bytesReturned)
    {
        var query = new BATTERY_QUERY_INFORMATION
        {
            BatteryTag = Tag,
            InformationLevel = level,
            AtRate = atRate,
        };

        int inSize = Marshal.SizeOf<BATTERY_QUERY_INFORMATION>();
        IntPtr inBuffer = Marshal.AllocHGlobal(inSize);
        try
        {
            Marshal.StructureToPtr(query, inBuffer, false);
            return DeviceIoControl(
                _handle, IOCTL_BATTERY_QUERY_INFORMATION, inBuffer, (uint)inSize,
                outBuffer, outSize, out bytesReturned, IntPtr.Zero);
        }
        finally
        {
            Marshal.DestroyStructure<BATTERY_QUERY_INFORMATION>(inBuffer);
            Marshal.FreeHGlobal(inBuffer);
        }
    }

    public void Dispose() => _handle.Dispose();
}
