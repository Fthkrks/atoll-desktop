using System.Runtime.InteropServices;

namespace HyTranslate.Core.Translation;

internal static class PhysicalCores
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetLogicalProcessorInformationEx(int relationship, IntPtr buffer, ref uint length);

    public static int Count()
    {
        if (!OperatingSystem.IsWindows()) return Math.Max(1, Environment.ProcessorCount / 2);
        uint length = 0;
        GetLogicalProcessorInformationEx(0, IntPtr.Zero, ref length);
        if (length == 0) return Math.Max(1, Environment.ProcessorCount / 2);
        var buffer = Marshal.AllocHGlobal((int)length);
        try
        {
            if (!GetLogicalProcessorInformationEx(0, buffer, ref length))
                return Math.Max(1, Environment.ProcessorCount / 2);
            int count = 0;
            for (int offset = 0; offset < length;)
            {
                int size = Marshal.ReadInt32(buffer, offset + 4);
                if (size < 8) break;
                count++;
                offset += size;
            }
            return Math.Max(1, count);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
}
