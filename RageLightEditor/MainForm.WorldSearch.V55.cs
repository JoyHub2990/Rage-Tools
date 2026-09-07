using System;
using System.Collections.Generic;
using System.Linq;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private double worldFindAt_V55 = -10;
        private bool worldFindEnvDone_V55;

        private void ServiceWorldFind_V55(double now)
        {
            var p = panel;
            if (p == null) return;
            ServiceWorldFindEnv_V55();

            if (p.WorldFindQuery_V55 != null && now - worldFindAt_V55 > 0.25)
            {
                var q = p.WorldFindQuery_V55.Trim();
                p.WorldFindQuery_V55 = null;
                worldFindAt_V55 = now;
                RunWorldFind_V55(q);
            }
            if (p.RequestWorldFindGoto_V55 >= 0)
            {
                int i = p.RequestWorldFindGoto_V55;
                p.RequestWorldFindGoto_V55 = -1;
                if (i < p.WorldFindHits_V55.Count) WorldFindGoto_V55(p.WorldFindHits_V55[i]);
            }
            if (p.RequestWorldFindView_V55 >= 0)
            {
                int i = p.RequestWorldFindView_V55;
                p.RequestWorldFindView_V55 = -1;
                if (i < p.WorldFindHits_V55.Count) WorldFindView_V55(p.WorldFindHits_V55[i]);
            }
        }

        private void RunWorldFind_V55(string q)
        {
            var p = panel;
            p.WorldFindHits_V55.Clear();
            p.WorldFindTotal_V55 = 0;
            if (string.IsNullOrEmpty(q)) { p.WorldFindStatus_V55 = ""; return; }
            if (q.Length < 2) { p.WorldFindStatus_V55 = "type at least 2 characters"; return; }
            if (World?.ResidentYmaps == null)
            {
                p.WorldFindStatus_V55 = "the world is not loaded yet";
                return;
            }

            var cam = camera.Position;
            var found = new List<LightPanel.WorldFindHit_V55>(512);
            int total = 0;

            void Consider(YmapEntityDef e, string where, bool inMlo)
            {
                if (e == null) return;
                var name = e.Archetype?.Name ?? e._CEntityDef.archetypeName.ToString();
                if (string.IsNullOrEmpty(name) ||
                    name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) return;
                total++;
                if (found.Count >= 4000) return;
                var pos = e.Position;
                found.Add(new LightPanel.WorldFindHit_V55
                {
                    Name = name,
                    Pos = new System.Numerics.Vector3(pos.X, pos.Y, pos.Z),
                    Dist = (pos - cam).Length(),
                    Where = where,
                    Entity = e,
                    InMlo = inMlo,
                });
            }

            foreach (var y in World.ResidentYmaps)
            {
                var ents = y?.AllEntities;
                if (ents == null) continue;
                var ymapName = y.RpfFileEntry?.Name ?? y.Name ?? "ymap";
                foreach (var e in ents)
                {
                    Consider(e, "in " + ymapName, false);
                    var kids = e?.MloInstance?.Entities;
                    if (kids == null) continue;
                    var mloName = e.Archetype?.Name ?? "an interior";
                    foreach (var k in kids) Consider(k, $"inside {mloName} (MLO), {ymapName}", true);
                }
            }

            found.Sort((a, b) => a.Dist.CompareTo(b.Dist));
            for (int i = 0; i < found.Count && i < 100; i++) p.WorldFindHits_V55.Add(found[i]);
            p.WorldFindTotal_V55 = total;
            p.WorldFindStatus_V55 = total == 0
                ? $"nothing loaded is called \"{q}\""
                : total > p.WorldFindHits_V55.Count
                    ? $"{total:N0} matches - showing the nearest {p.WorldFindHits_V55.Count}"
                    : $"{total:N0} match{(total == 1 ? "" : "es")}";
        }

        private void WorldFindGoto_V55(in LightPanel.WorldFindHit_V55 hit)
        {
            if (hit.Entity == null) return;
            WorldEdit.Select(hit.Entity);
            panel.RequestFrameWorldSelection_M3();
            panel.WorldFindStatus_V55 = "flying to " + hit.Name;
            Console.WriteLine($"WORLDFIND goto {hit.Name} at {hit.Pos.X:0.#},{hit.Pos.Y:0.#},{hit.Pos.Z:0.#} ({hit.Where})");
        }

        private void WorldFindView_V55(in LightPanel.WorldFindHit_V55 hit)
        {
            if (panel.Archive == null || !panel.Archive.Ready)
            {
                panel.WorldFindStatus_V55 = "the archives are not open yet";
                return;
            }
            var name = hit.Name;
            var hits = new List<ArchiveBrowser.Entry>();
            panel.Archive.Find(name, new[] { ".ydr", ".yft", ".ydd" }, hits, 12);
            RpfFileEntry pick = null;
            foreach (var h in hits)
            {
                var f = h.File;
                if (f == null) continue;
                var stem = System.IO.Path.GetFileNameWithoutExtension(f.NameLower ?? "");
                if (string.Equals(stem, name, StringComparison.OrdinalIgnoreCase)) { pick = f; break; }
                pick ??= f;
            }
            if (pick == null)
            {
                panel.WorldFindStatus_V55 = "no .ydr/.yft named " + name + " in the archives";
                return;
            }
            panel.RequestRpfView = pick;
            panel.WorldFindStatus_V55 = "opening " + pick.Name + " in the model viewer";
            Console.WriteLine($"WORLDFIND view {pick.Path}");
        }

        private int worldFindEnvWait_V55;

        private void ServiceWorldFindEnv_V55()
        {
            if (worldFindEnvDone_V55) return;
            var spec = Environment.GetEnvironmentVariable("RLE_WORLDFIND");
            if (string.IsNullOrWhiteSpace(spec)) { worldFindEnvDone_V55 = true; return; }
            if (!panel.WorldMode || World?.ResidentYmaps == null || !World.ResidentYmaps.Any()) return;
            worldFindEnvWait_V55++;
            if (worldFindEnvWait_V55 % 30 != 0) { if (screenshotPath != null) screenshotFrames = Math.Max(screenshotFrames, 3); return; }
            var probe = spec.Split('|')[0].Trim();
            RunWorldFind_V55(probe);
            if (panel.WorldFindTotal_V55 == 0 && worldFindEnvWait_V55 < 900)
            {
                if (screenshotPath != null) screenshotFrames = Math.Max(screenshotFrames, 3);
                return;
            }
            worldFindEnvDone_V55 = true;
            var parts = spec.Split('|');
            panel.WorldSearchOpen_V56 = true;
            RunWorldFind_V55(parts[0].Trim());
            Console.WriteLine($"WORLDFIND \"{parts[0].Trim()}\": {panel.WorldFindTotal_V55} total, " +
                              $"{panel.WorldFindHits_V55.Count} shown");
            foreach (var h in panel.WorldFindHits_V55.Take(5))
                Console.WriteLine($"WORLDFIND   {h.Name}  {h.Dist:0} m  {h.Where}");
            if (parts.Length > 1 && parts[1] == "goto" && panel.WorldFindHits_V55.Count > 0)
                WorldFindGoto_V55(panel.WorldFindHits_V55[0]);
            if (screenshotPath != null) screenshotFrames = Math.Max(screenshotFrames, 6);
        }
    }
}
