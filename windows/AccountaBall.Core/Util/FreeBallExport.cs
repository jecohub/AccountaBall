using System;
using System.Globalization;
using System.IO;
using AccountaBall.Core.Models;

namespace AccountaBall.Core.Util;

/// FreeBall recap export. Port of Swift `FreeBallExport`. The filename + Markdown
/// write are portable here; the macOS "reveal in Finder" (NSWorkspace) maps to the
/// Windows "reveal in Explorer" in the Platform/App layer.
public static class FreeBallExport
{
    public static string Filename(DateTimeOffset date)
        => $"freeball-{date.ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture)}.md";

    /// Write the recap's Markdown into <directory>/AccountaBall/FreeBall/<file>.md
    /// and return the path (or null on failure). Revealing it in the file manager
    /// is the Platform layer's job.
    public static string? Export(FreeBallRecap recap, string baseDirectory)
    {
        try
        {
            var dir = Path.Combine(baseDirectory, "AccountaBall", "FreeBall");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, Filename(recap.Date));
            File.WriteAllText(path, FreeBallMarkdown.Render(recap));
            return path;
        }
        catch
        {
            return null;
        }
    }
}
