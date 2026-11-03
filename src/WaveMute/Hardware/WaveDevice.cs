using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WaveMute.Hardware;

// Wave:3 USB Audio Class microphone feature unit 6, control interface 3.
// Only the one-byte microphone mute control is written; no configuration blocks,
// firmware, gain, headphone volume, lighting, or audio drivers are changed.
public sealed class WaveDevice : IDisposable
{
    private static readonly Guid InterfaceGuid = new("E70242C7-FB25-443B-9E7E-A4260F373982");
    private SafeFileHandle? _file;
    private nint _usb;
    public string? DevicePath { get; private set; }

    public bool ReadMute()
    {
        EnsureOpen();
        var data = Transfer(0xA1, 0x81, 0x0100, 0x0603, [0]);
        if (data[0] > 1) { Dispose(); throw new IOException("Wave:3 returned an invalid microphone mute state."); }
        return data[0] != 0;
    }

    public void SetMute(bool muted)
    {
        EnsureOpen();
        Transfer(0x21, 0x01, 0x0100, 0x0603, [(byte)(muted ? 1 : 0)]);
        if (ReadMute() != muted) throw new IOException("Wave:3 did not confirm the requested mute state.");
    }

    public byte[] ReadConfiguration()
    {
        EnsureOpen();
        return Transfer(0xA1, 0x85, 0, 0x3303, new byte[16]);
    }

    private byte[] Transfer(byte type, byte request, ushort value, ushort index, byte[] buffer)
    {
        var packet = new SetupPacket { RequestType = type, Request = request, Value = value, Index = index, Length = (ushort)buffer.Length };
        if (!WinUsb_ControlTransfer(_usb, packet, buffer, (uint)buffer.Length, out var transferred, 0))
        {
            var error = Marshal.GetLastWin32Error();
            Dispose();
            throw new Win32Exception(error, "Wave:3 USB control transfer failed");
        }
        if (transferred != buffer.Length) { Dispose(); throw new IOException($"Wave:3 returned {transferred} bytes; expected {buffer.Length}."); }
        return buffer;
    }

    private void EnsureOpen()
    {
        if (_usb != 0) return;
        var paths = FindDevicePaths().Where(p => p.Contains("vid_0fd9&pid_0070&mi_03", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (paths.Length == 0) throw new IOException("Elgato Wave:3 is not connected, or its WinUSB control interface is unavailable.");
        if (paths.Length > 1) throw new IOException("More than one Wave:3 is connected. Disconnect the extra device to select the microphone unambiguously.");
        _file = CreateFile(paths[0], 0xC0000000, 3, 0, 3, 0x40000000, 0);
        if (_file.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            Dispose();
            throw new Win32Exception(error, "Cannot open the Wave:3 control interface. Another app may be using it; close other Wave control apps and choose Reconnect");
        }
        if (!WinUsb_Initialize(_file, out _usb))
        {
            var error = Marshal.GetLastWin32Error();
            Dispose();
            throw new Win32Exception(error, "Cannot initialize the Wave:3 control interface");
        }
        uint timeout = 700;
        if (!WinUsb_SetPipePolicy(_usb, 0, 3, sizeof(uint), ref timeout))
        {
            var error = Marshal.GetLastWin32Error();
            Dispose();
            throw new Win32Exception(error, "Cannot set the Wave:3 USB timeout");
        }
        DevicePath = paths[0];
    }

    private static IEnumerable<string> FindDevicePaths()
    {
        var guid = InterfaceGuid;
        var info = SetupDiGetClassDevs(ref guid, null, 0, 0x12);
        if (info == -1) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            for (uint i = 0; ; i++)
            {
                var item = new DeviceInterfaceData { Size = Marshal.SizeOf<DeviceInterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(info, 0, ref guid, i, ref item))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == 259) yield break;
                    throw new Win32Exception(error);
                }
                SetupDiGetDeviceInterfaceDetail(info, ref item, 0, 0, out var required, 0);
                var detail = Marshal.AllocHGlobal((int)required);
                try
                {
                    Marshal.WriteInt32(detail, nint.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(info, ref item, detail, required, out _, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    yield return Marshal.PtrToStringUni(detail + 4)!;
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(info); }
    }

    public void Dispose()
    {
        if (_usb != 0) { WinUsb_Free(_usb); _usb = 0; }
        _file?.Dispose();
        _file = null;
        DevicePath = null;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct SetupPacket { public byte RequestType, Request; public ushort Value, Index, Length; }
    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInterfaceData { public int Size; public Guid ClassGuid; public int Flags; public nuint Reserved; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFile(string name, uint access, uint share, nint security, uint creation, uint flags, nint template);
    [DllImport("winusb.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool WinUsb_Initialize(SafeFileHandle file, out nint handle);
    [DllImport("winusb.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool WinUsb_Free(nint handle);
    [DllImport("winusb.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool WinUsb_SetPipePolicy(nint handle, byte pipe, uint policy, uint length, ref uint value);
    [DllImport("winusb.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool WinUsb_ControlTransfer(nint handle, SetupPacket packet, [In, Out] byte[] buffer, uint length, out uint transferred, nint overlapped);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint SetupDiGetClassDevs(ref Guid guid, string? enumerator, nint parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiEnumDeviceInterfaces(nint info, nint device, ref Guid guid, uint index, ref DeviceInterfaceData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiGetDeviceInterfaceDetail(nint info, ref DeviceInterfaceData data, nint detail, uint size, out uint required, nint device);
    [DllImport("setupapi.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiDestroyDeviceInfoList(nint info);
}
