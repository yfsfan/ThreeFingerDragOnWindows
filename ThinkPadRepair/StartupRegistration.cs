using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ThreeFingerDragOnWindows;
internal static class StartupRegistration {
    public const string ShortcutName = "ThreeFingerDrag-ThinkPadRepair.lnk";
    public static string ShortcutPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), ShortcutName);
    private static string Executable => Path.Combine(AppContext.BaseDirectory, "ThreeFingerDrag-ThinkPadRepair.exe");

    public static bool IsEnabled() => IsEnabled(ShortcutPath, Executable);
    internal static bool IsEnabled(string shortcutPath, string executable) {
        if(!File.Exists(shortcutPath)) return false;
        object shell = null, shortcut = null;
        try {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
            shortcut = ((dynamic)shell).CreateShortcut(shortcutPath);
            return string.Equals((string)((dynamic)shortcut).TargetPath, executable, StringComparison.OrdinalIgnoreCase)
                && string.Equals((string)((dynamic)shortcut).Arguments, "--tray", StringComparison.Ordinal);
        } catch { return false; }
        finally { Release(shortcut); Release(shell); }
    }

    public static void SetEnabled(bool enabled) => SetEnabled(enabled, ShortcutPath, Executable);
    internal static void SetEnabled(bool enabled, string shortcutPath, string executable) {
        if(!enabled) {
            if(IsEnabled(shortcutPath, executable)) File.Delete(shortcutPath);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath));
        object shell = null, shortcut = null;
        try {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
            shortcut = ((dynamic)shell).CreateShortcut(shortcutPath);
            dynamic link = shortcut;
            link.TargetPath = executable;
            link.Arguments = "--tray";
            link.WorkingDirectory = Path.GetDirectoryName(executable);
            link.IconLocation = executable + ",0";
            link.Description = "ThinkPad 三指拖动修正版：登录后启动到托盘";
            link.Save();
        } finally { Release(shortcut); Release(shell); }
    }
    private static void Release(object value) { if(value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
}
