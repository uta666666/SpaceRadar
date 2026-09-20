using System.Runtime.InteropServices;
using SpaceRadar.Models;

namespace SpaceRadar.Services;

public class FolderScanService
{
    public readonly record struct TopFileEntry(string Name, string Path, long Size);

    #region Windows API P/Invoke

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindFirstFileW(string lpFileName, out WIN32_FIND_DATA lpFindFileData);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool FindNextFileW(IntPtr hFindFile, out WIN32_FIND_DATA lpFindFileData);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FindClose(IntPtr hFindFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetCompressedFileSizeW(string lpFileName, out uint lpFileSizeHigh);

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

    public Task LoadChildrenAsync(FolderItem folder, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => PopulateFolder(folder, cancellationToken), cancellationToken);
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

        PopulateFolder(item, cancellationToken);

        return item;
    }

    private void PopulateFolder(FolderItem folder, CancellationToken cancellationToken)
    {
        if (!folder.IsDirectory)
        {
            return;
        }

        folder.Children.Clear();
        folder.DirectFileSize = 0;
        folder.Size = 0;

        ScanProgressChanged?.Invoke(folder.Path);

        string searchPath = System.IO.Path.Combine(folder.Path, "*");
        IntPtr handle = FindFirstFileW(searchPath, out var findData);

        if (handle == INVALID_HANDLE_VALUE)
        {
            folder.IsLoaded = true;
            return;
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

                bool isDir = (findData.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;

                if (isDir)
                {
                    try
                    {
                        string fullPath = System.IO.Path.Combine(folder.Path, name);
                        long childSize = CalculateDirectorySize(fullPath, cancellationToken);
                        folder.Children.Add(new FolderItem
                        {
                            Name = name,
                            Path = fullPath,
                            Size = childSize,
                            IsDirectory = true,
                            Parent = folder,
                            IsLoaded = false
                        });
                        folder.Size += childSize;
                    }
                    catch (UnauthorizedAccessException) { }
                    catch (OperationCanceledException) { throw; }
                    catch { }
                }
                else
                {
                    string filePath = System.IO.Path.Combine(folder.Path, name);
                    long logicalSize = ((long)findData.nFileSizeHigh << 32) | findData.nFileSizeLow;
                    long fileSize = GetSizeOnDisk(filePath, logicalSize);
                    folder.Size += fileSize;
                    folder.DirectFileSize += fileSize;
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

        folder.IsLoaded = true;
    }

    private long CalculateDirectorySize(string path, CancellationToken cancellationToken)
    {
        long totalSize = 0;
        var directories = new Stack<string>();
        directories.Push(path);

        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string currentPath = directories.Pop();
            ScanProgressChanged?.Invoke(currentPath);

            string searchPath = System.IO.Path.Combine(currentPath, "*");
            IntPtr handle = FindFirstFileW(searchPath, out var findData);
            if (handle == INVALID_HANDLE_VALUE)
            {
                continue;
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

                    bool isDir = (findData.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
                    string fullPath = System.IO.Path.Combine(currentPath, name);
                    if (isDir)
                    {
                        directories.Push(fullPath);
                    }
                    else
                    {
                        long logicalSize = ((long)findData.nFileSizeHigh << 32) | findData.nFileSizeLow;
                        totalSize += GetSizeOnDisk(fullPath, logicalSize);
                    }
                }
                while (FindNextFileW(handle, out findData));
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
            }
            finally
            {
                FindClose(handle);
            }
        }

        return totalSize;
    }

    public IReadOnlyList<TopFileEntry> GetTopNFiles(string path, int topN, CancellationToken cancellationToken = default)
    {
        if (topN <= 0)
        {
            return [];
        }

        var topFiles = new PriorityQueue<TopFileEntry, long>();
        var directories = new Stack<string>();
        directories.Push(path);

        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentPath = directories.Pop();
            ScanProgressChanged?.Invoke(currentPath);

            string searchPath = System.IO.Path.Combine(currentPath, "*");
            IntPtr handle = FindFirstFileW(searchPath, out var findData);

            if (handle == INVALID_HANDLE_VALUE)
            {
                continue;
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

                    bool isDir = (findData.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
                    if (isDir)
                    {
                        string dirPath = System.IO.Path.Combine(currentPath, name);
                        directories.Push(dirPath);
                        continue;
                    }

                    string filePath = System.IO.Path.Combine(currentPath, name);
                    long logicalSize = ((long)findData.nFileSizeHigh << 32) | findData.nFileSizeLow;
                    long fileSize = GetSizeOnDisk(filePath, logicalSize);
                    var entry = new TopFileEntry(name, filePath, fileSize);

                    if (topFiles.Count < topN)
                    {
                        topFiles.Enqueue(entry, fileSize);
                    }
                    else
                    {
                        topFiles.TryPeek(out _, out long minSize);
                        if (fileSize > minSize)
                        {
                            topFiles.Dequeue();
                            topFiles.Enqueue(entry, fileSize);
                        }
                    }
                }
                while (FindNextFileW(handle, out findData));
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
            }
            finally
            {
                FindClose(handle);
            }
        }

        return topFiles.UnorderedItems
            .Select(x => x.Element)
            .OrderByDescending(x => x.Size)
            .ToList();
    }

    private static long GetSizeOnDisk(string filePath, long fallbackLogicalSize)
    {
        uint fileSizeLow = GetCompressedFileSizeW(filePath, out uint fileSizeHigh);
        int error = Marshal.GetLastWin32Error();

        if (fileSizeLow == 0xFFFFFFFF && error != 0)
        {
            return fallbackLogicalSize;
        }

        return ((long)fileSizeHigh << 32) | fileSizeLow;
    }
}
