using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GitIgnoreCleaner.Helpers;
using GitIgnoreCleaner.Models;
using GitIgnoreCleaner.Services;
using GitIgnoreCleaner.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;
using Windows.Graphics;
using Windows.Storage.Pickers;

namespace GitIgnoreCleaner;

public sealed partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private readonly ScanService _scanService = new();
    private readonly DeleteService _deleteService = new();
    private CancellationTokenSource? _scanCts;
    private IntPtr _windowHandle;
    private readonly DispatcherQueue _dispatcherQueue;

    public MainWindow()
    {
        InitializeComponent();
        _windowHandle = WindowNative.GetWindowHandle(this);
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        try
        {
            SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        }
        catch
        {
        }

        if (Content is FrameworkElement root)
        {
            root.DataContext = _viewModel;
        }

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        SetDefaultWindowSize();
        Closed += (_, _) =>
        {
            _scanCts?.Cancel();
            _scanCts?.Dispose();
        };
    }

    private void SetDefaultWindowSize()
    {
        try
        {
            AppWindow.Resize(new SizeInt32(1100, 720));
        }
        catch
        {
        }
    }

    private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width >= 900)
        {
            MainAreaColumn0.Width = new GridLength(420);
            MainAreaColumn1.Width = new GridLength(1, GridUnitType.Star);
            MainAreaRow2.Height = new GridLength(1, GridUnitType.Star);

            Grid.SetColumn(ContentPanel, 1);
            Grid.SetRow(ContentPanel, 0);
            Grid.SetRowSpan(ContentPanel, 3);

            HeaderPanel.Padding = new Thickness(24, 24, 12, 12);
            ToolbarPanel.Padding = new Thickness(24, 0, 12, 12);
            ContentPanel.Padding = new Thickness(12, 24, 24, 12);

            ToolbarCol2.Width = new GridLength(0);
            ToolbarRow0.Height = GridLength.Auto;
            ToolbarRow1.Height = GridLength.Auto;

            Grid.SetRow(PermanentlyDeleteCheckBox, 1);
            Grid.SetColumn(PermanentlyDeleteCheckBox, 0);
            Grid.SetColumnSpan(PermanentlyDeleteCheckBox, 4);
            PermanentlyDeleteCheckBox.Margin = new Thickness(0, 12, 0, 0);
        }
        else
        {
            MainAreaColumn0.Width = new GridLength(1, GridUnitType.Star);
            MainAreaColumn1.Width = new GridLength(0);
            MainAreaRow2.Height = new GridLength(1, GridUnitType.Star);

            Grid.SetColumn(ContentPanel, 0);
            Grid.SetRow(ContentPanel, 2);
            Grid.SetRowSpan(ContentPanel, 1);

            HeaderPanel.Padding = new Thickness(24, 24, 24, 12);
            ToolbarPanel.Padding = new Thickness(24, 0, 24, 12);
            ContentPanel.Padding = new Thickness(24, 12, 24, 12);

            ToolbarCol2.Width = new GridLength(1, GridUnitType.Star);
            ToolbarRow0.Height = GridLength.Auto;
            ToolbarRow1.Height = new GridLength(0);

            Grid.SetRow(PermanentlyDeleteCheckBox, 0);
            Grid.SetColumn(PermanentlyDeleteCheckBox, 3);
            Grid.SetColumnSpan(PermanentlyDeleteCheckBox, 1);
            PermanentlyDeleteCheckBox.Margin = new Thickness(0);
        }
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.Desktop
        };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, _windowHandle);
        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            _viewModel.RootPath = folder.Path;
        }
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsScanning)
        {
            _scanCts?.Cancel();
            _viewModel.StatusMessage = "Canceling...";
            return;
        }

        if (_viewModel.IsBusy)
        {
            return;
        }

        var rootPath = _viewModel.RootPath.Trim();
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            await ShowMessageAsync("Select a valid root folder before scanning.");
            return;
        }

        _viewModel.IsScanning = true;
        _viewModel.IsProgressIndeterminate = true;
        _viewModel.ProgressValue = 0;
        _viewModel.ShowSuccessMessage = false;
        _viewModel.ClearResults();

        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        _viewModel.StatusMessage = "Scanning...";

        try
        {
            var ignoreFileNames = _viewModel.GetParsedIgnoreFileNames();
            if (ignoreFileNames.Count == 0)
            {
                ignoreFileNames = [".gitignore", ".ignore"];
            }

            var result = await _scanService.ScanAsync(
                rootPath,
                ignoreFileNames,
                _scanCts.Token,
                rootChildren: _viewModel.Results,
                onRootCreated: null,
                onProgress: (count) => _viewModel.StatusMessage = $"Scanning... {count} items processed",
                dispatcher: _dispatcherQueue);

            _viewModel.SummaryText = result.RootNode == null
                ? "No ignored files or directories were found."
                : $"{result.CandidateCount} items matched, {StringHelper.FormatBytes(result.TotalBytes)} total size.";
            _viewModel.ErrorSummary = BuildErrorSummary(result.Errors);
            _viewModel.StatusMessage = "Scan completed.";

            if (result.RootNode != null)
            {
                _viewModel.SuccessMessage = $"Scan completed. Found {result.CandidateCount} items totaling {StringHelper.FormatBytes(result.TotalBytes)}.";
                _viewModel.ShowSuccessMessage = true;
            }
        }
        catch (OperationCanceledException)
        {
            _viewModel.StatusMessage = "Scan canceled.";
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = "Scan failed.";
            _viewModel.ErrorSummary = ex.Message;
        }
        finally
        {
            _viewModel.IsScanning = false;
            _viewModel.IsProgressIndeterminate = false;
            _viewModel.ProgressValue = 0;
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy)
        {
            return;
        }

        var targets = _deleteService.GetDeletionTargets(_viewModel.Results);
        if (targets.Count == 0)
        {
            await ShowMessageAsync("No items are selected for deletion.");
            return;
        }

        var totalSize = targets.Sum(item => item.SizeBytes);
        var confirm = new ContentDialog
        {
            Title = "Confirm deletion",
            Content = $"Delete {targets.Count} items totaling {StringHelper.FormatBytes(totalSize)}? This action cannot be undone.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            XamlRoot = GetXamlRoot()
        };

        if (!_viewModel.PermanentlyDelete)
        {
            confirm.Content = $"Move {targets.Count} items totaling {StringHelper.FormatBytes(totalSize)} to Recycle Bin?";
        }

        var result = await confirm.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        _viewModel.IsDeleting = true;
        _viewModel.IsProgressIndeterminate = false;
        _viewModel.ProgressValue = 0;
        _viewModel.StatusMessage = "Deleting...";

        int deletedCount = 0;
        int totalCount = targets.Count;

        var progress = new Progress<ScanNode>(node =>
        {
            deletedCount++;
            _viewModel.ProgressValue = totalCount == 0 ? 0 : (double)deletedCount / totalCount * 100;
            _viewModel.StatusMessage = $"Deleting... {deletedCount}/{totalCount}";

            if (node.Parent != null)
            {
                node.Parent.RemoveChild(node);
            }
            else
            {
                _viewModel.Results.Remove(node);
            }
        });

        try
        {
            var deleteResult = await _deleteService.DeleteTargetsAsync(targets, _viewModel.PermanentlyDelete, progress);

            _viewModel.StatusMessage = deleteResult.Errors.Count == 0
                ? "Deletion completed."
                : "Deletion completed with warnings.";
            _viewModel.ErrorSummary = BuildErrorSummary(deleteResult.Errors);
        }
        finally
        {
            _viewModel.IsDeleting = false;
            _viewModel.IsProgressIndeterminate = false;
            _viewModel.ProgressValue = 0;
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsScanning)
        {
            _scanCts?.Cancel();
            _viewModel.StatusMessage = "Canceling...";
        }
        _viewModel.ClearResults();
    }

    private async Task ShowMessageAsync(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "GitIgnoreCleaner",
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = GetXamlRoot()
        };

        await dialog.ShowAsync();
    }

    private static string BuildErrorSummary(IReadOnlyList<string> errors)
    {
        if (errors.Count == 0)
        {
            return string.Empty;
        }

        if (errors.Count == 1)
        {
            return errors[0];
        }

        if (errors.Count == 2)
        {
            return string.Join(" | ", errors);
        }

        var preview = string.Join(" | ", errors.Take(2));
        return $"{preview} | {errors.Count - 2} more";
    }

    private Microsoft.UI.Xaml.XamlRoot? GetXamlRoot()
    {
        return (Content as UIElement)?.XamlRoot;
    }

    private void OpenInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is ScanNode node)
        {
            var path = node.FullPath;
            if (File.Exists(path))
            {
                path = Path.GetDirectoryName(path);
            }

            if (path != null && Directory.Exists(path))
            {
                Process.Start("explorer.exe", path);
            }
        }
    }

    private async void OpenIgnoreFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is ScanNode node)
        {
            var paths = node.IgnoreRulePaths;
            if (paths.Count == 0)
            {
                await ShowMessageAsync("No ignore file associated with this item.");
                return;
            }

            if (paths.Count == 1)
            {
                OpenFile(paths[0]);
            }
            else
            {
                var dialog = new ContentDialog
                {
                    Title = "Select Ignore File",
                    CloseButtonText = "Cancel",
                    XamlRoot = GetXamlRoot()
                };

                var stack = new StackPanel { Spacing = 8 };
                foreach (var path in paths)
                {
                    var button = new Button
                    {
                        Content = path,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Left
                    };
                    button.Click += (_, _) =>
                    {
                        OpenFile(path);
                        dialog.Hide();
                    };
                    stack.Children.Add(button);
                }
                dialog.Content = stack;
                await dialog.ShowAsync();
            }
        }
    }

    private void OpenFile(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _ = ShowMessageAsync($"Failed to open file: {ex.Message}");
        }
    }
}
