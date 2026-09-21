using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using SpaceRadar.Models;
using SpaceRadar.Services;
using SpaceRadar.Utilities;

namespace SpaceRadar.ViewModels;

public class MainViewModel : IDisposable
{
    private readonly FolderScanService _scanService = new();
    private readonly Stack<FolderItem> _navigationStack = new();
    private CancellationTokenSource? _cts;
    private FolderItem? _rootFolder;
    private DateTime _lastProgressUpdateUtc = DateTime.MinValue;

    // --- Properties ---
    public ReactivePropertySlim<FolderItem?> CurrentFolder { get; } = new();
    public ObservableCollection<FolderItem> DisplayChildren { get; } = new();
    public ReactivePropertySlim<bool> IsScanning { get; } = new(false);
    public ReactivePropertySlim<string> StatusText { get; } = new("フォルダーを選択してください");
    public ReactivePropertySlim<string> BreadcrumbPath { get; } = new(string.Empty);
    public ReactivePropertySlim<string> TotalSizeText { get; } = new(string.Empty);
    public ReactivePropertySlim<string> ListTitleText { get; } = new("フォルダー一覧");
    public ReactivePropertySlim<bool> CanNavigateUp { get; } = new(false);
    public ReactivePropertySlim<bool> IsDragOver { get; } = new(false);
    public ReactivePropertySlim<bool> IsTopNVisible { get; } = new(false);
    public ReactivePropertySlim<bool> TopNCurrentFolderOnly { get; } = new(false);
    public ReactivePropertySlim<int> TopNCount { get; } = new(10);
    public ObservableCollection<TopNFileItem> TopNFiles { get; } = new();

    // --- Commands ---
    public AsyncReactiveCommand SelectFolderCommand { get; }
    public ReactiveCommand NavigateUpCommand { get; }
    public ReactiveCommand<FolderItem?> OpenInExplorerCommand { get; }
    public ReactiveCommand ToggleTopNCommand { get; }
    public ReactiveCommand ToggleTopNScopeCommand { get; }
    public ReactiveCommand<string> SetTopNCountCommand { get; }
    public ReactiveCommand<TopNFileItem?> OpenTopNInExplorerCommand { get; }

    public MainViewModel()
    {
        SelectFolderCommand = new AsyncReactiveCommand()
            .WithSubscribe(SelectFolderAsync);

        NavigateUpCommand = CanNavigateUp
            .ToReactiveCommand()
            .WithSubscribe(NavigateUp);

        OpenInExplorerCommand = new ReactiveCommand<FolderItem?>()
            .WithSubscribe(OpenInExplorer);

        ToggleTopNCommand = new ReactiveCommand()
            .WithSubscribe(ToggleTopN);

        ToggleTopNScopeCommand = new ReactiveCommand().WithSubscribe(() =>
        {
            TopNCurrentFolderOnly.Value = !TopNCurrentFolderOnly.Value;
            if (IsTopNVisible.Value)
            {
                BuildTopNFiles();
            }
        });

        SetTopNCountCommand = new ReactiveCommand<string>().WithSubscribe(s =>
        {
            if (int.TryParse(s, out var count))
            {
                TopNCount.Value = count;
                BuildTopNFiles();
            }
        });

        OpenTopNInExplorerCommand = new ReactiveCommand<TopNFileItem?>().WithSubscribe(item =>
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
        });

        _scanService.ScanProgressChanged += path =>
        {
            var now = DateTime.UtcNow;
            if ((now - _lastProgressUpdateUtc).TotalMilliseconds < 100)
            {
                return;
            }

            _lastProgressUpdateUtc = now;
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                StatusText.Value = $"スキャン中: {path}";
            });
        };
    }

    private async Task SelectFolderAsync()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "スキャンするフォルダーを選択してください",
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _navigationStack.Clear();
        await ScanFolderAsync(dialog.FolderName);
    }

    public async Task ScanDroppedFolderAsync(string path)
    {
        _navigationStack.Clear();
        await ScanFolderAsync(path);
    }

    public async Task DrillDownAsync(FolderItem item)
    {
        if (!item.IsDirectory)
        {
            return;
        }

        await EnsureFolderLoadedAsync(item);

        if (CurrentFolder.Value != null)
        {
            _navigationStack.Push(CurrentFolder.Value);
        }

        await LoadFolderAsync(item);
    }

    public async Task ExpandFilesAsync(FolderItem item)
    {
        if (item.IsDirectory || item.Name != "[ファイル]")
        {
            return;
        }

        if (CurrentFolder.Value == null)
        {
            return;
        }

        _navigationStack.Push(CurrentFolder.Value);
        await LoadFileAsync(CurrentFolder.Value);
    }

    private async Task EnsureFolderLoadedAsync(FolderItem folder)
    {
        if (folder.IsLoaded)
        {
            return;
        }

        if (IsScanning.Value)
        {
            return;
        }

        IsScanning.Value = true;
        StatusText.Value = "詳細を読み込み中...";
        _lastProgressUpdateUtc = DateTime.MinValue;

        try
        {
            await _scanService.LoadChildrenAsync(folder, _cts?.Token ?? CancellationToken.None);
        }
        finally
        {
            IsScanning.Value = false;
        }
    }

    private void NavigateUp()
    {
        if (_navigationStack.TryPop(out var parent))
        {
            _ = LoadFolderAsync(parent);
        }
    }

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

    private async Task ScanFolderAsync(string path)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        if (IsScanning.Value)
        {
            return;
        }

        IsScanning.Value = true;
        StatusText.Value = "スキャン開始...";
        _lastProgressUpdateUtc = DateTime.MinValue;
        DisplayChildren.Clear();
        TotalSizeText.Value = string.Empty;
        BreadcrumbPath.Value = path;

        try
        {
            var root = await _scanService.ScanAsync(path, _cts.Token);
            _rootFolder = root;
            await LoadFolderAsync(root);
            if (IsTopNVisible.Value)
            {
                BuildTopNFiles();
            }
        }
        catch (OperationCanceledException)
        {
            StatusText.Value = "スキャンをキャンセルしました";
        }
        catch (Exception ex)
        {
            StatusText.Value = $"エラー: {ex.Message}";
        }
        finally
        {
            IsScanning.Value = false;
        }
    }

    private Task LoadFolderAsync(FolderItem folder)
    {
        CurrentFolder.Value = folder;
        BreadcrumbPath.Value = folder.Path;
        TotalSizeText.Value = FileSizeFormatter.FormatSize(folder.Size);
        CanNavigateUp.Value = _navigationStack.Count > 0;

        DisplayChildren.Clear();
        ListTitleText.Value = "フォルダー一覧";

        bool isVirtualOtherFolder = folder.IsVirtualOtherFolder;

        // ディレクトリのみ抽出し、サイズ降順でソート
        var dirs = folder.Children
            .Where(c => c.IsDirectory)
            .OrderByDescending(c => c.Size)
            .ToList();

        if (isVirtualOtherFolder)
        {
            foreach (var d in dirs)
            {
                DisplayChildren.Add(d);
            }

            StatusText.Value = $"完了 — {folder.Children.Count} アイテム";

            if (IsTopNVisible.Value && TopNCurrentFolderOnly.Value)
            {
                BuildTopNFiles();
            }

            return Task.CompletedTask;
        }

        // 小さいフォルダーをまとめる（上位10件以外）
        const int maxSlices = 10;
        var top = dirs.Take(maxSlices).ToList();
        var rest = dirs.Skip(maxSlices).ToList();

        foreach (var d in top)
        {
            DisplayChildren.Add(d);
        }

        // ファイルサイズ（直下ファイルの合計）
        long directFileSize = folder.DirectFileSize;

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

        if (rest.Any())
        {
            long otherSize = rest.Sum(c => c.Size);
            DisplayChildren.Add(new FolderItem
            {
                Name = "[その他]",
                Path = $"{folder.Path.TrimEnd('\\')}\\[その他]",
                Size = otherSize,
                IsDirectory = true,
                IsLoaded = true,
                IsVirtualOtherFolder = true,
                Children = rest,
                Parent = folder
            });
        }

        StatusText.Value = $"完了 — {folder.Children.Count} アイテム";

        if (IsTopNVisible.Value && TopNCurrentFolderOnly.Value)
        {
            BuildTopNFiles();
        }

        return Task.CompletedTask;
    }

    private Task LoadFileAsync(FolderItem folder)
    {
        CurrentFolder.Value = folder;
        BreadcrumbPath.Value = $"{folder.Path.TrimEnd('\\')}\\[ファイル]";
        CanNavigateUp.Value = _navigationStack.Count > 0;

        DisplayChildren.Clear();
        ListTitleText.Value = "ファイル一覧";

        // ファイルのみ抽出し、サイズ降順でソート
        var files = Directory.EnumerateFiles(folder.Path)
            .Select(path =>
            {
                var info = new FileInfo(path);
                return new FolderItem
                {
                    Name = Path.GetFileName(path),
                    Path = path,
                    Size = info.Length,
                    IsDirectory = false,
                    Parent = folder
                };
            })
            .OrderByDescending(c => c.Size)
            .ToList();

        foreach (var file in files)
        {
            DisplayChildren.Add(file);
        }

        TotalSizeText.Value = FileSizeFormatter.FormatSize(files.Sum(f => f.Size));
        StatusText.Value = $"完了 — {files.Count} アイテム";

        if (IsTopNVisible.Value && TopNCurrentFolderOnly.Value)
        {
            BuildTopNFiles();
        }

        return Task.CompletedTask;
    }

    private void ToggleTopN()
    {
        IsTopNVisible.Value = !IsTopNVisible.Value;
        if (IsTopNVisible.Value)
        {
            BuildTopNFiles();
        }
    }

    private void BuildTopNFiles()
    {
        TopNFiles.Clear();
        var root = TopNCurrentFolderOnly.Value ? CurrentFolder.Value : _rootFolder;
        if (root == null)
        {
            return;
        }

        var files = _scanService.GetTopNFiles(root.Path, TopNCount.Value, _cts?.Token ?? CancellationToken.None);

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
        CurrentFolder.Dispose();
        IsScanning.Dispose();
        StatusText.Dispose();
        BreadcrumbPath.Dispose();
        TotalSizeText.Dispose();
        ListTitleText.Dispose();
        CanNavigateUp.Dispose();
        IsDragOver.Dispose();
        IsTopNVisible.Dispose();
        TopNCurrentFolderOnly.Dispose();
        TopNCount.Dispose();
        SelectFolderCommand.Dispose();
        NavigateUpCommand.Dispose();
        OpenInExplorerCommand.Dispose();
        ToggleTopNCommand.Dispose();
        ToggleTopNScopeCommand.Dispose();
        SetTopNCountCommand.Dispose();
        OpenTopNInExplorerCommand.Dispose();
    }
}
