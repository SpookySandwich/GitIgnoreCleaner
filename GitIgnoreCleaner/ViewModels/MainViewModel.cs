using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using GitIgnoreCleaner.Models;
using GitIgnoreCleaner.Services;

namespace GitIgnoreCleaner.ViewModels;

public enum UiOperationKind
{
    Idle,
    Scanning,
    Deleting
}

public enum AppPageKind
{
    Main,
    Settings
}

public sealed class MainViewModel : INotifyPropertyChanged
{
    private const string LegacyManagedTrashFolderName = ".gitignorecleaner-trash";

    private string _rootPath = string.Empty;
    private string _ignoreFileNames = ".gitignore;.ignore";
    private string _excludedFolderNames = $".git;.vs;.idea;.vscode;.gitignorecleaner-data;{LegacyManagedTrashFolderName};$Recycle.Bin;System Volume Information;Windows;Program Files;Program Files (x86);ProgramData;Recovery;Config.Msi";
    private AppPageKind _currentPage = AppPageKind.Main;
    private UiOperationKind _operation = UiOperationKind.Idle;
    private string _summaryText = string.Empty;
    private string _errorSummary = string.Empty;
    private List<string> _errorsList = [];
    private bool _isErrorPaneOpen;
    private bool _permanentlyDelete;
    private string _statusMessage = string.Empty;
    private bool _showSuccessMessage;
    private string _successMessage = string.Empty;
    private double _progressValue;
    private bool _isProgressIndeterminate;
    private string _selectedLanguageTag = LocalizationService.GetSavedLanguageSelectionTag();
    private string _applicationVersionLabel = string.Empty;

    public MainViewModel()
    {
        foreach (var language in LocalizationService.GetAvailableLanguages())
        {
            AvailableLanguages.Add(language);
        }

        ResetIdleText();

        Results.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasResults));
            OnPropertyChanged(nameof(CanDelete));
            OnPropertyChanged(nameof(CanClear));
        };
    }

    public ObservableCollection<ScanNode> Results { get; } = [];

    public ObservableCollection<LanguageOption> AvailableLanguages { get; } = [];

    public string RootPath
    {
        get => _rootPath;
        set
        {
            if (_rootPath == value)
            {
                return;
            }

            _rootPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanScanAction));
        }
    }

    public AppPageKind CurrentPage
    {
        get => _currentPage;
        set
        {
            if (_currentPage == value)
            {
                return;
            }

            _currentPage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsMainPageVisible));
            OnPropertyChanged(nameof(IsSettingsPageVisible));
        }
    }

    public bool IsMainPageVisible => CurrentPage == AppPageKind.Main;

    public bool IsSettingsPageVisible => CurrentPage == AppPageKind.Settings;

    public string IgnoreFileNames
    {
        get => _ignoreFileNames;
        set
        {
            if (_ignoreFileNames == value)
            {
                return;
            }

            _ignoreFileNames = value;
            OnPropertyChanged();
        }
    }

    public string ExcludedFolderNames
    {
        get => _excludedFolderNames;
        set
        {
            if (_excludedFolderNames == value)
            {
                return;
            }

            _excludedFolderNames = value;
            OnPropertyChanged();
        }
    }

    public UiOperationKind Operation
    {
        get => _operation;
        set
        {
            if (_operation == value)
            {
                return;
            }

            _operation = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(IsScanning));
            OnPropertyChanged(nameof(IsDeleting));
            OnPropertyChanged(nameof(CanScanAction));
            OnPropertyChanged(nameof(CanDelete));
            OnPropertyChanged(nameof(CanClear));
        }
    }

    public bool IsBusy => Operation != UiOperationKind.Idle;

    public bool IsScanning => Operation == UiOperationKind.Scanning;

    public bool IsDeleting => Operation == UiOperationKind.Deleting;

    public double ProgressValue
    {
        get => _progressValue;
        set
        {
            if (Math.Abs(_progressValue - value) < 0.1)
            {
                return;
            }

            _progressValue = value;
            OnPropertyChanged();
        }
    }

    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        set
        {
            if (_isProgressIndeterminate == value)
            {
                return;
            }

            _isProgressIndeterminate = value;
            OnPropertyChanged();
        }
    }

    public string SummaryText
    {
        get => _summaryText;
        set
        {
            if (_summaryText == value)
            {
                return;
            }

            _summaryText = value;
            OnPropertyChanged();
        }
    }

    public string ErrorSummary
    {
        get => _errorSummary;
        set
        {
            if (_errorSummary == value)
            {
                return;
            }

            _errorSummary = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasErrors));
        }
    }

    public List<string> ErrorsList
    {
        get => _errorsList;
        set
        {
            if (_errorsList == value)
            {
                return;
            }

            _errorsList = value;
            OnPropertyChanged();
        }
    }

    public bool IsErrorPaneOpen
    {
        get => _isErrorPaneOpen;
        set
        {
            if (_isErrorPaneOpen == value)
            {
                return;
            }

            _isErrorPaneOpen = value;
            OnPropertyChanged();
        }
    }

    public bool HasResults => Results.Count > 0;

    public bool HasErrors => !string.IsNullOrWhiteSpace(ErrorSummary);

    public bool ShowSuccessMessage
    {
        get => _showSuccessMessage;
        set
        {
            if (_showSuccessMessage == value)
            {
                return;
            }

            _showSuccessMessage = value;
            OnPropertyChanged();
        }
    }

    public string SuccessMessage
    {
        get => _successMessage;
        set
        {
            if (_successMessage == value)
            {
                return;
            }

            _successMessage = value;
            OnPropertyChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (_statusMessage == value)
            {
                return;
            }

            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public bool PermanentlyDelete
    {
        get => _permanentlyDelete;
        set
        {
            if (_permanentlyDelete == value)
            {
                return;
            }

            _permanentlyDelete = value;
            OnPropertyChanged();
        }
    }

    public bool CanScanAction =>
        !string.IsNullOrWhiteSpace(RootPath) &&
        Directory.Exists(RootPath) &&
        Operation is UiOperationKind.Idle or UiOperationKind.Scanning;

    public string ApplicationVersionLabel
    {
        get => _applicationVersionLabel;
        set
        {
            if (_applicationVersionLabel == value)
            {
                return;
            }

            _applicationVersionLabel = value;
            OnPropertyChanged();
        }
    }

    public string SelectedLanguageTag
    {
        get => _selectedLanguageTag;
        set
        {
            if (_selectedLanguageTag == value)
            {
                return;
            }

            _selectedLanguageTag = value;
            OnPropertyChanged();
        }
    }

    public bool CanDelete => Operation == UiOperationKind.Idle && HasResults;

    public bool CanClear => Operation == UiOperationKind.Idle && HasResults;

    public event PropertyChangedEventHandler? PropertyChanged;

    public List<string> GetParsedIgnoreFileNames()
    {
        return ParseSemicolonList(IgnoreFileNames);
    }

    public List<string> GetParsedExcludedFolderNames()
    {
        return ParseSemicolonList(ExcludedFolderNames);
    }

    public void ClearResults()
    {
        Results.Clear();
        ResetIdleText();
        ErrorSummary = string.Empty;
        ErrorsList = [];
        IsErrorPaneOpen = false;
        ShowSuccessMessage = false;
        SuccessMessage = string.Empty;
        ProgressValue = 0;
        IsProgressIndeterminate = false;
    }

    private static List<string> ParseSemicolonList(string input)
    {
        return input
            .Split([';', ','], StringSplitOptions.RemoveEmptyEntries)
            .Select(name => name.Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void ResetIdleText()
    {
        SummaryText = LocalizationService.GetString("SummaryIdle");
        StatusMessage = LocalizationService.GetString("StatusReady");
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
