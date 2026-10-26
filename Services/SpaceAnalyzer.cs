using System.IO.Enumeration;
using Whitebox.Controls;

namespace Whitebox.Services;

public sealed class DirNode
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public DirNode? Parent { get; init; }
    public long Size;
    public long FileBytes;
    public int FileCount;
    public List<DirNode> Children { get; } = [];
}

/// <summary>Builds a directory size tree using parallel enumeration.</summary>
public static class SpaceAnalyzer
{
    private const FileAttributes CloudOnly = (FileAttributes)0x400000; // FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS

    private sealed class ScanProgress
    {
        public long Files;
        public long LastReport;
        public Action<long>? Callback;
    }

    public static DirNode Scan(string root, CancellationToken ct, Action<long>? onProgress = null)
    {
        var node = new DirNode { Name = root, Path = root };
        var progress = new ScanProgress { Callback = onProgress };
        ScanDir(node, 0, ct, progress);
        onProgress?.Invoke(progress.Files);
        return node;
    }

    private static void ScanDir(DirNode node, int depth, CancellationToken ct, ScanProgress progress)
    {
        ct.ThrowIfCancellationRequested();
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0, RecurseSubdirectories = false };
        long bytes = 0;
        int count = 0;
        try
        {
            var entries = new FileSystemEnumerable<(bool IsDir, string Name, string Path, long Length, FileAttributes Attr)>(node.Path,
                (ref FileSystemEntry e) => (e.IsDirectory, e.FileName.ToString(), e.IsDirectory ? e.ToFullPath() : "", e.Length, e.Attributes),
                options);
            foreach (var e in entries)
            {
                if (e.IsDir)
                {
                    if ((e.Attr & FileAttributes.ReparsePoint) != 0) continue;
                    node.Children.Add(new DirNode { Name = e.Name, Path = e.Path, Parent = node });
                }
                else
                {
                    count++;
                    if ((e.Attr & (CloudOnly | FileAttributes.Offline)) == 0) bytes += e.Length;
                }
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }

        node.FileBytes = bytes;
        node.FileCount = count;

        long total = Interlocked.Add(ref progress.Files, count);
        if (progress.Callback != null && total - Interlocked.Read(ref progress.LastReport) > 2500)
        {
            Interlocked.Exchange(ref progress.LastReport, total);
            progress.Callback(total);
        }

        if (depth < 3 && node.Children.Count > 1)
            Parallel.ForEach(node.Children, new ParallelOptions { CancellationToken = ct }, child => ScanDir(child, depth + 1, ct, progress));
        else
            foreach (var child in node.Children) ScanDir(child, depth + 1, ct, progress);

        node.Size = node.FileBytes + node.Children.Sum(c => c.Size);
        node.Children.Sort((a, b) => b.Size.CompareTo(a.Size));
    }

    /// <summary>Items to display for a node: its subfolders plus its largest loose files.</summary>
    public static List<TreeMapItem> ItemsFor(DirNode node, int maxItems = 120)
    {
        var items = node.Children.Where(c => c.Size > 0)
            .Select(c => new TreeMapItem { Name = c.Name, Path = c.Path, Size = c.Size, IsDirectory = true })
            .ToList();

        try
        {
            var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 };
            var files = new FileSystemEnumerable<TreeMapItem>(node.Path,
                (ref FileSystemEntry e) => new TreeMapItem { Name = e.FileName.ToString(), Path = e.ToFullPath(), Size = e.Length },
                options)
            {
                ShouldIncludePredicate = (ref FileSystemEntry e) => !e.IsDirectory && (e.Attributes & CloudOnly) == 0,
            };
            items.AddRange(files);
        }
        catch { }

        items.Sort((a, b) => b.Size.CompareTo(a.Size));
        if (items.Count > maxItems)
        {
            var rest = items.Skip(maxItems).ToList();
            items = items.Take(maxItems).ToList();
            items.Add(new TreeMapItem
            {
                Name = $"{rest.Count:N0} smaller items",
                Path = node.Path,
                Size = rest.Sum(r => r.Size),
                IsAggregate = true,
            });
        }
        return items;
    }
}
