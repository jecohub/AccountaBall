using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using AccountaBall.Core.Models;

namespace AccountaBall.Core.Util;

/// Render a FreeBallRecap to a self-contained Markdown document. Sections with no
/// items are omitted. Port of Swift `FreeBallMarkdown`.
public static class FreeBallMarkdown
{
    public static string Render(FreeBallRecap r)
    {
        var date = r.Date.ToString("MMM d, yyyy h:mm tt", CultureInfo.InvariantCulture);
        var mins = (int)Math.Round(r.Duration / 60.0, MidpointRounding.AwayFromZero);
        var sb = new StringBuilder();
        sb.Append($"# FreeBall — {date}\n\n_{mins}m session_\n");
        if (!string.IsNullOrEmpty(r.Narrative)) sb.Append($"\n{r.Narrative}\n");
        if (r.Categories.Count > 0)
        {
            sb.Append("\n## Time\n");
            foreach (var c in r.Categories.OrderByDescending(c => c.Minutes))
                sb.Append($"- {c.Label}: {c.Minutes}m\n");
        }

        void Section(string title, IReadOnlyList<string> items)
        {
            if (items.Count == 0) return;
            sb.Append($"\n## {title}\n");
            foreach (var i in items) sb.Append($"- {i}\n");
        }

        Section("Working on", r.WorkingOn);
        Section("People & conversations", r.People);
        Section("Code context", r.CodeContext);
        Section("Open threads", r.OpenThreads);
        if (!string.IsNullOrEmpty(r.Insight)) sb.Append($"\n## Insight\n{r.Insight}\n");
        return sb.ToString();
    }
}
