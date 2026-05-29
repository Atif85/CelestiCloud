using System;
using System.Runtime.InteropServices;

namespace CelestiCloud.CLI.Util;

public static class WindowsConsoleHelper
{
    private const int STD_OUTPUT_HANDLE = -11; // Standard Output
    private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004; // Enable ANSI/VT100 processing

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    /// <summary>
    /// Forces standard Windows consoles (cmd/powershell) to natively support 
    /// ANSI escape sequences, preventing default system alert beeps during refreshes.
    /// </summary>
    public static void EnableAnsiSupport()
    {
        // Only run this if we are physically executing on Windows
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var iStdOut = GetStdHandle(STD_OUTPUT_HANDLE);
            if (GetConsoleMode(iStdOut, out uint outMode))
            {
                outMode |= ENABLE_VIRTUAL_TERMINAL_PROCESSING;
                SetConsoleMode(iStdOut, outMode);
            }
        }
    }
}