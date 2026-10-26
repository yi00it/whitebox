using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using Whitebox.Core;

namespace Whitebox.Services;

public static class AdminHelper
{
    public static bool IsAdmin { get; } = Check();

    private static bool Check()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public static void RestartElevated()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" });
            Application.Current.Shutdown();
        }
        catch (Win32Exception)
        {
            // user dismissed the UAC prompt
        }
    }
}

public static class Shell
{
    public static void Reveal(string path)
    {
        try
        {
            if (File.Exists(path) || Directory.Exists(path))
                Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        catch (Exception ex)
        {
            Notifier.Show("Couldn't open Explorer", ex.Message, ToastKind.Error);
        }
    }

    public static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Notifier.Show("Couldn't open", ex.Message, ToastKind.Error);
        }
    }

    /// <summary>Moves files or folders to the Recycle Bin in batches. Returns the paths that are gone afterwards.</summary>
    public static List<string> Recycle(IReadOnlyList<string> paths)
    {
        const int batch = 400;
        for (int i = 0; i < paths.Count; i += batch)
        {
            var chunk = paths.Skip(i).Take(batch).ToList();
            var op = new Native.SHFILEOPSTRUCT
            {
                wFunc = Native.FO_DELETE,
                pFrom = string.Join('\0', chunk) + '\0',
                fFlags = (ushort)(Native.FOF_ALLOWUNDO | Native.FOF_NOCONFIRMATION | Native.FOF_SILENT | Native.FOF_NOERRORUI),
            };
            Native.SHFileOperation(ref op);
        }
        return paths.Where(p => !File.Exists(p) && !Directory.Exists(p)).ToList();
    }

    /// <summary>Runs a process elevated and waits for it to finish. Returns the exit code, or null if cancelled.</summary>
    public static async Task<int?> RunElevatedAsync(string file, string args, bool hidden = true)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = true,
                Verb = AdminHelper.IsAdmin ? "" : "runas",
                WindowStyle = hidden ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal,
            };
            using var p = Process.Start(psi);
            if (p == null) return null;
            await p.WaitForExitAsync();
            return p.ExitCode;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    public static async Task<string> RunCaptureAsync(string file, string args)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi)!;
        var output = await p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();
        return output;
    }
}
