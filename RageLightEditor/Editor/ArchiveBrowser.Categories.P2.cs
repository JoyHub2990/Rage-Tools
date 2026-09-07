using System;
using System.Collections.Generic;

namespace RageLightEditor.Editor
{
    public partial class ArchiveBrowser
    {
        public int FindByCategory_P2(string query, IReadOnlyList<string> extensions, int category,
                                     Func<string, string, int> classify, List<Entry> into, int max)
        {
            if (into == null || classify == null) return 0;
            if (!Ready) return 0;
            query = (query ?? "").Trim().ToLowerInvariant();
            int total = 0;
            foreach (var e in index)
            {
                bool extOk = extensions == null || extensions.Count == 0;
                if (!extOk)
                    for (int i = 0; i < extensions.Count; i++)
                        if (e.NameLower.EndsWith(extensions[i], StringComparison.Ordinal)) { extOk = true; break; }
                if (!extOk) continue;
                if (query.Length > 0 && e.NameLower.IndexOf(query, StringComparison.Ordinal) < 0) continue;
                var n = e.NameLower;
                int dot = n.LastIndexOf('.');
                if (dot > 0) n = n.Substring(0, dot);
                if (classify(n, e.Path) != category) continue;
                total++;
                if (into.Count < max) into.Add(e);
            }
            return total;
        }
    }
}

