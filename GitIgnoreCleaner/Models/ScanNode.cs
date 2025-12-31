using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
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
        IEnumerable<string>? ignoreRulePaths = null,
        ObservableCollection<ScanNode>? children = null)
    {
        DisplayName = displayName;
        FullPath = fullPath;
        IsDirectory = isDirectory;
        IsCandidate = isCandidate;
        MatchedRuleSource = matchedRuleSource;
        IgnoreRulePaths = ignoreRulePaths?.ToList() ?? [];
        Children = children ?? [];
        _isChecked = true;
        SizeBytes = sizeBytes;
    }

    public string DisplayName { get; private set; }
    public string FullPath { get; private set; }
    public bool IsDirectory { get; }
    public bool IsCandidate { get; private set; }
    public string MatchedRuleSource { get; private set; }
    public List<string> IgnoreRulePaths { get; private set; }
    public ObservableCollection<ScanNode> Children { get; }
    public ScanNode? Parent { get; private set; }

    public long SizeBytes
    {
        get => _sizeBytes;
        private set
        {
            if (_sizeBytes == value) return;
            _sizeBytes = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SizeText));
        }
    }

    public string SizeText => SizeBytes > 0 ? StringHelper.FormatBytes(SizeBytes) : string.Empty;

    public string HintText => !IsCandidate && IsDirectory ? "contains matches" : string.Empty;

    public void SetIsCandidate(bool value)
    {
        if (IsCandidate == value) return;
        IsCandidate = value;
        OnPropertyChanged(nameof(IsCandidate));
        OnPropertyChanged(nameof(HintText));
    }

    public bool? IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) return;

            bool targetState = value ?? false;

            SetInternalState(targetState);
            Parent?.ReflectChildrenState();
        }
    }

    private void SetInternalState(bool isSelected)
    {
        if (_isChecked == isSelected) return;

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
            // If the node became empty and is still in the tree, 
            // ensure we don't leave it in a confused checked state.
            if (_isChecked != false)
            {
                _isChecked = false;
                OnPropertyChanged(nameof(IsChecked));
            }
            return;
        }

        bool? newState;
        bool hasTrue = false;
        bool hasFalse = false;
        bool hasNull = false;

        foreach (var child in Children)
        {
            var state = child.IsChecked;
            if (state == true) hasTrue = true;
            else if (state == false) hasFalse = true;
            else hasNull = true;

            if ((hasTrue && hasFalse) || hasNull)
            {
                newState = null;
                goto ApplyState;
            }
        }

        if (hasTrue) newState = true;
        else newState = false;

        ApplyState:
        if (_isChecked != newState)
        {
            _isChecked = newState;
            OnPropertyChanged(nameof(IsChecked));
            Parent?.ReflectChildrenState();
        }
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
        if (Children.Remove(child))
        {
            child.Parent = null;

            if (child.SizeBytes > 0)
            {
                UpdateSizeUpwards(-child.SizeBytes);
            }

            // If this node is just a container (not an ignore candidate itself)
            // and it is now empty, it serves no purpose in the UI. 
            // Remove it from its parent recursively.
            if (Children.Count == 0 && !IsCandidate && Parent != null)
            {
                Parent.RemoveChild(this);
            }
            else
            {
                ReflectChildrenState();
            }
        }
    }

    public void FinalizeFileSize(long size)
    {
        if (size <= 0) return;
        UpdateSizeUpwards(size);
    }

    public long RecalculateSize()
    {
        if (!IsDirectory) return SizeBytes;

        long sum = 0;
        foreach (var child in Children)
        {
            sum += child.RecalculateSize();
        }

        if (SizeBytes != sum)
        {
            SizeBytes = sum;
        }
        return SizeBytes;
    }

    private void UpdateSizeUpwards(long deltaBytes)
    {
        SizeBytes += deltaBytes;
        Parent?.UpdateSizeUpwards(deltaBytes);
    }

    public void Compact()
    {
        // Compact children first (bottom-up)
        foreach (var child in Children.ToList())
        {
            child.Compact();
        }

        // Try to compact self with single child
        if (IsDirectory && !IsCandidate && Children.Count == 1)
        {
            var child = Children[0];
            if (child.IsDirectory)
            {
                // Merge child into this node
                DisplayName = $"{DisplayName}/{child.DisplayName}";
                FullPath = child.FullPath;
                IsCandidate = child.IsCandidate;
                MatchedRuleSource = child.MatchedRuleSource;
                IgnoreRulePaths = child.IgnoreRulePaths;
                
                // Move grandchildren to children
                var grandChildren = child.Children.ToList();
                Children.Clear();
                foreach (var gc in grandChildren)
                {
                    AddChild(gc);
                }

                // Update size
                SizeBytes = child.SizeBytes;
                
                // Since we merged, we might be able to merge again if the child was already compacted
                // But since we did bottom-up, 'child' is already compacted.
                // So if 'child' had 1 child, it's already merged into 'child'.
                // Now we merged 'child' into 'this'.
                // So 'this' now represents 'this/child/grandChild...'.
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
