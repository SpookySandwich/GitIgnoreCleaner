namespace GitIgnoreCleaner.Services;

public static class IgnoreFileParser
{
    public static List<IgnoreRule> ParseFile(string filePath, string baseDirectory)
    {
        var rules = new List<IgnoreRule>();

        foreach (var rawLine in File.ReadLines(filePath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var isNegation = false;
            if (line.StartsWith("\\#", StringComparison.Ordinal) || line.StartsWith("\\!", StringComparison.Ordinal))
            {
                line = line[1..];
            }
            else if (line.StartsWith("!", StringComparison.Ordinal))
            {
                isNegation = true;
                line = line[1..];
            }

            var directoryOnly = line.EndsWith("/", StringComparison.Ordinal);
            if (directoryOnly)
            {
                line = line.TrimEnd('/');
            }

            var anchored = line.StartsWith("/", StringComparison.Ordinal);
            if (anchored)
            {
                line = line.TrimStart('/');
            }

            if (line.Length == 0)
            {
                continue;
            }

            rules.Add(new IgnoreRule(line, baseDirectory, filePath, isNegation, directoryOnly, anchored));
        }

        return rules;
    }
}

