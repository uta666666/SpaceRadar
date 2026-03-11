using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpaceRader.Avalonia.Models;
using SpaceRader.Avalonia.Services;
using SpaceRader.Avalonia.Utilities;

namespace SpaceRader.Avalonia.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly FolderScanService _scanService = new();
    private readonly Stack<FolderItem> _navigationStack = new();
    private readonly Func<Task<string?>> _pickFolderAsync;
    private CancellationTokenSource? _cts;
    private FolderItem? _currentFolder;
    private FolderItem? _rootFolder;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NavigateUpCommand))]
    private bool _canNavigateUp;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _statusText = "フォルダーを選択してください";

    [ObservableProperty]
    private string _breadcrumbPath = string.Empty;

    [ObservableProperty]
    private string _totalSizeText = string.Empty;

    [ObservableProperty]
    private bool _isDragOver;

    [ObservableProperty]
    private bool _isTopNVisible;

    [ObservableProperty]
    private bool _topNCurrentFolderOnly;

    [ObservableProperty]
    private int _topNCount = 10;

    public ObservableCollection<FolderItem> DisplayChildren { get; } = [];
    public ObservableCollection<TopNFileItem> TopNFiles { get; } = [];

    public MainWindowViewModel(Func<Task<string?>> pickFolderAsync)
    {
        _pickFolderAsync = pickFolderAsync;
        _scanService.ScanProgressChanged += path =>
            Dispatcher.UIThread.Post(() => StatusText = $"スキャン中: {path}");
    }

    [RelayCommand]
    private async Task SelectFolderAsync()
    {
        var path = await _pickFolderAsync();
        if (path is null)
        {
            return;
        }

        _navigationStack.Clear();
        await ScanFolderAsync(path);
    }

    [RelayCommand(CanExecute = nameof(CanNavigateUp))]
    private void NavigateUp()
    {
        if (_navigationStack.TryPop(out var parent))
        {
            _ = LoadFolderAsync(parent);
        }
    }

    [RelayCommand]
    private void OpenInExplorer(FolderItem? item)
    {
        if (item == null)
        {
            return;
        }

        var path = Directory.Exists(item.Path)
            ? item.Path
            : Path.GetDirectoryName(item.Path);

        if (path == null || !Directory.Exists(path))
        {
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
    }

    [RelayCommand]
    private void ToggleTopN()
    {
        IsTopNVisible = !IsTopNVisible;
        if (IsTopNVisible)
        {
            BuildTopNFiles();
        }
    }

    [RelayCommand]
    private void ToggleTopNScope()
    {
        TopNCurrentFolderOnly = !TopNCurrentFolderOnly;
        if (IsTopNVisible)
        {
            BuildTopNFiles();
        }
    }

    [RelayCommand]
    private void SetTopNCount(string s)
    {
        if (int.TryParse(s, out var count))
        {
            TopNCount = count;
            BuildTopNFiles();
        }
    }

    [RelayCommand]
    private void OpenTopNInExplorer(TopNFileItem? item)
    {
        if (item == null)
        {
            return;
        }

        var dir = Path.GetDirectoryName(item.Path);
        if (dir == null || !Directory.Exists(dir))
        {
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
    }

    public async Task DrillDownAsync(FolderItem item)
    {
        if (!item.IsDirectory)
        {
            return;
        }

        if (_currentFolder != null)
        {
            _navigationStack.Push(_currentFolder);
        }

        await LoadFolderAsync(item);
    }

    public async Task ScanDroppedFolderAsync(string path)
    {
        _navigationStack.Clear();
        await ScanFolderAsync(path);
    }

    private async Task ScanFolderAsync(string path)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        IsScanning = true;
        StatusText = "スキャン開始...";
        DisplayChildren.Clear();
        TotalSizeText = string.Empty;
        BreadcrumbPath = path;

        try
        {
            var root = await _scanService.ScanAsync(path, _cts.Token);
            _rootFolder = root;
            await LoadFolderAsync(root);
            if (IsTopNVisible)
            {
                BuildTopNFiles();
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "スキャンをキャンセルしました";
        }
        catch (Exception ex)
        {
            StatusText = $"エラー: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    private Task LoadFolderAsync(FolderItem folder)
    {
        _currentFolder = folder;
        BreadcrumbPath = folder.Path;
        TotalSizeText = FileSizeFormatter.FormatSize(folder.Size);
        CanNavigateUp = _navigationStack.Count > 0;

        DisplayChildren.Clear();

        var dirs = folder.Children
            .Where(c => c.IsDirectory)
            .OrderByDescending(c => c.Size)
            .ToList();

        const int maxSlices = 10;
        var top = dirs.Take(maxSlices).ToList();
        var rest = dirs.Skip(maxSlices).ToList();

        foreach (var d in top)
        {
            DisplayChildren.Add(d);
        }

        long directFileSize = folder.Children
            .Where(c => !c.IsDirectory)
            .Sum(c => c.Size);

        if (directFileSize > 0)
        {
            DisplayChildren.Add(new FolderItem
            {
                Name = "[ファイル]",
                Path = folder.Path,
                Size = directFileSize,
                IsDirectory = false
            });
        }

        if (rest.Count > 0)
        {
            long otherSize = rest.Sum(c => c.Size);
            DisplayChildren.Add(new FolderItem
            {
                Name = "その他",
                Path = folder.Path,
                Size = otherSize,
                IsDirectory = false
            });
        }

        StatusText = $"完了 — {folder.Children.Count} アイテム";

        if (IsTopNVisible && TopNCurrentFolderOnly)
        {
            BuildTopNFiles();
        }

        return Task.CompletedTask;
    }

    private static IEnumerable<FolderItem> FlattenFiles(FolderItem folder)
    {
        foreach (var child in folder.Children)
        {
            if (!child.IsDirectory)
            {
                yield return child;
            }
            else
            {
                foreach (var f in FlattenFiles(child))
                {
                    yield return f;
                }
            }
        }
    }

    private void BuildTopNFiles()
    {
        TopNFiles.Clear();
        var root = TopNCurrentFolderOnly ? _currentFolder : _rootFolder;
        if (root == null)
        {
            return;
        }

        var files = FlattenFiles(root)
            .OrderByDescending(f => f.Size)
            .Take(TopNCount)
            .ToList();

        long maxSize = files.Count > 0 ? files[0].Size : 1;

        for (int i = 0; i < files.Count; i++)
        {
            TopNFiles.Add(new TopNFileItem
            {
                Rank = i + 1,
                Name = files[i].Name,
                Path = files[i].Path,
                Size = files[i].Size,
                SizeText = FileSizeFormatter.FormatSize(files[i].Size),
                BarWidthRatio = maxSize > 0 ? (double)files[i].Size / maxSize : 0
            });
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
