using System.Runtime.InteropServices;
using SpaceRadar.Models;

namespace SpaceRadar.Services;

public class FolderScanService
{
    #region Windows API P/Invoke

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindFirstFileW(string lpFileName, out WIN32_FIND_DATA lpFindFileData);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool FindNextFileW(IntPtr hFindFile, out WIN32_FIND_DATA lpFindFileData);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FindClose(IntPtr hFindFile);

    private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WIN32_FIND_DATA
    {
        public uint dwFileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint dwReserved0;
        public uint dwReserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string cFileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string cAlternateFileName;
    }

    #endregion

    public event Action<string>? ScanProgressChanged;

    public Task<FolderItem> ScanAsync(string path, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ScanFolder(path, null, cancellationToken), cancellationToken);
    }

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

        string searchPath = System.IO.Path.Combine(path, "*");
        IntPtr handle = FindFirstFileW(searchPath, out var findData);

        if (handle == INVALID_HANDLE_VALUE)
        {
            return item;
        }

        try
        {
            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                string name = findData.cFileName;
                if (name == "." || name == "..")
                {
                    continue;
                }

                string fullPath = System.IO.Path.Combine(path, name);
                bool isDir = (findData.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;

                if (isDir)
                {
                    try
                    {
                        var child = ScanFolder(fullPath, item, cancellationToken);
                        item.Children.Add(child);
                        item.Size += child.Size;
                    }
                    catch (UnauthorizedAccessException) { }
                    catch (OperationCanceledException) { throw; }
                    catch { }
                }
                else
                {
                    long fileSize = ((long)findData.nFileSizeHigh << 32) | findData.nFileSizeLow;
                    item.Size += fileSize;
                    item.Children.Add(new FolderItem
                    {
                        Name = name,
                        Path = fullPath,
                        Size = fileSize,
                        IsDirectory = false,
                        Parent = item
                    });
                }
            }
            while (FindNextFileW(handle, out findData));
        }
        catch (OperationCanceledException) { throw; }
        catch { }
        finally
        {
            FindClose(handle);
        }

        return item;
    }
}
