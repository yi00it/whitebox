using Whitebox.Core;
using Whitebox.Services;

namespace Whitebox.ViewModels;

public sealed class LocationOption
{
    public required string Label { get; init; }
    public required string Path { get; init; }
    public string? Detail { get; init; }

    public static List<LocationOption> Drives() =>
        SystemInfo.FixedDrives().Select(d => new LocationOption
        {
            Label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? d.Name.TrimEnd('\\') : $"{d.VolumeLabel} ({d.Name.TrimEnd('\\')})",
            Path = d.RootDirectory.FullName,
            Detail = $"{Fmt.Bytes(d.AvailableFreeSpace)} free of {Fmt.Bytes(d.TotalSize)}",
        }).ToList();

    public static LocationOption Folder(string path) => new()
    {
        Label = System.IO.Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } n ? n : path,
        Path = path,
    };

    public static LocationOption? Browse()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Choose a folder to scan" };
        return dlg.ShowDialog() == true ? Folder(dlg.FolderName) : null;
    }
}

public sealed class SizeOption
{
    public required string Label { get; init; }
    public long Bytes { get; init; }
}
