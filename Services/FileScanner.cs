using System.Collections.Concurrent;
using System.IO.Enumeration;
using System.Security.Cryptography;
using Whitebox.Core;

namespace Whitebox.Services;

public sealed class FileItem : ObservableObject
{
    public FileItem(string path, long size, DateTime modified)
    {
        Path = path;
        Size = size;
        Modified = modified;
        Name = System.IO.Path.GetFileName(path);
        Folder = System.IO.Path.GetDirectoryName(path) ?? "";
        Kind = FileKinds.Classify(path);
    }

    public string Path { get; }
    public string Name { get; }
    public string Folder { get; }
    public long Size { get; }
    public DateTime Modified { get; }
    public string Kind { get; }
    public string Glyph => FileKinds.Glyph(Kind);
    public string ModifiedText => Modified.ToString("d MMM yyyy");

    private bool _isChecked;
    public bool IsChecked { get => _isChecked; set => Set(ref _isChecked, value); }
}

public static class FileKinds
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase);

    static FileKinds()
    {
        void Add(string kind, params string[] exts) { foreach (var e in exts) Map[e] = kind; }
        Add("Videos", ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".m4v", ".flv", ".mpg", ".mpeg", ".ts");
        Add("Music", ".mp3", ".flac", ".wav", ".aac", ".m4a", ".ogg", ".wma", ".aiff");
        Add("Pictures", ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff", ".heic", ".raw", ".cr2", ".nef", ".psd", ".webp");
        Add("Archives", ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".cab", ".zst");
        Add("Disk images", ".iso", ".img", ".vhd", ".vhdx", ".vmdk", ".vdi", ".qcow2", ".wim", ".esd");
        Add("Installers", ".exe", ".msi", ".msix", ".msixbundle", ".appx", ".appxbundle");
        Add("Documents", ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".csv", ".epub");
    }

    public static string Classify(string path) => Map.TryGetValue(System.IO.Path.GetExtension(path), out var k) ? k : "Other";

    public static string Glyph(string kind) => kind switch
    {
        "Videos" => "",
        "Music" => "",
        "Pictures" => "",
        "Archives" => "",
        "Disk images" => "",
        "Installers" => "",
        "Documents" => "",
        _ => "",
    };
}

public sealed class DuplicateGroup
{
    public required long Size { get; init; }
    public required List<FileItem> Files { get; init; }
    public long Wasted => Size * (Files.Count - 1);
    public string Title => Files[0].Name;
    public string Summary => $"{Files.Count} copies  ·  {Fmt.Bytes(Size)} each  ·  {Fmt.Bytes(Wasted)} wasted";
}

public static class FileScanner
{
    private static readonly string WindowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    private static bool ShouldSkipDirectory(ref FileSystemEntry e)
    {
        if ((e.Attributes & FileAttributes.ReparsePoint) != 0) return true;
        var name = e.FileName;
        if (name.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("WinSxS", StringComparison.OrdinalIgnoreCase))
            return true;
        return e.ToFullPath().Equals(WindowsDir, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Enumerates all files under root with size ≥ minSize, walking top-level folders in parallel.</summary>
    public static List<(string Path, long Size, DateTime Modified)> Enumerate(string root, long minSize, CancellationToken ct, Action<long>? onProgress = null)
    {
        var bag = new ConcurrentBag<(string, long, DateTime)>();
        long visited = 0;

        IEnumerable<(string, long, DateTime)> Walk(string dir, bool recurse)
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = recurse,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.System | FileAttributes.Offline,
            };
            return new FileSystemEnumerable<(string, long, DateTime)>(dir,
                (ref FileSystemEntry e) => (e.ToFullPath(), e.Length, e.LastWriteTimeUtc.LocalDateTime), options)
            {
                ShouldRecursePredicate = (ref FileSystemEntry e) => !ShouldSkipDirectory(ref e),
                ShouldIncludePredicate = (ref FileSystemEntry e) =>
                {
                    if (e.IsDirectory) return false;
                    long n = Interlocked.Increment(ref visited);
                    if ((n & 4095) == 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        onProgress?.Invoke(n);
                    }
                    return e.Length >= minSize && (e.Attributes & (FileAttributes)0x400000) == 0; // skip cloud-only placeholders
                },
            };
        }

        void Collect(IEnumerable<(string, long, DateTime)> source)
        {
            try { foreach (var f in source) bag.Add(f); }
            catch (OperationCanceledException) { throw; }
            catch { }
        }

        Collect(Walk(root, false));

        List<string> subdirs;
        try
        {
            var dirOptions = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 };
            subdirs = new FileSystemEnumerable<string>(root, (ref FileSystemEntry e) => e.ToFullPath(), dirOptions)
            {
                ShouldIncludePredicate = (ref FileSystemEntry e) => e.IsDirectory && !ShouldSkipDirectory(ref e),
            }.ToList();
        }
        catch
        {
            subdirs = [];
        }

        Parallel.ForEach(subdirs, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount },
            d => Collect(Walk(d, true)));

        onProgress?.Invoke(visited);
        return bag.ToList();
    }

    public static List<FileItem> FindLarge(string root, long minSize, CancellationToken ct, Action<long>? onProgress = null) =>
        Enumerate(root, minSize, ct, onProgress)
            .OrderByDescending(f => f.Size)
            .Take(5000)
            .Select(f => new FileItem(f.Path, f.Size, f.Modified))
            .ToList();

    /// <summary>
    /// Finds duplicate files: group by size, then by a hash of the first/last 64 KB, then by full SHA-256.
    /// </summary>
    public static List<DuplicateGroup> FindDuplicates(string root, long minSize, CancellationToken ct,
        Action<string>? onStage = null, Action<long>? onProgress = null)
    {
        onStage?.Invoke("Indexing files…");
        var files = Enumerate(root, Math.Max(1, minSize), ct, onProgress);

        var bySize = files.GroupBy(f => f.Size).Where(g => g.Count() > 1).ToList();
        onStage?.Invoke($"Comparing {bySize.Sum(g => g.Count()):N0} candidates…");

        var partial = new ConcurrentBag<(string Key, (string Path, long Size, DateTime Modified) File)>();
        Parallel.ForEach(bySize.SelectMany(g => g), new ParallelOptions { CancellationToken = ct }, f =>
        {
            var h = PartialHash(f.Path, f.Size);
            if (h != null) partial.Add(($"{f.Size}:{h}", f));
        });

        var candidates = partial.GroupBy(p => p.Key).Where(g => g.Count() > 1).ToList();
        onStage?.Invoke("Verifying matches…");

        var full = new ConcurrentBag<(string Key, (string Path, long Size, DateTime Modified) File)>();
        Parallel.ForEach(candidates.SelectMany(g => g), new ParallelOptions { CancellationToken = ct }, p =>
        {
            var f = p.File;
            var h = f.Size <= 128 * 1024 ? p.Key : FullHash(f.Path);
            if (h != null) full.Add(($"{f.Size}:{h}", f));
        });

        return full.GroupBy(p => p.Key)
            .Where(g => g.Count() > 1)
            .Select(g => new DuplicateGroup
            {
                Size = g.First().File.Size,
                Files = g.Select(x => new FileItem(x.File.Path, x.File.Size, x.File.Modified))
                    .OrderBy(x => x.Modified).ToList(),
            })
            .OrderByDescending(g => g.Wasted)
            .Take(1000)
            .ToList();
    }

    private static string? PartialHash(string path, long size)
    {
        try
        {
            const int chunk = 64 * 1024;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.None);
            var buffer = new byte[Math.Min(size, chunk * 2)];
            int read = ReadFully(fs, buffer, 0, (int)Math.Min(size, chunk));
            if (size > chunk)
            {
                fs.Seek(Math.Max(chunk, size - chunk), SeekOrigin.Begin);
                read += ReadFully(fs, buffer, read, buffer.Length - read);
            }
            return Convert.ToHexString(SHA256.HashData(buffer.AsSpan(0, read)));
        }
        catch
        {
            return null;
        }
    }

    private static int ReadFully(Stream s, byte[] buffer, int offset, int count)
    {
        int total = 0;
        while (total < count)
        {
            int n = s.Read(buffer, offset + total, count - total);
            if (n == 0) break;
            total += n;
        }
        return total;
    }

    private static string? FullHash(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20, FileOptions.SequentialScan);
            return Convert.ToHexString(SHA256.HashData(fs));
        }
        catch
        {
            return null;
        }
    }
}
