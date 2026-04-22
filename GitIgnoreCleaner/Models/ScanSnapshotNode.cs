using System.Collections.Generic;

namespace GitIgnoreCleaner.Models;

public sealed record ScanSnapshotNode(
    string DisplayName,
    string FullPath,
    bool IsDirectory,
    bool IsCandidate,
    long SizeBytes,
    string MatchedRuleSource,
    IReadOnlyList<string> IgnoreRulePaths,
    IReadOnlyList<ScanSnapshotNode> Children);
