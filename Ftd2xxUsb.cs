using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Ad7606Host;

internal sealed class Ftd2xxUsb : IDisposable
{
    private const uint FT_OK = 0;
    private const uint FT_LIST_BY_SERIAL_NUMBER = 1;

    [DllImport("ftd2xx64.dll",
    EntryPoint = "FT_GetDeviceInfoDetail",
    CallingConvention = CallingConvention.StdCall)]
    private static extern uint FT_GetDeviceInfoDetail(
    uint index,
    out uint flags,
    out uint type,
    out uint id,
    out uint locationId,
    StringBuilder serialNumber,
    StringBuilder description,
    out IntPtr handle);

    private const uint FT_LIST_BY_INDEX = 0x40000000;
    private const uint FT_OPEN_BY_SERIAL_NUMBER = 0x00000001;

    private const byte FT_BITMODE_RESET = 0x00;
    private const byte FT_BITMODE_SYNC_FIFO = 0x40;
    private const uint FT_PURGE_RX = 1, FT_PURGE_TX = 2;
    private IntPtr handle;
    private byte[] tempBuffer = new byte[256 * 1024];

    [DllImport("ftd2xx64.dll", EntryPoint = "FT_Open", CallingConvention = CallingConvention.StdCall)]
    private static extern uint FT_Open(int index, out IntPtr handle);

    [DllImport("ftd2xx64.dll", EntryPoint = "FT_OpenEx", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
    private static extern uint FT_OpenEx([MarshalAs(UnmanagedType.LPStr)] string arg, uint flags, out IntPtr handle);

    [DllImport("ftd2xx64.dll", EntryPoint = "FT_CreateDeviceInfoList", CallingConvention = CallingConvention.StdCall)]
    private static extern uint FT_CreateDeviceInfoList(out uint numberOfDevices);

    [DllImport("ftd2xx64.dll", EntryPoint = "FT_ListDevices",
    CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
    private static extern uint FT_ListDevices(
    IntPtr arg1,
    StringBuilder arg2,
    uint flags);

    [DllImport("ftd2xx64.dll", CallingConvention = CallingConvention.StdCall)] private static extern uint FT_Close(IntPtr h);
    [DllImport("ftd2xx64.dll", CallingConvention = CallingConvention.StdCall)] private static extern uint FT_ResetDevice(IntPtr h);
    [DllImport("ftd2xx64.dll", CallingConvention = CallingConvention.StdCall)] private static extern uint FT_SetBitMode(IntPtr h, byte mask, byte mode);
    [DllImport("ftd2xx64.dll", CallingConvention = CallingConvention.StdCall)] private static extern uint FT_SetUSBParameters(IntPtr h, uint input, uint output);
    [DllImport("ftd2xx64.dll", CallingConvention = CallingConvention.StdCall)] private static extern uint FT_SetLatencyTimer(IntPtr h, byte timer);
    [DllImport("ftd2xx64.dll", CallingConvention = CallingConvention.StdCall)] private static extern uint FT_SetTimeouts(IntPtr h, uint read, uint write);
    [DllImport("ftd2xx64.dll", CallingConvention = CallingConvention.StdCall)] private static extern uint FT_Purge(IntPtr h, uint mask);
    [DllImport("ftd2xx64.dll", CallingConvention = CallingConvention.StdCall)] private static extern uint FT_GetQueueStatus(IntPtr h, out uint count);
    [DllImport("ftd2xx64.dll", CallingConvention = CallingConvention.StdCall)] private static extern uint FT_Read(IntPtr h, byte[] buffer, uint length, out uint read);
    [DllImport("ftd2xx64.dll", CallingConvention = CallingConvention.StdCall)] private static extern uint FT_Write(IntPtr h, byte[] buffer, uint length, out uint written);

    public void Open(int index)
    {
        Check(FT_Open(index, out handle), "打开 USB");
        try { Initialize(); }
        catch { Dispose(); throw; }
    }

    public static string[] GetSerialNumbers()
    {
        Check(
            FT_CreateDeviceInfoList(out uint count),
            "枚举 USB 设备"
        );

        var serialNumbers = new List<string>((int)count);

        for (uint index = 0; index < count; index++)
        {
            var serial = new StringBuilder(256);
            var description = new StringBuilder(256);

            Check(
                FT_GetDeviceInfoDetail(
                    index,
                    out uint flags,
                    out uint type,
                    out uint id,
                    out uint locationId,
                    serial,
                    description,
                    out IntPtr handle
                ),
                $"读取设备信息 Index={index}"
            );

            if (serial.Length > 0)
            {
                serialNumbers.Add(serial.ToString());
                Debug.WriteLine(
                    $"Index={index}, Serial={serial}, Description={description}"
                );
            }
        }

        return serialNumbers.ToArray();
    }

    public void OpenBySerial(string serialNumber)
    {
        if (string.IsNullOrWhiteSpace(serialNumber))
            throw new ArgumentException("序列号不能为空", nameof(serialNumber));

        handle = IntPtr.Zero;
        Check(FT_OpenEx(serialNumber, FT_OPEN_BY_SERIAL_NUMBER, out handle), $"按序列号打开 USB（{serialNumber}）");
        try { Initialize(); }
        catch { Dispose(); throw; }
    }

    private void Initialize()
    {
        Check(FT_ResetDevice(handle), "复位 USB");
        Check(FT_SetUSBParameters(handle, 65536, 65536), "设置 USB 缓冲区");
        Check(FT_SetLatencyTimer(handle, 1), "设置 USB 延迟");       // 1ms，流模式要尽量小
        Check(FT_SetTimeouts(handle, 2, 1000), "设置 USB 超时");    // 单次 FT_Read 最多阻塞 2ms
        Check(FT_SetBitMode(handle, 0xFF, FT_BITMODE_RESET), "退出位模式");
        Check(FT_SetBitMode(handle, 0xFF, FT_BITMODE_SYNC_FIFO), "启用同步 FIFO");
        Check(FT_Purge(handle, FT_PURGE_RX | FT_PURGE_TX), "清空 USB 缓冲区");
    }

    public void Write(byte[] data) => Check(FT_Write(handle, data, (uint)data.Length, out _), "发送 USB 命令");

    public void Purge() => Check(FT_Purge(handle, FT_PURGE_RX | FT_PURGE_TX), "清空 USB 缓冲区");

    /// <summary>
    /// 单次阻塞 FT_Read：有数据立即返回（可能少于 maxBytes），无数据最多阻塞 2ms 后返回空数组。
    /// 比"GetQueueStatus + Sleep(1)"循环效率高得多，是持续流的关键。
    /// </summary>
    public byte[] ReadAvailable(int maxBytes)
    {
        if (maxBytes <= 0) return Array.Empty<byte>();
        if (maxBytes > tempBuffer.Length) tempBuffer = new byte[maxBytes];

        uint status = FT_Read(handle, tempBuffer, (uint)maxBytes, out uint got);
        if (status != FT_OK) Check(status, "读取 USB 数据");
        if (got == 0) return Array.Empty<byte>();

        var result = new byte[got];
        Buffer.BlockCopy(tempBuffer, 0, result, 0, (int)got);
        return result;
    }

    public byte[] ReadExact(int length, TimeSpan timeout)
    {
        byte[] result = new byte[length];
        int offset = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (offset < length)
        {
            if (sw.Elapsed >= timeout) throw new TimeoutException($"USB 响应超时：已收到 {offset}/{length} 字节");
            uint take = Math.Min((uint)(length - offset), 65536u);
            if (take > tempBuffer.Length) tempBuffer = new byte[take];
            Check(FT_Read(handle, tempBuffer, take, out uint got), "读取 USB 数据");
            if (got == 0) continue;
            Buffer.BlockCopy(tempBuffer, 0, result, offset, (int)got);
            offset += (int)got;
        }
        return result;
    }

    private static void Check(uint status, string operation)
    {
        if (status == FT_OK) return;
        string hint = status switch
        {
            1 => "FT_INVALID_HANDLE",
            2 => "FT_DEVICE_NOT_FOUND（未找到该设备，检查连接或驱动）",
            3 => "FT_DEVICE_NOT_OPENED（设备存在但打不开：被占用或驱动模式不对）",
            4 => "FT_IO_ERROR",
            5 => "FT_INSUFFICIENT_RESOURCES",
            6 => "FT_INVALID_PARAMETER",
            10 => "FT_DEVICE_NOT_FOUND",
            14 => "FT_OTHER_ERROR（常见于序列号不匹配）",
            _ => "未知错误"
        };
        throw new InvalidOperationException($"{operation}失败，D2XX 状态码 {status}：{hint}");
    }

    public void Dispose()
    {
        if (handle != IntPtr.Zero)
        {
            try { FT_SetBitMode(handle, 0, FT_BITMODE_RESET); } catch { }
            FT_Close(handle);
            handle = IntPtr.Zero;
        }
    }
}