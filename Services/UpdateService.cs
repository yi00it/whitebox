using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;

namespace Whitebox.Services;

public sealed record UpdateInfo(Version Version, string SetupUrl, string SumsUrl, string PageUrl);

/// <summary>Checks GitHub Releases once a day and installs a newer version through its setup program.</summary>
public static class UpdateService
{
    private const string LatestApi = "https://api.github.com/repos/yi00it/whitebox/releases/latest";
    private const string SettingsKey = @"Software\Whitebox";
    private const string LastCheckValue = "LastUpdateCheck";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromDays(1);

    private static readonly HttpClient Http = CreateClient();

    /// <summary>True when this copy was put in place by the installer, so it can upgrade itself.</summary>
    public static bool IsInstalled { get; } =
        Path.GetDirectoryName(Environment.ProcessPath) is { } dir && File.Exists(Path.Combine(dir, "unins000.exe"));

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"Whitebox/{App.Version}");   // GitHub's API rejects requests without one
        return http;
    }

    /// <summary>Asks GitHub for the latest release if the last check was a day or more ago. Null when up to date, not due, or offline.</summary>
    /// <remarks>A found update doesn't reset the clock, so it's offered again on every launch until the user picks Later (<see cref="Snooze"/>).</remarks>
    public static async Task<UpdateInfo?> CheckIfDueAsync()
    {
        if (DateTime.UtcNow - LoadLastCheck() < CheckInterval) return null;
        try
        {
            var update = await FetchLatestAsync();
            if (update == null) SaveLastCheck();
            return update;
        }
        catch
        {
            return null;   // offline or GitHub unavailable; try again on the next tick
        }
    }

    private static async Task<UpdateInfo?> FetchLatestAsync()
    {
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(LatestApi));
        var root = doc.RootElement;
        if (!Version.TryParse(root.GetProperty("tag_name").GetString()?.TrimStart('v'), out var latest)) return null;
        if (!Version.TryParse(App.Version, out var current) || latest <= current) return null;

        string? setup = null, sums = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            var url = asset.GetProperty("browser_download_url").GetString();
            if (name.EndsWith("-setup.exe", StringComparison.OrdinalIgnoreCase)) setup = url;
            else if (name == "SHA256SUMS.txt") sums = url;
        }
        if (setup == null || sums == null) return null;
        return new UpdateInfo(latest, setup, sums, root.GetProperty("html_url").GetString() ?? App.ProjectUrl);
    }

    /// <summary>Downloads and verifies the setup program, starts a silent upgrade and closes the app; the installer reopens it.</summary>
    public static async Task InstallAsync(UpdateInfo update, IProgress<double> progress)
    {
        var dir = Path.Combine(Path.GetTempPath(), "Whitebox-update");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        var fileName = Path.GetFileName(new Uri(update.SetupUrl).LocalPath);
        var setupPath = Path.Combine(dir, fileName);

        using (var response = await Http.GetAsync(update.SetupUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            long? total = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync();
            await using var target = File.Create(setupPath);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read));
                done += read;
                if (total > 0) progress.Report((double)done / total.Value);
            }
        }

        var sums = await Http.GetStringAsync(update.SumsUrl);
        var expected = sums.Split('\n')
            .Select(line => line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .FirstOrDefault(parts => parts.Length == 2 && parts[1] == fileName)?[0];
        string actual;
        await using (var fs = File.OpenRead(setupPath))
            actual = Convert.ToHexString(await SHA256.HashDataAsync(fs));
        if (expected == null || !actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(setupPath);
            throw new InvalidOperationException("The downloaded update didn't match its published checksum, so it wasn't installed.");
        }

        // /UPDATE=1 tells the installer to reopen Whitebox once it's done.
        Process.Start(new ProcessStartInfo(setupPath, "/SILENT /SUPPRESSMSGBOXES /NORESTART /UPDATE=1") { UseShellExecute = true });
        Application.Current.Shutdown();
    }

    /// <summary>"Later": don't offer updates again until the next daily check.</summary>
    public static void Snooze() => SaveLastCheck();

    private static DateTime LoadLastCheck()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(SettingsKey);
            return k?.GetValue(LastCheckValue) is string s && DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t)
                ? t : DateTime.MinValue;
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static void SaveLastCheck()
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(SettingsKey);
            k.SetValue(LastCheckValue, DateTime.UtcNow.ToString("o"));
        }
        catch { /* we'll just check again sooner */ }
    }
}
