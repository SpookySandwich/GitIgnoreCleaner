using GitIgnoreCleaner.Models;

namespace GitIgnoreCleaner.Services;

public sealed class DeleteResult
{
    public List<string> Errors { get; } = [];
    public List<DeletionPlanEntry> DeletedEntries { get; } = [];
    public ReversibleDeleteSession? Session { get; set; }
}

public sealed class RestoreResult
{
    public List<string> Errors { get; } = [];
    public int RestoredCount { get; set; }
    public ReversibleDeleteSession? RemainingSession { get; set; }
}

public sealed class DeleteService
{
    private readonly DeletionPlanBuilder _deletionPlanBuilder = new();
    private readonly ReversibleTrashService _trashService = new();

    public DeletionPlan CreatePreviewPlan(ScanSnapshotNode? rootNode)
    {
        return _deletionPlanBuilder.CreatePreviewPlan(rootNode);
    }

    public DeletionPlan CreateDeletionPlan(IEnumerable<ScanNode> nodes)
    {
        return _deletionPlanBuilder.CreateSelectionPlan(nodes);
    }

    public Task<DeleteResult> DeleteTargetsAsync(
        string scanRootPath,
        DeletionPlan plan,
        bool permanentlyDelete,
        IProgress<DeletionPlanEntry>? progress = null)
    {
        return Task.Run(() => DeleteTargets(scanRootPath, plan, permanentlyDelete, progress));
    }

    public Task<RestoreResult> RestoreLastDeleteAsync(ReversibleDeleteSession session)
    {
        return Task.Run(() => _trashService.Restore(session));
    }

    public ReversibleDeleteSession? TryLoadLatestSession(string scanRootPath)
    {
        return _trashService.TryLoadLatestSession(scanRootPath);
    }

    private DeleteResult DeleteTargets(
        string scanRootPath,
        DeletionPlan plan,
        bool permanentlyDelete,
        IProgress<DeletionPlanEntry>? progress)
    {
        if (permanentlyDelete)
        {
            return DeletePermanently(plan.Entries, progress);
        }

        return _trashService.MoveToTrash(scanRootPath, plan.Entries, progress);
    }

    private static DeleteResult DeletePermanently(IReadOnlyList<DeletionPlanEntry> targets, IProgress<DeletionPlanEntry>? progress)
    {
        var result = new DeleteResult();
        foreach (var target in targets
                     .OrderBy(item => item.IsDirectory)
                     .ThenByDescending(item => item.FullPath.Length)
                     .ThenBy(item => item.FullPath, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                FileSystemEntryOperations.DeletePath(target.FullPath, target.IsDirectory);
                result.DeletedEntries.Add(target);
                progress?.Report(target);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Failed to delete {target.FullPath}: {ex.Message}");
            }
        }

        return result;
    }
}
