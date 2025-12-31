using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using GitIgnoreCleaner.Models;

namespace GitIgnoreCleaner.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private string _rootPath = string.Empty;
    private string _ignoreFileNames = ".gitignore;.ignore";
    private bool _isScanning;
    private bool _isDeleting;
    private string _summaryText = "Select a root folder and scan to preview deletions.";
    private string _errorSummary = string.Empty;
    private bool _permanentlyDelete;
    private string _statusMessage = "Ready";
    private bool _showSuccessMessage;
    private string _successMessage = string.Empty;
    private double _progressValue;
    private bool _isProgressIndeterminate;

    public MainViewModel()
    {
        Results.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasResults));
            OnPropertyChanged(nameof(CanDelete));
            OnPropertyChanged(nameof(CanClear));
        };
    }

    public ObservableCollection<ScanNode> Results { get; } = [];

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
            OnPropertyChanged(nameof(CanScan));
        }
    }

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

    public bool IsScanning
    {
        get => _isScanning;
        set
        {
            if (_isScanning == value)
            {
                return;
            }

            _isScanning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(CanScan));
            OnPropertyChanged(nameof(CanDelete));
            OnPropertyChanged(nameof(CanClear));
        }
    }

    public bool IsDeleting
    {
        get => _isDeleting;
        set
        {
            if (_isDeleting == value)
            {
                return;
            }

            _isDeleting = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(CanScan));
            OnPropertyChanged(nameof(CanDelete));
            OnPropertyChanged(nameof(CanClear));
        }
    }

    public bool IsBusy => _isScanning || _isDeleting;

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

    // CanScan is now true even if busy, because the button will turn into Cancel.
    // But we still need a valid path.
    public bool CanScan => !IsDeleting && !string.IsNullOrWhiteSpace(RootPath) && Directory.Exists(RootPath);

    public bool CanDelete => !IsBusy && HasResults;

    public bool CanClear => !IsBusy && HasResults;

    public event PropertyChangedEventHandler? PropertyChanged;

    public List<string> GetParsedIgnoreFileNames()
    {
        return IgnoreFileNames
            .Split([';', ','], StringSplitOptions.RemoveEmptyEntries)
            .Select(name => name.Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void ClearResults()
    {
        Results.Clear();
        SummaryText = "Select a root folder and scan to preview deletions.";
        ErrorSummary = string.Empty;
        StatusMessage = "Ready";
        ShowSuccessMessage = false;
        ProgressValue = 0;
        IsProgressIndeterminate = false;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

