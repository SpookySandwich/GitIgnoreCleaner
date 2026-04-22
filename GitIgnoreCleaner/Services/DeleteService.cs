using GitIgnoreCleaner.Models;

namespace GitIgnoreCleaner.Services;

public sealed class DeleteResult
{
    public List<string> Errors { get; } = [];
    public List<DeletionPlanEntry> DeletedEntries { get; } = [];
}

public sealed class DeleteService
{
    private readonly DeletionPlanBuilder _deletionPlanBuilder = new();
    private readonly ShellRecycleBinService _shellRecycleBinService = new();

    public DeletionPlan CreatePreviewPlan(ScanSnapshotNode? rootNode)
    {
        return _deletionPlanBuilder.CreatePreviewPlan(rootNode);
    }

    public DeletionPlan CreateDeletionPlan(IEnumerable<ScanNode> nodes)
    {
        return _deletionPlanBuilder.CreateSelectionPlan(nodes);
    }

    public Task<DeleteResult> DeleteTargetsAsync(
        DeletionPlan plan,
        bool permanentlyDelete,
        IProgress<DeletionPlanEntry>? progress = null)
    {
        return Task.Run(() => DeleteTargets(plan, permanentlyDelete, progress));
    }

    private DeleteResult DeleteTargets(
        DeletionPlan plan,
        bool permanentlyDelete,
        IProgress<DeletionPlanEntry>? progress)
    {
        if (permanentlyDelete)
        {
            return DeletePermanently(plan.Entries, progress);
        }

        return _shellRecycleBinService.MoveToRecycleBin(plan.Entries, progress);
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
                result.Errors.Add(LocalizationService.Format("ErrorDeletePath", target.FullPath, ex.Message));
            }
        }

        return result;
    }
}
