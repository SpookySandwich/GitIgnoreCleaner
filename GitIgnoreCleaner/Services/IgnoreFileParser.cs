using System.Collections.Generic;
using System.IO;

namespace GitIgnoreCleaner.Services;

public static class IgnoreFileParser
{
    // Returns the wrapper containing the parsed rules
    public static IgnoreListWrapper ParseFile(string filePath)
    {
        var lines = new List<string>();
      
        // Basic file reading, the library will handle # comments and ! negations
        foreach (var line in File.ReadLines(filePath))
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                lines.Add(line);
            }
        }

        return new IgnoreListWrapper(lines, filePath);
    }
}

