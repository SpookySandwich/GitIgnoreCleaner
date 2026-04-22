using GitIgnoreCleaner.Models;

namespace GitIgnoreCleaner.Services;

public sealed record DeletionPlanEntry(
    string DisplayName,
    string FullPath,
    bool IsDirectory,
    long SizeBytes,
    ScanNode? SourceNode = null);

public sealed class DeletionPlan
{
    public static DeletionPlan Empty { get; } = new([]);

    public DeletionPlan(IReadOnlyList<DeletionPlanEntry> entries)
    {
        Entries = entries;
        TotalBytes = entries.Sum(entry => entry.SizeBytes);
    }

    public IReadOnlyList<DeletionPlanEntry> Entries { get; }

    public int Count => Entries.Count;

    public long TotalBytes { get; }
}

public sealed class DeletionPlanBuilder
{
    public DeletionPlan CreatePreviewPlan(ScanSnapshotNode? rootNode)
    {
        if (rootNode is null)
        {
            return DeletionPlan.Empty;
        }

        var entries = new List<DeletionPlanEntry>();
        foreach (var child in rootNode.Children)
        {
            CollectPreviewTargets(child, entries);
        }

        return new DeletionPlan(SortEntries(entries));
    }

    public DeletionPlan CreateSelectionPlan(IEnumerable<ScanNode> roots)
    {
        var entries = new List<DeletionPlanEntry>();
        foreach (var root in roots)
        {
            CollectSelectedTargets(root, entries);
        }

        return new DeletionPlan(SortEntries(entries));
    }

    private static void CollectPreviewTargets(ScanSnapshotNode node, List<DeletionPlanEntry> entries)
    {
        if (node.IsCandidate)
        {
            entries.Add(new DeletionPlanEntry(node.DisplayName, node.FullPath, node.IsDirectory, node.SizeBytes));
            return;
        }

        foreach (var child in node.Children)
        {
            CollectPreviewTargets(child, entries);
        }
    }

    private static void CollectSelectedTargets(ScanNode node, List<DeletionPlanEntry> entries)
    {
        if (node.IsChecked == false)
        {
            return;
        }

        if (node.IsCandidate && node.IsChecked == true)
        {
            entries.Add(new DeletionPlanEntry(node.DisplayName, node.FullPath, node.IsDirectory, node.SizeBytes, node));
            return;
        }

        foreach (var child in node.Children)
        {
            CollectSelectedTargets(child, entries);
        }
    }

    private static IReadOnlyList<DeletionPlanEntry> SortEntries(IEnumerable<DeletionPlanEntry> entries)
    {
        return entries
            .OrderBy(entry => entry.FullPath.Length)
            .ThenBy(entry => entry.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
