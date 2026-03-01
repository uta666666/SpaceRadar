using System.Collections.ObjectModel;
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

    public ObservableCollection<FolderItem> DisplayChildren { get; } = [];

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
        if (path is null) return;

        _navigationStack.Clear();
        await ScanFolderAsync(path);
    }

    [RelayCommand(CanExecute = nameof(CanNavigateUp))]
    private void NavigateUp()
    {
        if (_navigationStack.TryPop(out var parent))
            _ = LoadFolderAsync(parent);
    }

    public async Task DrillDownAsync(FolderItem item)
    {
        if (!item.IsDirectory) return;

        if (_currentFolder != null)
            _navigationStack.Push(_currentFolder);

        await LoadFolderAsync(item);
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
            await LoadFolderAsync(root);
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
            DisplayChildren.Add(d);

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
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
