using System.Text;
using System.Text.RegularExpressions;

namespace GitIgnoreCleaner.Services;

public enum IgnoreDecision
{
    None,
    Ignore,
    Include
}

public sealed record IgnoreMatch(IgnoreDecision Decision, string? MatchedRule, string? SourceFile)
{
    public static IgnoreMatch None { get; } = new(IgnoreDecision.None, null, null);

    public bool IsIgnored => Decision == IgnoreDecision.Ignore;
}

public sealed class IgnorePatternRule
{
    private static readonly RegexOptions RegexOptions =
        System.Text.RegularExpressions.RegexOptions.Compiled |
        System.Text.RegularExpressions.RegexOptions.CultureInvariant |
        System.Text.RegularExpressions.RegexOptions.IgnoreCase;

    private readonly Regex _matcher;

    private IgnorePatternRule(
        string sourceFile,
        string originalText,
        bool isNegation,
        bool directoryOnly,
        string patternText,
        bool matchFromRoot)
    {
        SourceFile = sourceFile;
        OriginalText = originalText;
        IsNegation = isNegation;
        DirectoryOnly = directoryOnly;
        PatternText = patternText;
        _matcher = new Regex(BuildRegexPattern(patternText, matchFromRoot), RegexOptions);
    }

    public string SourceFile { get; }

    public string OriginalText { get; }

    public bool IsNegation { get; }

    public bool DirectoryOnly { get; }

    public string PatternText { get; }

    private IgnoreDecision Decision => IsNegation ? IgnoreDecision.Include : IgnoreDecision.Ignore;

    public IgnoreMatch CreateMatch()
    {
        return new IgnoreMatch(Decision, OriginalText.Trim(), SourceFile);
    }

    public bool Matches(string relativePath, bool isDirectory)
    {
        if (DirectoryOnly && !isDirectory)
        {
            return false;
        }

        return _matcher.IsMatch(relativePath);
    }

    public static IgnorePatternRule? Parse(string rawLine, string sourceFile)
    {
        var line = TrimTrailingUnescapedWhitespace(rawLine);
        if (string.IsNullOrEmpty(line))
        {
            return null;
        }

        var leadingMarkerEscaped =
            line.Length > 1 &&
            line[0] == '\\' &&
            (line[1] == '#' || line[1] == '!');

        if (line[0] == '#' && !leadingMarkerEscaped)
        {
            return null;
        }

        var isNegation = line[0] == '!' && !leadingMarkerEscaped;
        if (isNegation)
        {
            line = line[1..];
        }

        var directoryOnly = line.EndsWith("/", StringComparison.Ordinal);
        if (directoryOnly)
        {
            line = line[..^1];
        }

        var anchored = line.StartsWith("/", StringComparison.Ordinal);
        if (anchored)
        {
            line = line[1..];
        }

        var patternText = UnescapePattern(line);
        if (string.IsNullOrEmpty(patternText))
        {
            return null;
        }

        var matchFromRoot = anchored || patternText.Contains('/', StringComparison.Ordinal);
        return new IgnorePatternRule(sourceFile, rawLine, isNegation, directoryOnly, patternText, matchFromRoot);
    }

    private static string TrimTrailingUnescapedWhitespace(string value)
    {
        var end = value.Length;
        while (end > 0 && value[end - 1] == ' ')
        {
            var backslashCount = 0;
            for (var index = end - 2; index >= 0 && value[index] == '\\'; index--)
            {
                backslashCount++;
            }

            if (backslashCount % 2 == 1)
            {
                break;
            }

            end--;
        }

        return value[..end];
    }

    private static string UnescapePattern(string pattern)
    {
        if (pattern.IndexOf('\\') < 0)
        {
            return pattern;
        }

        var builder = new StringBuilder(pattern.Length);
        var escaped = false;
        foreach (var character in pattern)
        {
            if (escaped)
            {
                builder.Append(character);
                escaped = false;
                continue;
            }

            if (character == '\\')
            {
                escaped = true;
                continue;
            }

            builder.Append(character);
        }

        if (escaped)
        {
            builder.Append('\\');
        }

        return builder.ToString();
    }

    private static string BuildRegexPattern(string pattern, bool matchFromRoot)
    {
        var translated = TranslatePattern(pattern);
        return matchFromRoot
            ? $"^{translated}$"
            : $"(?:^|.*/){translated}$";
    }

    private static string TranslatePattern(string pattern)
    {
        var builder = new StringBuilder(pattern.Length * 2);

        for (var index = 0; index < pattern.Length; index++)
        {
            var character = pattern[index];
            switch (character)
            {
                case '*':
                {
                    var isDoubleStar = index + 1 < pattern.Length && pattern[index + 1] == '*';
                    builder.Append(isDoubleStar ? ".*" : "[^/]*");
                    if (isDoubleStar)
                    {
                        index++;
                    }

                    break;
                }
                case '?':
                    builder.Append("[^/]");
                    break;
                case '[':
                    index = AppendCharacterClass(pattern, index, builder);
                    break;
                case '/':
                    builder.Append('/');
                    break;
                default:
                    builder.Append(Regex.Escape(character.ToString()));
                    break;
            }
        }

        return builder.ToString();
    }

    private static int AppendCharacterClass(string pattern, int startIndex, StringBuilder builder)
    {
        var endIndex = startIndex + 1;
        while (endIndex < pattern.Length && pattern[endIndex] != ']')
        {
            endIndex++;
        }

        if (endIndex >= pattern.Length)
        {
            builder.Append(@"\[");
            return startIndex;
        }

        var contents = pattern[(startIndex + 1)..endIndex];
        if (contents.Length == 0)
        {
            builder.Append(@"\[\]");
            return endIndex;
        }

        builder.Append('[');

        var firstIndex = 0;
        if (contents[0] is '!' or '^')
        {
            builder.Append('^');
            firstIndex = 1;
        }

        for (var index = firstIndex; index < contents.Length; index++)
        {
            var character = contents[index];
            if (character is '\\' or ']')
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        builder.Append(']');
        return endIndex;
    }
}

public sealed class IgnoreRuleLayer
{
    public IgnoreRuleLayer(string sourceFile, string baseDirectory, IReadOnlyList<IgnorePatternRule> rules)
    {
        SourceFile = sourceFile;
        BaseDirectory = FileSystemEntryOperations.NormalizePath(baseDirectory);
        Rules = rules;
    }

    public string SourceFile { get; }

    public string BaseDirectory { get; }

    public IReadOnlyList<IgnorePatternRule> Rules { get; }

    public IgnoreMatch Evaluate(string fullPath, bool isDirectory)
    {
        var relativePath = Path.GetRelativePath(BaseDirectory, fullPath);
        relativePath = NormalizeRelativePath(relativePath);

        if (relativePath.Length == 0 || relativePath == ".." || relativePath.StartsWith("../", StringComparison.Ordinal))
        {
            return IgnoreMatch.None;
        }

        IgnorePatternRule? matchedRule = null;
        foreach (var rule in Rules)
        {
            if (rule.Matches(relativePath, isDirectory))
            {
                matchedRule = rule;
            }
        }

        return matchedRule?.CreateMatch() ?? IgnoreMatch.None;
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        return normalized == "." ? string.Empty : normalized;
    }
}

public sealed class IgnoreRuleStack
{
    private readonly List<IgnoreRuleLayer> _layers = [];

    public int Count => _layers.Count;

    public void Add(IgnoreRuleLayer layer)
    {
        _layers.Add(layer);
    }

    public void RemoveLast(int count)
    {
        if (count <= 0 || _layers.Count == 0)
        {
            return;
        }

        if (count > _layers.Count)
        {
            count = _layers.Count;
        }

        _layers.RemoveRange(_layers.Count - count, count);
    }

    public IgnoreMatch Evaluate(string fullPath, bool isDirectory)
    {
        var normalizedPath = FileSystemEntryOperations.NormalizePath(fullPath);
        var currentMatch = IgnoreMatch.None;

        foreach (var layer in _layers)
        {
            var layerMatch = layer.Evaluate(normalizedPath, isDirectory);
            if (layerMatch.Decision != IgnoreDecision.None)
            {
                currentMatch = layerMatch;
            }
        }

        return currentMatch;
    }
}
