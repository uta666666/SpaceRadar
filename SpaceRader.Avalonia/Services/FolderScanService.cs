using SpaceRader.Avalonia.Models;

namespace SpaceRader.Avalonia.Services;

public class FolderScanService
{
    public event Action<string>? ScanProgressChanged;

    public Task<FolderItem> ScanAsync(string path, CancellationToken cancellationToken = default)
        => Task.Run(() => ScanFolder(path, null, cancellationToken), cancellationToken);

    private FolderItem ScanFolder(string path, FolderItem? parent, CancellationToken cancellationToken)
    {
        var item = new FolderItem
        {
            Name = System.IO.Path.GetFileName(path) is { Length: > 0 } n ? n : path,
            Path = path,
            IsDirectory = true,
            Parent = parent
        };

        ScanProgressChanged?.Invoke(path);

        try
        {
            var dirInfo = new System.IO.DirectoryInfo(path);
            foreach (var entry in dirInfo.EnumerateFileSystemInfos("*", System.IO.SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (entry is System.IO.DirectoryInfo subDir)
                {
                    try
                    {
                        var child = ScanFolder(subDir.FullName, item, cancellationToken);
                        item.Children.Add(child);
                        item.Size += child.Size;
                    }
                    catch (UnauthorizedAccessException) { }
                    catch (OperationCanceledException) { throw; }
                    catch { }
                }
                else if (entry is System.IO.FileInfo fileInfo)
                {
                    long fileSize = fileInfo.Length;
                    item.Size += fileSize;
                    item.Children.Add(new FolderItem
                    {
                        Name = fileInfo.Name,
                        Path = fileInfo.FullName,
                        Size = fileSize,
                        IsDirectory = false,
                        Parent = item
                    });
                }
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (OperationCanceledException) { throw; }
        catch { }

        return item;
    }
}
