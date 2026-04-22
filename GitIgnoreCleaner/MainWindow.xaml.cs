using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GitIgnoreCleaner.Helpers;
using GitIgnoreCleaner.Models;
using GitIgnoreCleaner.Services;
using GitIgnoreCleaner.ViewModels;
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

    private ReversibleDeleteSession? _lastDeleteSession;
    private CancellationTokenSource? _scanCts;
    private IntPtr _windowHandle;
    private int _restoreLoadVersion;

    public MainWindow()
    {
        InitializeComponent();
        _windowHandle = WindowNative.GetWindowHandle(this);

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

        _viewModel.PropertyChanged += ViewModel_PropertyChanged;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        SetDefaultWindowSize();

        Closed += (_, _) =>
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
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

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.RootPath))
        {
            _ = RefreshRestoreSessionAsync(_viewModel.RootPath);
        }
    }

    private async Task RefreshRestoreSessionAsync(string rootPath)
    {
        var requestVersion = Interlocked.Increment(ref _restoreLoadVersion);
        _viewModel.ShowRestoreActionInSuccess = false;
        ApplyRestorableSession(null);

        var trimmedRoot = rootPath.Trim();
        if (_viewModel.Operation != UiOperationKind.Idle ||
            string.IsNullOrWhiteSpace(trimmedRoot) ||
            !Directory.Exists(trimmedRoot))
        {
            return;
        }

        ReversibleDeleteSession? session;
        try
        {
            session = await Task.Run(() => _deleteService.TryLoadLatestSession(trimmedRoot));
        }
        catch
        {
            return;
        }

        if (requestVersion != _restoreLoadVersion ||
            _viewModel.Operation != UiOperationKind.Idle ||
            !string.Equals(_viewModel.RootPath.Trim(), trimmedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ApplyRestorableSession(session);
    }

    private void ApplyRestorableSession(ReversibleDeleteSession? session)
    {
        _lastDeleteSession = session;
        _viewModel.HasRestorableDelete = session is { Entries.Count: > 0 };
    }

    private List<string> GetConfiguredIgnoreFileNames()
    {
        var ignoreFileNames = _viewModel.GetParsedIgnoreFileNames();
        return ignoreFileNames.Count == 0
            ? [".gitignore", ".ignore"]
            : ignoreFileNames;
    }

    private List<string> GetConfiguredExcludedFolderNames()
    {
        return _viewModel.GetParsedExcludedFolderNames();
    }

    private async Task<ScanResult> RefreshCurrentResultsAsync(string rootPath)
    {
        var result = await _scanService.ScanAsync(
            rootPath,
            GetConfiguredIgnoreFileNames(),
            GetConfiguredExcludedFolderNames(),
            CancellationToken.None,
            new Progress<int>(count => _viewModel.StatusMessage = $"Refreshing results... {count} items processed"));

        ApplyScanResults(result);
        return result;
    }

    private void ApplyScanResults(ScanResult result)
    {
        ReplaceResults(result.RootNode);
        _viewModel.SummaryText = result.PreviewPlan.Count == 0
            ? "No ignored files or directories were found."
            : $"{result.PreviewPlan.Count} items matched, {StringHelper.FormatBytes(result.PreviewPlan.TotalBytes)} total size.";
    }

    private void ReplaceResults(ScanSnapshotNode rootNode)
    {
        _viewModel.Results.Clear();

        foreach (var child in rootNode.Children)
        {
            _viewModel.Results.Add(ScanNode.FromSnapshot(child));
        }
    }

    private void ClearResultsAfterMutationRefreshFailure(string summaryText)
    {
        _viewModel.Results.Clear();
        _viewModel.SummaryText = summaryText;
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

            MainScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
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

            MainScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
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

    private async void EditExclusions_Click(object sender, RoutedEventArgs e)
    {
        var currentExclusions = _viewModel.GetParsedExcludedFolderNames();
        var text = string.Join(Environment.NewLine, currentExclusions);

        var textBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            Height = 300,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            Text = text,
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(textBox, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(textBox, ScrollBarVisibility.Auto);

        var stackPanel = new StackPanel { Spacing = 8 };
        stackPanel.Children.Add(new TextBlock
        {
            Text = "Enter folder names to skip during scanning (one per line):",
            Style = (Style)Application.Current.Resources["BodyTextBlockStyle"]
        });
        stackPanel.Children.Add(textBox);

        var dialog = new ContentDialog
        {
            Title = "Manage Scan Exclusions",
            Content = stackPanel,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = GetXamlRoot()
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var newExclusions = textBox.Text
                .Split([Environment.NewLine, "\r", "\n"], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line));

            _viewModel.ExcludedFolderNames = string.Join(";", newExclusions);
        }
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsScanning)
        {
            _scanCts?.Cancel();
            _viewModel.StatusMessage = "Canceling scan...";
            return;
        }

        if (_viewModel.Operation != UiOperationKind.Idle)
        {
            return;
        }

        var rootPath = _viewModel.RootPath.Trim();
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            await ShowMessageAsync("Select a valid root folder before scanning.");
            return;
        }

        _viewModel.ClearResults();
        _viewModel.Operation = UiOperationKind.Scanning;
        _viewModel.IsProgressIndeterminate = true;
        _viewModel.ProgressValue = 0;
        _viewModel.StatusMessage = "Scanning...";
        _viewModel.ShowRestoreActionInSuccess = false;

        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();

        try
        {
            var result = await _scanService.ScanAsync(
                rootPath,
                GetConfiguredIgnoreFileNames(),
                GetConfiguredExcludedFolderNames(),
                _scanCts.Token,
                new Progress<int>(count => _viewModel.StatusMessage = $"Scanning... {count} items processed"));

            ApplyScanResults(result);

            _viewModel.ErrorsList = result.Errors.ToList();
            _viewModel.ErrorSummary = BuildErrorSummary(_viewModel.ErrorsList);
            _viewModel.StatusMessage = result.Errors.Count == 0
                ? "Scan completed."
                : "Scan completed with warnings.";

            if (result.PreviewPlan.Count > 0)
            {
                _viewModel.SuccessMessage = $"Found {result.PreviewPlan.Count} items totaling {StringHelper.FormatBytes(result.PreviewPlan.TotalBytes)}.";
                _viewModel.ShowSuccessMessage = true;
                _viewModel.ShowRestoreActionInSuccess = false;
            }
        }
        catch (OperationCanceledException)
        {
            _viewModel.ClearResults();
            _viewModel.StatusMessage = "Scan canceled.";
            _viewModel.SummaryText = "Scan canceled before results were finalized.";
        }
        catch (Exception ex)
        {
            _viewModel.ClearResults();
            _viewModel.StatusMessage = "Scan failed.";
            _viewModel.ErrorsList = [ex.Message];
            _viewModel.ErrorSummary = BuildErrorSummary(_viewModel.ErrorsList);
        }
        finally
        {
            _viewModel.Operation = UiOperationKind.Idle;
            _viewModel.IsProgressIndeterminate = false;
            _viewModel.ProgressValue = 0;
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Operation != UiOperationKind.Idle)
        {
            return;
        }

        var rootPath = _viewModel.RootPath.Trim();
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            await ShowMessageAsync("Select a valid root folder before deleting.");
            return;
        }

        var plan = _deleteService.CreateDeletionPlan(_viewModel.Results);
        if (plan.Count == 0)
        {
            await ShowMessageAsync("No items are selected for deletion.");
            return;
        }

        var confirm = new ContentDialog
        {
            Title = _viewModel.PermanentlyDelete ? "Confirm permanent delete" : "Confirm reversible delete",
            Content = _viewModel.PermanentlyDelete
                ? $"Delete {plan.Count} items totaling {StringHelper.FormatBytes(plan.TotalBytes)} permanently? This action can't be undone."
                : $"Move {plan.Count} items totaling {StringHelper.FormatBytes(plan.TotalBytes)} into GitIgnoreCleaner's reversible trash? You can restore the last delete later.",
            PrimaryButtonText = _viewModel.PermanentlyDelete ? "Delete" : "Move to Trash",
            CloseButtonText = "Cancel",
            XamlRoot = GetXamlRoot()
        };

        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        _viewModel.Operation = UiOperationKind.Deleting;
        _viewModel.IsProgressIndeterminate = false;
        _viewModel.ProgressValue = 0;
        _viewModel.StatusMessage = "Deleting...";
        _viewModel.ShowSuccessMessage = false;
        _viewModel.ShowRestoreActionInSuccess = false;

        var deletedCount = 0;
        var totalCount = plan.Count;
        var progress = new Progress<DeletionPlanEntry>(_ =>
        {
            deletedCount++;
            _viewModel.ProgressValue = totalCount == 0 ? 0 : (double)deletedCount / totalCount * 100;
            _viewModel.StatusMessage = $"Deleting... {deletedCount}/{totalCount}";
        });

        try
        {
            var deleteResult = await _deleteService.DeleteTargetsAsync(rootPath, plan, _viewModel.PermanentlyDelete, progress);
            var deletedBytes = deleteResult.DeletedEntries.Sum(item => item.SizeBytes);
            var allErrors = deleteResult.Errors.ToList();
            ScanResult? refreshResult = null;

            if (deleteResult.DeletedEntries.Count > 0 &&
                !string.IsNullOrWhiteSpace(rootPath) &&
                Directory.Exists(rootPath))
            {
                try
                {
                    refreshResult = await RefreshCurrentResultsAsync(rootPath);
                    allErrors.AddRange(refreshResult.Errors);
                }
                catch (Exception ex)
                {
                    ClearResultsAfterMutationRefreshFailure("Items changed, but refreshing the current results failed. Run Scan again.");
                    allErrors.Add($"Deleted items, but refreshing the current results failed: {ex.Message}");
                }
            }

            _viewModel.ErrorsList = allErrors;
            _viewModel.ErrorSummary = BuildErrorSummary(_viewModel.ErrorsList);

            if (deleteResult.DeletedEntries.Count > 0)
            {
                var refreshedSuffix = refreshResult != null ? " and refreshed the results." : ".";
                _viewModel.SuccessMessage = _viewModel.PermanentlyDelete
                    ? $"Deleted {deleteResult.DeletedEntries.Count} items totaling {StringHelper.FormatBytes(deletedBytes)}{refreshedSuffix}"
                    : $"Moved {deleteResult.DeletedEntries.Count} items totaling {StringHelper.FormatBytes(deletedBytes)} into reversible trash{refreshedSuffix}";
                _viewModel.ShowSuccessMessage = true;
                _viewModel.ShowRestoreActionInSuccess = !_viewModel.PermanentlyDelete && deleteResult.Session != null;
            }

            if (!_viewModel.PermanentlyDelete && deleteResult.Session != null)
            {
                ApplyRestorableSession(deleteResult.Session);
            }

            _viewModel.StatusMessage = deleteResult switch
            {
                { DeletedEntries.Count: 0, Errors.Count: > 0 } => "Delete failed.",
                { Errors.Count: > 0 } => "Delete completed with warnings.",
                _ => "Delete completed."
            };
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = "Delete failed.";
            _viewModel.ErrorsList = [ex.Message];
            _viewModel.ErrorSummary = BuildErrorSummary(_viewModel.ErrorsList);
        }
        finally
        {
            _viewModel.Operation = UiOperationKind.Idle;
            _viewModel.IsProgressIndeterminate = false;
            _viewModel.ProgressValue = 0;
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Operation != UiOperationKind.Idle)
        {
            return;
        }

        _viewModel.ClearResults();
    }

    private void ViewErrors_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.IsErrorPaneOpen = true;
    }

    private void CloseErrorPane_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.IsErrorPaneOpen = false;
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

    private async void RestoreLastDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Operation != UiOperationKind.Idle || _lastDeleteSession == null)
        {
            return;
        }

        _viewModel.Operation = UiOperationKind.Restoring;
        _viewModel.IsProgressIndeterminate = true;
        _viewModel.ProgressValue = 0;
        _viewModel.StatusMessage = "Restoring last delete...";
        _viewModel.ShowSuccessMessage = false;
        _viewModel.ShowRestoreActionInSuccess = false;

        try
        {
            var restoreResult = await _deleteService.RestoreLastDeleteAsync(_lastDeleteSession);
            ApplyRestorableSession(restoreResult.RemainingSession);

            var allErrors = restoreResult.Errors.ToList();
            ScanResult? refreshResult = null;
            var refreshFailed = false;
            var rootPath = _viewModel.RootPath.Trim();

            if (restoreResult.RestoredCount > 0 &&
                !string.IsNullOrWhiteSpace(rootPath) &&
                Directory.Exists(rootPath))
            {
                try
                {
                    refreshResult = await RefreshCurrentResultsAsync(rootPath);
                    allErrors.AddRange(refreshResult.Errors);
                }
                catch (Exception ex)
                {
                    refreshFailed = true;
                    ClearResultsAfterMutationRefreshFailure("Items were restored, but refreshing the current results failed. Run Scan again.");
                    allErrors.Add($"Restored items, but refreshing the current results failed: {ex.Message}");
                }
            }

            _viewModel.ErrorsList = allErrors;
            _viewModel.ErrorSummary = BuildErrorSummary(_viewModel.ErrorsList);

            if (restoreResult.RestoredCount > 0)
            {
                var itemLabel = restoreResult.RestoredCount == 1 ? "item" : "items";
                _viewModel.SuccessMessage = refreshResult != null
                    ? $"Restored {restoreResult.RestoredCount} {itemLabel} and refreshed the results."
                    : $"Restored {restoreResult.RestoredCount} {itemLabel}.";
                _viewModel.ShowSuccessMessage = true;
            }

            if (restoreResult.RestoredCount > 0 && refreshResult == null && !refreshFailed)
            {
                _viewModel.SummaryText = "Items were restored. Run Scan again to refresh the current results.";
            }

            _viewModel.StatusMessage = restoreResult switch
            {
                { RestoredCount: 0, Errors.Count: > 0 } => "Restore failed.",
                { Errors.Count: > 0 } => "Restore completed with warnings.",
                _ => "Restore completed."
            };
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = "Restore failed.";
            _viewModel.ErrorsList = [ex.Message];
            _viewModel.ErrorSummary = BuildErrorSummary(_viewModel.ErrorsList);
        }
        finally
        {
            _viewModel.Operation = UiOperationKind.Idle;
            _viewModel.IsProgressIndeterminate = false;
            _viewModel.ProgressValue = 0;
        }
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

        var preview = string.Join(" | ", errors.Take(2));
        return errors.Count == 2
            ? preview
            : $"{preview} | {errors.Count - 2} more";
    }

    private Microsoft.UI.Xaml.XamlRoot? GetXamlRoot()
    {
        return (Content as UIElement)?.XamlRoot;
    }

    private void OpenInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item || item.Tag is not ScanNode node)
        {
            return;
        }

        var path = node.FullPath;
        if (File.Exists(path))
        {
            path = Path.GetDirectoryName(path);
        }

        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        {
            Process.Start("explorer.exe", path);
        }
    }

    private async void OpenIgnoreFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item || item.Tag is not ScanNode node)
        {
            return;
        }

        var paths = node.IgnoreRulePaths;
        if (paths.Count == 0)
        {
            await ShowMessageAsync("No ignore file associated with this item.");
            return;
        }

        if (paths.Count == 1)
        {
            OpenFile(paths[0]);
            return;
        }

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
