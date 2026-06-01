using System;
using System.IO;
using Microsoft.Win32;

namespace CelestiCloud.GUI.Helpers;

public static class StartupManager
{
    private const string AppName = "CelestiCloud";

    public static void SetStartup(bool enable)
    {
        string exePath = Environment.ProcessPath ?? string.Empty;
        if (string.IsNullOrEmpty(exePath)) return;

        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key != null)
            {
                if (enable)
                    key.SetValue(AppName, $"\"{exePath}\" --minimized"); // Starts minimized directly to tray
                else
                    key.DeleteValue(AppName, false);
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            string homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string plistPath = Path.Combine(homeDir, "Library", "LaunchAgents", "com.celesticloud.gui.plist");

            if (enable)
            {
                string plistContent = $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                <plist version="1.0">
                <dict>
                    <key>Label</key>
                    <string>com.celesticloud.gui</string>
                    <key>ProgramArguments</key>
                    <array>
                        <string>{exePath}</string>
                        <string>--minimized</string>
                    </array>
                    <key>RunAtLoad</key>
                    <true/>
                </dict>
                </plist>
                """;
                Directory.CreateDirectory(Path.GetDirectoryName(plistPath)!);
                File.WriteAllText(plistPath, plistContent);
            }
            else if (File.Exists(plistPath))
            {
                File.Delete(plistPath);
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            string homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string desktopPath = Path.Combine(homeDir, ".config", "autostart", "celesticloud.desktop");

            if (enable)
            {
                string desktopContent = $"""
                [Desktop Entry]
                Type=Application
                Version=1.0
                Name=CelestiCloud
                Comment=Background sync daemon
                Exec="{exePath}" --minimized
                StartupNotify=false
                Terminal=false
                """;
                Directory.CreateDirectory(Path.GetDirectoryName(desktopPath)!);
                File.WriteAllText(desktopPath, desktopContent);
            }
            else if (File.Exists(desktopPath))
            {
                File.Delete(desktopPath);
            }
        }
    }
}