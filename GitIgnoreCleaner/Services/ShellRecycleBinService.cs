using System.Runtime.InteropServices;

namespace GitIgnoreCleaner.Services;

internal sealed class ShellRecycleBinService
{
    private const uint FoDelete = 0x0003;
    private const ushort FofSilent = 0x0004;
    private const ushort FofNoConfirmation = 0x0010;
    private const ushort FofAllowUndo = 0x0040;
    private const ushort FofNoErrorUi = 0x0400;

    public DeleteResult MoveToRecycleBin(IReadOnlyList<DeletionPlanEntry> targets, IProgress<DeletionPlanEntry>? progress = null)
    {
        var result = new DeleteResult();

        foreach (var target in targets
                     .OrderBy(item => item.IsDirectory)
                     .ThenByDescending(item => item.FullPath.Length)
                     .ThenBy(item => item.FullPath, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var normalizedPath = FileSystemEntryOperations.NormalizePath(target.FullPath);
                if (!FileSystemEntryOperations.PathExists(normalizedPath, target.IsDirectory))
                {
                    result.DeletedEntries.Add(target);
                    progress?.Report(target);
                    continue;
                }

                MovePathToRecycleBin(normalizedPath);
                result.DeletedEntries.Add(target);
                progress?.Report(target);
            }
            catch (Exception ex)
            {
                result.Errors.Add(LocalizationService.Format("ErrorMoveToRecycleBin", target.FullPath, ex.Message));
            }
        }

        return result;
    }

    private static void MovePathToRecycleBin(string normalizedPath)
    {
        var operation = new ShFileOpStruct
        {
            wFunc = FoDelete,
            pFrom = normalizedPath + '\0' + '\0',
            fFlags = FofAllowUndo | FofNoConfirmation | FofSilent | FofNoErrorUi
        };

        var returnCode = SHFileOperation(ref operation);
        if (returnCode != 0)
        {
            throw new IOException($"{LocalizationService.GetString("ErrorShellOperationCode")} {returnCode} (0x{returnCode:X8}).");
        }

        if (operation.fAnyOperationsAborted)
        {
            throw new IOException(LocalizationService.GetString("ErrorMoveToRecycleBinAborted"));
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref ShFileOpStruct fileOp);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOpStruct
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszProgressTitle;
    }
}
