using System.Collections.Generic;
using System.IO;
using Ignore;

namespace GitIgnoreCleaner.Services;

// This replaces your complex IgnoreRule class
public sealed class IgnoreListWrapper
{
    private readonly Ignore.Ignore _ignore;

    public IgnoreListWrapper(IEnumerable<string> rules, string sourceFile)
    {
        SourceFile = sourceFile;
        _ignore = new Ignore.Ignore();
        _ignore.Add(rules); // The library handles comments, negations, and anchors automatically
    }

    public string SourceFile { get; }

    public bool IsIgnored(string relativePath)
    {
        // The library expects paths relative to where the .gitignore sits
        return _ignore.IsIgnored(relativePath);
    }
}

// Update the stack to hold the Wrappers instead of your custom Rules
public sealed class IgnoreRuleStack
{
    private readonly List<IgnoreListWrapper> _layers = [];

    public int Count => _layers.Count;

    public void Add(IgnoreListWrapper layer)
    {
        _layers.Add(layer);
    }

    public void RemoveLast(int count)
    {
        // Since we are adding one wrapper per file, we usually remove 1 at a time, 
        // but your logic supports batching.
        if (count > _layers.Count) count = _layers.Count;
        _layers.RemoveRange(_layers.Count - count, count);
    }

    // Returns: (IsIgnored, SourceFile that caused the ignore)
    public (bool IsIgnored, string? MatchedDetails, string? MatchedSourceFile) CheckIgnored(string fullPath)
    {
        // We iterate backwards (newest rules / deepest folders first)
        // Git spec says deeper .gitignores override parents.
        for (int i = _layers.Count - 1; i >= 0; i--)
        {
            var layer = _layers[i];
          
            // Calculate path relative to the specific .gitignore file's folder
            // We assume the SourceFile path is stored in the layer to derive directory
            var ignoreFileDir = Path.GetDirectoryName(layer.SourceFile);
            var relativePath = Path.GetRelativePath(ignoreFileDir!, fullPath);

             // If the target is outside this ignore file's scope (e.g. parent folder), skip
            if (relativePath.StartsWith("..")) continue;

            if (layer.IsIgnored(relativePath))
            {
                // Note: The 'Ignore' library doesn't easily tell us WHICH rule matched,
                // only THAT it matched. This is a trade-off. 
                // If you strictly need "MatchedRuleSource" for UI, you might need a different approaches,
                // but this ensures correctness.
                return (true, $"{Path.GetFileName(layer.SourceFile)} (matched)", layer.SourceFile);
            }
        }

        return (false, null, null);
    }
}

