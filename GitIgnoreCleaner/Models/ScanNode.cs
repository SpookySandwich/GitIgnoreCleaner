using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using GitIgnoreCleaner.Helpers;

namespace GitIgnoreCleaner.Models;

public sealed class ScanNode : INotifyPropertyChanged
{
    private bool? _isChecked;
    private long _sizeBytes;

    public ScanNode(
        string displayName,
        string fullPath,
        bool isDirectory,
        bool isCandidate,
        long sizeBytes = 0,
        string matchedRuleSource = "",
        IEnumerable<string>? ignoreRulePaths = null)
    {
        DisplayName = displayName;
        FullPath = fullPath;
        IsDirectory = isDirectory;
        IsCandidate = isCandidate;
        MatchedRuleSource = matchedRuleSource;
        IgnoreRulePaths = ignoreRulePaths?.ToList() ?? [];
        Children = [];
        _isChecked = true;
        _sizeBytes = sizeBytes;
    }

    public string DisplayName { get; }

    public string FullPath { get; }

    public bool IsDirectory { get; }

    public bool IsCandidate { get; }

    public string MatchedRuleSource { get; }

    public List<string> IgnoreRulePaths { get; }

    public ObservableCollection<ScanNode> Children { get; }

    public ScanNode? Parent { get; private set; }

    public long SizeBytes
    {
        get => _sizeBytes;
        private set
        {
            if (_sizeBytes == value)
            {
                return;
            }

            _sizeBytes = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SizeText));
        }
    }

    public string SizeText => SizeBytes > 0 ? StringHelper.FormatBytes(SizeBytes) : string.Empty;

    public string HintText => !IsCandidate && IsDirectory ? "contains matches" : string.Empty;

    public bool? IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value)
            {
                return;
            }

            var targetState = value ?? false;
            SetInternalState(targetState);
            Parent?.ReflectChildrenState();
        }
    }

    public static ScanNode FromSnapshot(ScanSnapshotNode snapshot)
    {
        var initialSize = snapshot.IsDirectory && snapshot.Children.Count > 0
            ? 0
            : snapshot.SizeBytes;

        var node = new ScanNode(
            snapshot.DisplayName,
            snapshot.FullPath,
            snapshot.IsDirectory,
            snapshot.IsCandidate,
            initialSize,
            snapshot.MatchedRuleSource,
            snapshot.IgnoreRulePaths);

        foreach (var child in snapshot.Children)
        {
            node.AddChild(FromSnapshot(child));
        }

        if (snapshot.IsDirectory && snapshot.Children.Count == 0)
        {
            node.SetSize(snapshot.SizeBytes);
        }

        return node;
    }

    public void AddChild(ScanNode child)
    {
        child.Parent = this;
        Children.Add(child);

        if (child.SizeBytes > 0)
        {
            UpdateSizeUpwards(child.SizeBytes);
        }

        ReflectChildrenState();
    }

    public void RemoveChild(ScanNode child)
    {
        if (!Children.Remove(child))
        {
            return;
        }

        child.Parent = null;

        if (child.SizeBytes > 0)
        {
            UpdateSizeUpwards(-child.SizeBytes);
        }

        if (Children.Count == 0 && !IsCandidate && Parent != null)
        {
            Parent.RemoveChild(this);
            return;
        }

        ReflectChildrenState();
    }

    private void SetInternalState(bool isSelected)
    {
        if (_isChecked == isSelected)
        {
            return;
        }

        _isChecked = isSelected;
        OnPropertyChanged(nameof(IsChecked));

        foreach (var child in Children)
        {
            child.SetInternalState(isSelected);
        }
    }

    private void ReflectChildrenState()
    {
        if (Children.Count == 0)
        {
            if (_isChecked != false)
            {
                _isChecked = false;
                OnPropertyChanged(nameof(IsChecked));
            }

            return;
        }

        bool? newState;
        var hasTrue = false;
        var hasFalse = false;
        var hasNull = false;

        foreach (var child in Children)
        {
            var state = child.IsChecked;
            if (state == true)
            {
                hasTrue = true;
            }
            else if (state == false)
            {
                hasFalse = true;
            }
            else
            {
                hasNull = true;
            }

            if ((hasTrue && hasFalse) || hasNull)
            {
                newState = null;
                goto ApplyState;
            }
        }

        newState = hasTrue ? true : false;

    ApplyState:
        if (_isChecked == newState)
        {
            return;
        }

        _isChecked = newState;
        OnPropertyChanged(nameof(IsChecked));
        Parent?.ReflectChildrenState();
    }

    private void SetSize(long sizeBytes)
    {
        SizeBytes = sizeBytes;
    }

    private void UpdateSizeUpwards(long deltaBytes)
    {
        SizeBytes += deltaBytes;
        Parent?.UpdateSizeUpwards(deltaBytes);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
