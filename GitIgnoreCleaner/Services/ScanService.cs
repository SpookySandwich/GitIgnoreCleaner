using GitIgnoreCleaner.Models;

namespace GitIgnoreCleaner.Services;

public sealed class ScanResult
{
    public required ScanSnapshotNode RootNode { get; set; }
    public required DeletionPlan PreviewPlan { get; set; }
    public List<string> Errors { get; } = [];
    public int ProcessedEntryCount { get; set; }
}

public sealed class ScanService
{
    private readonly DeletionPlanBuilder _deletionPlanBuilder = new();

    public Task<ScanResult> ScanAsync(
        string rootPath,
        IReadOnlyList<string> ignoreFileNames,
        IReadOnlyList<string> excludedFolderNames,
        CancellationToken cancellationToken,
        IProgress<int>? progress = null)
    {
        return Task.Run(() => Scan(rootPath, ignoreFileNames, excludedFolderNames, cancellationToken, progress), cancellationToken);
    }

    private ScanResult Scan(
        string rootPath,
        IReadOnlyList<string> ignoreFileNames,
        IReadOnlyList<string> excludedFolderNames,
        CancellationToken cancellationToken,
        IProgress<int>? progress)
    {
        var normalizedRootPath = FileSystemEntryOperations.NormalizePath(rootPath);
        var result = new ScanResult
        {
            RootNode = new ScanSnapshotNode(normalizedRootPath, normalizedRootPath, IsDirectory: true, IsCandidate: false, 0, string.Empty, [], []),
            PreviewPlan = DeletionPlan.Empty
        };

        var orderedIgnoreFileNames = ignoreFileNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var ignoreSet = new HashSet<string>(orderedIgnoreFileNames, StringComparer.OrdinalIgnoreCase);
        var excludedSet = new HashSet<string>(excludedFolderNames, StringComparer.OrdinalIgnoreCase);
        var rules = new IgnoreRuleStack();
        var processedEntryCount = 0;

        var rootChildren = ScanDirectory(normalizedRootPath, rules, orderedIgnoreFileNames, ignoreSet, excludedSet, result, cancellationToken, progress, ref processedEntryCount);
        var compactedChildren = rootChildren
            .Select(CompactNode)
            .ToList();

        result.RootNode = new ScanSnapshotNode(
            normalizedRootPath,
            normalizedRootPath,
            IsDirectory: true,
            IsCandidate: false,
            compactedChildren.Sum(child => child.SizeBytes),
            string.Empty,
            [],
            compactedChildren);

        result.PreviewPlan = _deletionPlanBuilder.CreatePreviewPlan(result.RootNode);
        result.ProcessedEntryCount = processedEntryCount;
        progress?.Report(processedEntryCount);

        return result;
    }

    private static List<ScanSnapshotNode> ScanDirectory(
        string directoryPath,
        IgnoreRuleStack rules,
        IReadOnlyList<string> orderedIgnoreFileNames,
        HashSet<string> ignoreFileNames,
        HashSet<string> excludedFolderNames,
        ScanResult result,
        CancellationToken cancellationToken,
        IProgress<int>? progress,
        ref int processedEntryCount)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var addedRuleCount = LoadIgnoreRules(directoryPath, rules, result, orderedIgnoreFileNames);
        string[] entries;

        try
        {
            entries = Directory.GetFileSystemEntries(directoryPath);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Failed to read {directoryPath}: {ex.Message}");
            rules.RemoveLast(addedRuleCount);
            return [];
        }

        var children = new List<ScanSnapshotNode>();

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            processedEntryCount++;

            if (processedEntryCount % 50 == 0)
            {
                progress?.Report(processedEntryCount);
            }

            var name = Path.GetFileName(entry);
            if (ignoreFileNames.Contains(name))
            {
                continue;
            }

            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(entry);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Failed to inspect {entry}: {ex.Message}");
                continue;
            }

            var isDirectory = attributes.HasFlag(FileAttributes.Directory);
            if (isDirectory && excludedFolderNames.Contains(name))
            {
                continue;
            }

            if (isDirectory)
            {
                if (ShouldTreatAsDirectoryLink(entry, attributes))
                {
                    var linkMatch = rules.Evaluate(entry, isDirectory: true);
                    if (linkMatch.IsIgnored)
                    {
                        children.Add(CreateNode(name, entry, isDirectory: true, sizeBytes: 0, linkMatch, []));
                    }

                    continue;
                }

                var directoryMatch = rules.Evaluate(entry, isDirectory: true);
                if (directoryMatch.IsIgnored)
                {
                    var directorySize = FileSystemEntryOperations.MeasurePathSize(entry, isDirectory: true, result.Errors);
                    children.Add(CreateNode(name, entry, isDirectory: true, directorySize, directoryMatch, []));
                    continue;
                }

                var nestedChildren = ScanDirectory(
                    entry,
                    rules,
                    orderedIgnoreFileNames,
                    ignoreFileNames,
                    excludedFolderNames,
                    result,
                    cancellationToken,
                    progress,
                    ref processedEntryCount);

                if (nestedChildren.Count == 0)
                {
                    continue;
                }

                children.Add(CreateDirectoryContainer(name, entry, nestedChildren));
                continue;
            }

            var fileMatch = rules.Evaluate(entry, isDirectory: false);
            if (!fileMatch.IsIgnored)
            {
                continue;
            }

            var fileSize = FileSystemEntryOperations.MeasurePathSize(entry, isDirectory: false, result.Errors);
            children.Add(CreateNode(name, entry, isDirectory: false, fileSize, fileMatch, []));
        }

        rules.RemoveLast(addedRuleCount);
        return children;
    }

    private static bool ShouldTreatAsDirectoryLink(string entryPath, FileAttributes attributes)
    {
        if (!attributes.HasFlag(FileAttributes.Directory) || !attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return false;
        }

        try
        {
            var info = new DirectoryInfo(entryPath);
            return info.LinkTarget != null;
        }
        catch
        {
            return true;
        }
    }

    private static ScanSnapshotNode CreateDirectoryContainer(string displayName, string fullPath, IReadOnlyList<ScanSnapshotNode> children)
    {
        return new ScanSnapshotNode(
            displayName,
            FileSystemEntryOperations.NormalizePath(fullPath),
            IsDirectory: true,
            IsCandidate: false,
            children.Sum(child => child.SizeBytes),
            string.Empty,
            [],
            children);
    }

    private static ScanSnapshotNode CreateNode(
        string displayName,
        string fullPath,
        bool isDirectory,
        long sizeBytes,
        IgnoreMatch match,
        IReadOnlyList<ScanSnapshotNode> children)
    {
        var ignoreRulePaths = match.SourceFile is null
            ? Array.Empty<string>()
            : [match.SourceFile];

        return new ScanSnapshotNode(
            displayName,
            FileSystemEntryOperations.NormalizePath(fullPath),
            IsDirectory: isDirectory,
            IsCandidate: true,
            sizeBytes,
            match.MatchedRule ?? string.Empty,
            ignoreRulePaths,
            children);
    }

    private static ScanSnapshotNode CompactNode(ScanSnapshotNode node)
    {
        if (!node.IsDirectory)
        {
            return node;
        }

        var compactedChildren = node.Children
            .Select(CompactNode)
            .ToList();

        var effectiveSize = compactedChildren.Count > 0
            ? compactedChildren.Sum(child => child.SizeBytes)
            : node.SizeBytes;

        var current = node with
        {
            SizeBytes = effectiveSize,
            Children = compactedChildren
        };

        while (current is { IsCandidate: false } && current.Children.Count == 1 && current.Children[0].IsDirectory)
        {
            var child = current.Children[0];
            current = new ScanSnapshotNode(
                $"{current.DisplayName}/{child.DisplayName}",
                child.FullPath,
                IsDirectory: true,
                child.IsCandidate,
                child.SizeBytes,
                child.MatchedRuleSource,
                child.IgnoreRulePaths,
                child.Children);
        }

        return current;
    }

    private static int LoadIgnoreRules(
        string directoryPath,
        IgnoreRuleStack rules,
        ScanResult result,
        IReadOnlyList<string> orderedIgnoreFileNames)
    {
        var addedRuleCount = 0;

        foreach (var ignoreFileName in orderedIgnoreFileNames)
        {
            var ignorePath = Path.Combine(directoryPath, ignoreFileName);
            if (!File.Exists(ignorePath))
            {
                continue;
            }

            try
            {
                var attributes = File.GetAttributes(ignorePath);
                if (attributes.HasFlag(FileAttributes.Offline))
                {
                    continue;
                }

                rules.Add(IgnoreFileParser.ParseFile(ignorePath));
                addedRuleCount++;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Failed to read ignore file {ignorePath}: {ex.Message}");
            }
        }

        return addedRuleCount;
    }
}
