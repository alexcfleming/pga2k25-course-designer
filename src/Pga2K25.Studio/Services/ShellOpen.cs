using System.Diagnostics;

namespace Pga2K25.Studio.Services;

/// <summary>
/// Hands a file or folder to Windows to open in its usual app. Windows can refuse (no app for the file type, the
/// app failed to start, a OneDrive file that isn't available), and that must not take the studio down with it:
/// these return a message to show instead of throwing.
/// </summary>
public static class ShellOpen
{
    /// <summary>Opens the file or folder; null if it went, otherwise why not.</summary>
    public static string? Open(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return $"{Path.GetFileName(path)} isn't there any more. Rebuild it.";
        return Run(new ProcessStartInfo(path) { UseShellExecute = true }, path);
    }

    /// <summary>Shows the file selected in File Explorer; null if it went, otherwise why not.</summary>
    public static string? Reveal(string path)
    {
        if (!File.Exists(path)) return Open(Path.GetDirectoryName(path) ?? path);
        return Run(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }, path);
    }

    static string? Run(ProcessStartInfo info, string path)
    {
        try
        {
            Process.Start(info)?.Dispose();
            return null;
        }
        catch (Exception ex)
        {
            Program.LogCrash(new InvalidOperationException($"Could not open {path}", ex));
            return $"Windows couldn't open {Path.GetFileName(path)}: {ex.Message}";
        }
    }
}
