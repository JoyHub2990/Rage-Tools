using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;

namespace RageLightEditor.Editor
{
    public partial class ProjectController
    {
        public static (int connected, int stillMissing, List<string> loadedYtyps)
            ReconnectArchetypes_V67(CwProject p, GameFileCache cache, IEnumerable<string> extraDirs)
        {
            var loadedYtyps = new List<string>();
            if (p == null) return (0, 0, loadedYtyps);

            HashSet<uint> Missing()
            {
                var missing = new HashSet<uint>();
                foreach (var y in p.YmapFiles)
                {
                    var all = y?.AllEntities;
                    if (all == null) continue;
                    foreach (var e in all)
                        if (e != null && e.Archetype == null) missing.Add(e._CEntityDef.archetypeName.Hash);
                }
                return missing;
            }

            var need = Missing();
            if (need.Count == 0) return (0, 0, loadedYtyps);

            var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in extraDirs ?? Enumerable.Empty<string>())
                if (!string.IsNullOrEmpty(d) && Directory.Exists(d)) dirs.Add(d);
            foreach (var y in p.YmapFiles)
            {
                var d = Path.GetDirectoryName(y?.FilePath ?? "");
                if (!string.IsNullOrEmpty(d) && Directory.Exists(d)) dirs.Add(d);
            }

            var have = new HashSet<string>(p.YtypFiles.Where(t => t?.FilePath != null)
                                                      .Select(t => Path.GetFullPath(t.FilePath)),
                                           StringComparer.OrdinalIgnoreCase);
            foreach (var dir in dirs)
            {
                foreach (var file in Directory.GetFiles(dir, "*.ytyp"))
                {
                    if (have.Contains(Path.GetFullPath(file))) continue;
                    YtypFile probe = null;
                    try
                    {
                        probe = new YtypFile();
                        probe.Load(File.ReadAllBytes(file));
                    }
                    catch { continue; }
                    bool defines = probe.AllArchetypes?.Any(a => a != null && need.Contains(a.Hash)) == true;
                    if (!defines) continue;
                    var t = p.AddYtypFile(file);
                    if (t == null) continue;
                    t.Load(File.ReadAllBytes(file));
                    t.FilePath = file;
                    t.RpfFileEntry ??= new RpfResourceFileEntry();
                    t.RpfFileEntry.Name = Path.GetFileName(file);
                    t.Name = t.RpfFileEntry.Name;
                    t.Loaded = true;
                    have.Add(Path.GetFullPath(file));
                    loadedYtyps.Add(Path.GetFileName(file));
                }
            }

            foreach (var y in p.YmapFiles)
            {
                if (y == null) continue;
                try { p.InitYmapArchetypes(y, cache); } catch { }
            }

            int after = Missing().Count;
            return (need.Count - after, after, loadedYtyps);
        }

        public void ReconnectProjectArchetypes_V67(IEnumerable<string> batchFiles)
        {
            var p = win.Project;
            if (p == null) return;
            var dirs = (batchFiles ?? Enumerable.Empty<string>())
                       .Select(f => { try { return Path.GetDirectoryName(f); } catch { return null; } })
                       .Where(d => !string.IsNullOrEmpty(d));
            var (connected, missing, loaded) = ReconnectArchetypes_V67(p, cache(), dirs);
            if (loaded.Count > 0)
                win.Status = $"pulled in {string.Join(", ", loaded)} beside the ymap - {connected} archetype(s) connected" +
                             (missing > 0 ? $"; {missing} still have no .ytyp" : "");
            else if (missing > 0)
                win.Status = $"{missing} archetype(s) are not in the game or the project - open the .ytyp that defines them";
            if (loaded.Count > 0) ProjectYmapsChanged?.Invoke();
        }
    }
}
