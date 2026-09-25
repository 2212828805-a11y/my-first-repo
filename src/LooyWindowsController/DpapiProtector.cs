using System.Runtime.InteropServices;
using System.Text;

namespace Looy.WindowsController;

internal static class DpapiProtector
{
    private const int CryptProtectUiForbidden = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Length;
        public IntPtr Data;
    }

    [DllImport("Crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        ref DataBlob dataOut);

    [DllImport("Crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        ref DataBlob dataOut);

    [DllImport("Kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr memory);

    public static string Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return string.Empty;
        }

        var bytes = Encoding.UTF8.GetBytes(plainText);
        var input = CreateBlob(bytes);
        var output = new DataBlob();
        try
        {
            if (!CryptProtectData(
                    ref input,
                    "LOOY MCP endpoint",
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    ref output))
            {
                throw new InvalidOperationException($"无法加密 MCP 地址，错误码：{Marshal.GetLastWin32Error()}");
            }

            var protectedBytes = new byte[output.Length];
            Marshal.Copy(output.Data, protectedBytes, 0, output.Length);
            return Convert.ToBase64String(protectedBytes);
        }
        finally
        {
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero)
            {
                LocalFree(output.Data);
            }
        }
    }

    public static string Unprotect(string protectedText)
    {
        if (string.IsNullOrWhiteSpace(protectedText))
        {
            return string.Empty;
        }

        var bytes = Convert.FromBase64String(protectedText);
        var input = CreateBlob(bytes);
        var output = new DataBlob();
        try
        {
            if (!CryptUnprotectData(
                    ref input,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    ref output))
            {
                throw new InvalidOperationException($"无法解密 MCP 地址，错误码：{Marshal.GetLastWin32Error()}");
            }

            var plainBytes = new byte[output.Length];
            Marshal.Copy(output.Data, plainBytes, 0, output.Length);
            return Encoding.UTF8.GetString(plainBytes);
        }
        finally
        {
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero)
            {
                LocalFree(output.Data);
            }
        }
    }

    private static DataBlob CreateBlob(byte[] bytes)
    {
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        return new DataBlob { Length = bytes.Length, Data = pointer };
    }
}
