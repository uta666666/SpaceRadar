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

    // --- Properties ---
    public ReactivePropertySlim<FolderItem?> CurrentFolder { get; } = new();
    public ObservableCollection<FolderItem> DisplayChildren { get; } = new();
    public ReactivePropertySlim<bool> IsScanning { get; } = new(false);
    public ReactivePropertySlim<string> StatusText { get; } = new("フォルダーを選択してください");
    public ReactivePropertySlim<string> BreadcrumbPath { get; } = new(string.Empty);
    public ReactivePropertySlim<string> TotalSizeText { get; } = new(string.Empty);
    public ReactivePropertySlim<bool> CanNavigateUp { get; } = new(false);

    // --- Commands ---
    public AsyncReactiveCommand SelectFolderCommand { get; }
    public ReactiveCommand NavigateUpCommand { get; }
    public ReactiveCommand<FolderItem?> OpenInExplorerCommand { get; }

    public MainViewModel()
    {
        SelectFolderCommand = new AsyncReactiveCommand()
            .WithSubscribe(SelectFolderAsync);

        NavigateUpCommand = CanNavigateUp
            .ToReactiveCommand()
            .WithSubscribe(NavigateUp);

        OpenInExplorerCommand = new ReactiveCommand<FolderItem?>()
            .WithSubscribe(OpenInExplorer);

        _scanService.ScanProgressChanged += path =>
        {
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                StatusText.Value = $"スキャン中: {path}");
        };
    }

    private async Task SelectFolderAsync()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "スキャンするフォルダーを選択してください",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            return;

        _navigationStack.Clear();
        await ScanFolderAsync(dialog.SelectedPath);
    }

    public async Task DrillDownAsync(FolderItem item)
    {
        if (!item.IsDirectory) return;

        if (CurrentFolder.Value != null)
            _navigationStack.Push(CurrentFolder.Value);

        // 既にスキャン済みの子フォルダーにドリルダウン
        await LoadFolderAsync(item);
    }

    private void NavigateUp()
    {
        if (_navigationStack.TryPop(out var parent))
            _ = LoadFolderAsync(parent);
    }

    private void OpenInExplorer(FolderItem? item)
    {
        if (item == null) return;

        var path = Directory.Exists(item.Path)
            ? item.Path
            : Path.GetDirectoryName(item.Path);

        if (path == null || !Directory.Exists(path)) return;

        Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
    }

    private async Task ScanFolderAsync(string path)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        IsScanning.Value = true;
        StatusText.Value = "スキャン開始...";
        DisplayChildren.Clear();
        TotalSizeText.Value = string.Empty;
        BreadcrumbPath.Value = path;

        try
        {
            var root = await _scanService.ScanAsync(path, _cts.Token);
            await LoadFolderAsync(root);
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

        // ディレクトリのみ抽出し、サイズ降順でソート
        var dirs = folder.Children
            .Where(c => c.IsDirectory)
            .OrderByDescending(c => c.Size)
            .ToList();

        // 小さいフォルダーをまとめる（上位10件以外）
        const int maxSlices = 10;
        var top = dirs.Take(maxSlices).ToList();
        var rest = dirs.Skip(maxSlices).ToList();

        foreach (var d in top)
            DisplayChildren.Add(d);

        // ファイルサイズ（直下ファイルの合計）
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

        if (rest.Any())
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

        StatusText.Value = $"完了 — {folder.Children.Count} アイテム";
        return Task.CompletedTask;
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
        CanNavigateUp.Dispose();
        SelectFolderCommand.Dispose();
        NavigateUpCommand.Dispose();
        OpenInExplorerCommand.Dispose();
    }
}
