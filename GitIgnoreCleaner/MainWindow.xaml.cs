using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using GitIgnoreCleaner.Helpers;
using GitIgnoreCleaner.Models;
using GitIgnoreCleaner.Services;
using GitIgnoreCleaner.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;
using Windows.ApplicationModel;
using Windows.Graphics;
using Windows.Storage.Pickers;

namespace GitIgnoreCleaner;

public sealed partial class MainWindow : Window
{
    private const string RepositoryUrl = "https://github.com/SpookySandwich/GitIgnoreCleaner";
    private const string LicenseUrl = "https://github.com/SpookySandwich/GitIgnoreCleaner/blob/master/LICENSE.txt";

    private readonly MainViewModel _viewModel = new();
    private readonly ScanService _scanService = new();
    private readonly DeleteService _deleteService = new();

    private CancellationTokenSource? _scanCts;
    private IntPtr _windowHandle;
    private bool _languageSelectionReady;
    private bool _languageDropDownOpen;
    private string? _pendingLanguageTag;

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

        LanguageComboBox.DropDownOpened += LanguageComboBox_DropDownOpened;
        LanguageComboBox.DropDownClosed += LanguageComboBox_DropDownClosed;
        ApplyLocalizedText();
        _viewModel.ApplicationVersionLabel = LocalizationService.GetVersionLabel(GetApplicationVersion());
        _languageSelectionReady = true;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        SetDefaultWindowSize();

        Closed += (_, _) =>
        {
            _scanCts?.Cancel();
            _scanCts?.Dispose();
        };
    }

    private void ApplyLocalizedText()
    {
        Title = LocalizationService.GetString("AppDisplayName");
        AppTitleBar.Title = LocalizationService.GetString("AppDisplayName");
        WarningsPaneTitleText.Text = GetXamlResource("WarningsPaneTitleText.Text");
        MainTitleText.Text = LocalizationService.GetString("AppDisplayName");
        RootFolderTextBox.Header = GetXamlResource("RootFolderTextBox.Header");
        RootFolderTextBox.PlaceholderText = GetXamlResource("RootFolderTextBox.PlaceholderText");
        BrowseButton.Content = GetXamlResource("BrowseButton.Content");
        IgnoreFileNamesTextBox.Header = GetXamlResource("IgnoreFileNamesTextBox.Header");
        IgnoreFileNamesTextBox.Description = GetXamlResource("IgnoreFileNamesTextBox.Description");
        IgnoreFileNamesTextBox.PlaceholderText = GetXamlResource("IgnoreFileNamesTextBox.PlaceholderText");
        ScanExclusionsTitleText.Text = GetXamlResource("ScanExclusionsTitleText.Text");
        EditExclusionsButton.Content = GetXamlResource("EditExclusionsButton.Content");
        DeleteSelectedText.Text = GetXamlResource("DeleteSelectedText.Text");
        ClearText.Text = GetXamlResource("ClearText.Text");
        PermanentlyDeleteCheckBox.Content = GetXamlResource("PermanentlyDeleteCheckBox.Content");
        WarningsInfoBar.Title = GetXamlResource("WarningsInfoBar.Title");
        ViewErrorsButton.Content = GetXamlResource("ViewErrorsButton.Content");
        SuccessInfoBar.Title = GetXamlResource("SuccessInfoBar.Title");
        EmptyStateText.Text = GetXamlResource("EmptyStateText.Text");
        SettingsButton.Label = LocalizationService.GetString("SettingsButtonLabel");
        ToolTipService.SetToolTip(SettingsButton, LocalizationService.GetString("SettingsButtonToolTip"));
        SettingsTitleText.Text = GetXamlResource("SettingsTitleText.Text");
        SettingsAppNameText.Text = LocalizationService.GetString("AppDisplayName");
        SettingsDescriptionText.Text = LocalizationService.GetString("AppDescription");
        DisplayLanguageSettingsCard.Header = LocalizationService.GetString("DisplayLanguageCardHeader");
        LicenseLink.Content = GetXamlResource("LicenseLink.Content");
        RepositoryLink.Content = GetXamlResource("RepositoryLink.Content");
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

    private static string GetApplicationVersion()
    {
        try
        {
            var version = Package.Current.Id.Version;
            return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }
        catch
        {
        }

        try
        {
            var informationalVersion = Assembly
                .GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informationalVersion))
            {
                return informationalVersion.Split('+')[0];
            }
        }
        catch
        {
        }

        try
        {
            var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
            if (assemblyVersion != null)
            {
                return $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}.{assemblyVersion.Revision}";
            }
        }
        catch
        {
        }

        try
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath))
            {
                var fileVersion = FileVersionInfo.GetVersionInfo(processPath).FileVersion;
                if (!string.IsNullOrWhiteSpace(fileVersion))
                {
                    return fileVersion;
                }
            }
        }
        catch
        {
        }

        return "1.0.0.0";
    }

    private static string GetXamlResource(string key)
    {
        return LocalizationService.GetString(key.Replace(".", "/"));
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
            new Progress<int>(count => _viewModel.StatusMessage = LocalizationService.Format("StatusRefreshingProgress", count)));

        ApplyScanResults(result);
        return result;
    }

    private void ApplyScanResults(ScanResult result)
    {
        ReplaceResults(result.RootNode);
        _viewModel.SummaryText = result.PreviewPlan.Count == 0
            ? LocalizationService.GetString("SummaryNoMatches")
            : LocalizationService.Format("SummaryMatches", result.PreviewPlan.Count, StringHelper.FormatBytes(result.PreviewPlan.TotalBytes));
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
            Text = LocalizationService.GetString("ExclusionsDialogPrompt"),
            Style = (Style)Application.Current.Resources["BodyTextBlockStyle"]
        });
        stackPanel.Children.Add(textBox);

        var dialog = new ContentDialog
        {
            Title = LocalizationService.GetString("ExclusionsDialogTitle"),
            Content = stackPanel,
            PrimaryButtonText = LocalizationService.GetString("DialogSave"),
            CloseButtonText = LocalizationService.GetString("DialogCancel"),
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
            _viewModel.StatusMessage = LocalizationService.GetString("StatusCancelingScan");
            return;
        }

        if (_viewModel.Operation != UiOperationKind.Idle)
        {
            return;
        }

        var rootPath = _viewModel.RootPath.Trim();
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            await ShowMessageAsync(LocalizationService.GetString("MessageInvalidRootScan"));
            return;
        }

        _viewModel.ClearResults();
        _viewModel.Operation = UiOperationKind.Scanning;
        _viewModel.IsProgressIndeterminate = true;
        _viewModel.ProgressValue = 0;
        _viewModel.StatusMessage = LocalizationService.GetString("StatusScanning");

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
                new Progress<int>(count => _viewModel.StatusMessage = LocalizationService.Format("StatusScanningProgress", count)));

            ApplyScanResults(result);

            _viewModel.ErrorsList = result.Errors.ToList();
            _viewModel.ErrorSummary = BuildErrorSummary(_viewModel.ErrorsList);
            _viewModel.StatusMessage = result.Errors.Count == 0
                ? LocalizationService.GetString("StatusScanCompleted")
                : LocalizationService.GetString("StatusScanCompletedWithWarnings");

            if (result.PreviewPlan.Count > 0)
            {
                _viewModel.SuccessMessage = LocalizationService.Format("SuccessFoundItems", result.PreviewPlan.Count, StringHelper.FormatBytes(result.PreviewPlan.TotalBytes));
                _viewModel.ShowSuccessMessage = true;
            }
        }
        catch (OperationCanceledException)
        {
            _viewModel.ClearResults();
            _viewModel.StatusMessage = LocalizationService.GetString("StatusScanCanceled");
            _viewModel.SummaryText = LocalizationService.GetString("SummaryScanCanceled");
        }
        catch (Exception ex)
        {
            _viewModel.ClearResults();
            _viewModel.StatusMessage = LocalizationService.GetString("StatusScanFailed");
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
            await ShowMessageAsync(LocalizationService.GetString("MessageInvalidRootDelete"));
            return;
        }

        var plan = _deleteService.CreateDeletionPlan(_viewModel.Results);
        if (plan.Count == 0)
        {
            await ShowMessageAsync(LocalizationService.GetString("MessageNoItemsSelected"));
            return;
        }

        var confirm = new ContentDialog
        {
            Title = _viewModel.PermanentlyDelete
                ? LocalizationService.GetString("ConfirmPermanentDeleteTitle")
                : LocalizationService.GetString("ConfirmReversibleDeleteTitle"),
            Content = _viewModel.PermanentlyDelete
                ? LocalizationService.Format("ConfirmPermanentDeleteContent", plan.Count, StringHelper.FormatBytes(plan.TotalBytes))
                : LocalizationService.Format("ConfirmReversibleDeleteContent", plan.Count, StringHelper.FormatBytes(plan.TotalBytes)),
            PrimaryButtonText = _viewModel.PermanentlyDelete
                ? LocalizationService.GetString("DialogDelete")
                : LocalizationService.GetString("DialogMoveToTrash"),
            CloseButtonText = LocalizationService.GetString("DialogCancel"),
            XamlRoot = GetXamlRoot()
        };

        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        _viewModel.Operation = UiOperationKind.Deleting;
        _viewModel.IsProgressIndeterminate = false;
        _viewModel.ProgressValue = 0;
        _viewModel.StatusMessage = LocalizationService.GetString("StatusDeleting");
        _viewModel.ShowSuccessMessage = false;

        var deletedCount = 0;
        var totalCount = plan.Count;
        var progress = new Progress<DeletionPlanEntry>(_ =>
        {
            deletedCount++;
            _viewModel.ProgressValue = totalCount == 0 ? 0 : (double)deletedCount / totalCount * 100;
            _viewModel.StatusMessage = LocalizationService.Format("StatusDeletingProgress", deletedCount, totalCount);
        });

        try
        {
            var deleteResult = await _deleteService.DeleteTargetsAsync(plan, _viewModel.PermanentlyDelete, progress);
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
                    ClearResultsAfterMutationRefreshFailure(LocalizationService.GetString("SummaryDeleteRefreshFailed"));
                    allErrors.Add(LocalizationService.Format("ErrorDeleteRefreshFailed", ex.Message));
                }
            }

            _viewModel.ErrorsList = allErrors;
            _viewModel.ErrorSummary = BuildErrorSummary(_viewModel.ErrorsList);

            if (deleteResult.DeletedEntries.Count > 0)
            {
                _viewModel.SuccessMessage = _viewModel.PermanentlyDelete
                    ? LocalizationService.Format("SuccessDeleted", deleteResult.DeletedEntries.Count, StringHelper.FormatBytes(deletedBytes))
                    : LocalizationService.Format("SuccessMovedToTrash", deleteResult.DeletedEntries.Count, StringHelper.FormatBytes(deletedBytes));
                _viewModel.ShowSuccessMessage = true;
            }

            _viewModel.StatusMessage = deleteResult switch
            {
                { DeletedEntries.Count: 0, Errors.Count: > 0 } => LocalizationService.GetString("StatusDeleteFailed"),
                { Errors.Count: > 0 } => LocalizationService.GetString("StatusDeleteCompletedWithWarnings"),
                _ => LocalizationService.GetString("StatusDeleteCompleted")
            };
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = LocalizationService.GetString("StatusDeleteFailed");
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
            Title = LocalizationService.GetString("DialogAppTitle"),
            Content = message,
            CloseButtonText = LocalizationService.GetString("DialogOk"),
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

        var preview = string.Join(" | ", errors.Take(2));
        return errors.Count == 2
            ? preview
            : $"{preview} | {LocalizationService.Format("ErrorSummaryMore", errors.Count - 2)}";
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
            await ShowMessageAsync(LocalizationService.GetString("MessageNoIgnoreFileAssociated"));
            return;
        }

        if (paths.Count == 1)
        {
            OpenFile(paths[0]);
            return;
        }

        var dialog = new ContentDialog
        {
            Title = LocalizationService.GetString("DialogSelectIgnoreFile"),
            CloseButtonText = LocalizationService.GetString("DialogCancel"),
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
            _ = ShowMessageAsync(LocalizationService.Format("ErrorOpenFileFailed", ex.Message));
        }
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.CurrentPage = AppPageKind.Settings;
    }

    private async void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_languageSelectionReady ||
            sender is not ComboBox comboBox ||
            comboBox.SelectedValue is not string selectedTag)
        {
            return;
        }

        if (_languageDropDownOpen)
        {
            _pendingLanguageTag = selectedTag;
        }
    }

    private void LanguageComboBox_DropDownOpened(object? sender, object e)
    {
        _languageDropDownOpen = true;
        _pendingLanguageTag = _viewModel.SelectedLanguageTag;
    }

    private async void LanguageComboBox_DropDownClosed(object? sender, object e)
    {
        if (!_languageSelectionReady || !_languageDropDownOpen)
        {
            return;
        }

        _languageDropDownOpen = false;
        if (string.IsNullOrWhiteSpace(_pendingLanguageTag))
        {
            return;
        }

        var selectedTag = _pendingLanguageTag;
        _pendingLanguageTag = null;
        await ApplyLanguageSelectionAsync(selectedTag);
    }

    private async Task ApplyLanguageSelectionAsync(string selectedTag)
    {
        var previousTag = LocalizationService.GetSavedLanguageSelectionTag();
        if (string.Equals(selectedTag, previousTag, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            LocalizationService.SavePreferredLanguage(selectedTag);
        }
        catch (Exception ex)
        {
            _viewModel.SelectedLanguageTag = previousTag;
            await ShowMessageAsync(LocalizationService.Format("LanguagePreferenceSaveFailed", ex.Message));
            return;
        }

        var restartFailure = Microsoft.Windows.AppLifecycle.AppInstance.Restart(string.Empty);

        try
        {
            LocalizationService.SavePreferredLanguage(previousTag);
        }
        catch
        {
        }

        _viewModel.SelectedLanguageTag = previousTag;
        await ShowMessageAsync(LocalizationService.Format("LanguageRestartFailed", restartFailure));
    }

    private void CloseSettings_Click(object sender, RoutedEventArgs e)
    {
        ShowMainPage();
    }

    private void AppTitleBar_BackRequested(TitleBar sender, object args)
    {
        ShowMainPage();
    }

    private void ShowMainPage()
    {
        _viewModel.CurrentPage = AppPageKind.Main;
    }

    private void OpenRepository_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl(RepositoryUrl);
    }

    private void OpenLicense_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl(LicenseUrl);
    }

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _ = ShowMessageAsync(LocalizationService.Format("ErrorOpenLinkFailed", ex.Message));
        }
    }
}
