using System;
using System.Collections.Generic;
using System.Linq;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private static uint YmapHash_V22(YmapFile y)
        {
            if (y == null) return 0;
            uint h = y.RpfFileEntry?.ShortNameHash ?? 0;
            if (h == 0 && !string.IsNullOrEmpty(y.Name))
                h = JenkHash.GenHash(System.IO.Path.GetFileNameWithoutExtension(y.Name).ToLowerInvariant());
            return h;
        }

        private int WorldRevertFiles_V22(IEnumerable<YmapFile> filesIn, string why)
        {
            var files = filesIn.Where(y => y != null).Distinct().ToList();
            var hashes = new HashSet<uint>();
            foreach (var y in files) { uint h = YmapHash_V22(y); if (h != 0) hashes.Add(h); }

            int evicted = 0;
            var cache = gameFiles?.Cache;
            if (cache != null)
                foreach (var h in hashes) { try { if (cache.EvictYmap_V21(h)) evicted++; } catch { } }

            if (WorldEdit != null) foreach (var y in files) WorldEdit.ForgetDirty(y);
            WorldEdit?.Deselect();
            worldHoverSel = WorldSelection.Empty;
            WorldHistory?.Clear();

            int dropped = 0, unloaded = 0;
            if (hashes.Count > 0)
            {
                try { dropped = worldRender?.ForgetYmapNames_R2(hashes) ?? 0; } catch { }
                try { unloaded = World?.UnloadYmaps_V22(hashes) ?? 0; } catch { }
            }
            World?.Invalidate();

            if (panel != null)
            {
                panel.WorldDirtyCount = 0;
                panel.WorldEditStatus = hashes.Count > 0
                    ? $"world reset - {hashes.Count} edited .ymap file(s) put back the way the archives have them"
                    : "world reset - nothing had been edited";
            }
            Console.WriteLine($"WORLDRESET ({why}) {hashes.Count} file(s): {evicted} evicted from the cache, " +
                              $"{dropped} entity instance(s) dropped, {unloaded} streamer node(s) unloaded");
            return hashes.Count;
        }

        public int WorldRevertAll_V22()
        {
            var files = new List<YmapFile>();
            if (WorldEdit?.Dirty != null) files.AddRange(WorldEdit.Dirty);

            var p = ProjWin?.Project;
            if (p != null)
            {
                files.AddRange(p.YmapFiles.Where(y => y != null && y.RpfFileEntry != null));
                p.HasChanged = false;
                foreach (var y in p.YmapFiles) if (y != null) y.HasChanged = false;
                foreach (var t in p.YtypFiles) if (t != null) t.HasChanged = false;
                foreach (var f in p.YndFiles) if (f != null) f.HasChanged = false;
                foreach (var f in p.YnvFiles) if (f != null) f.HasChanged = false;
                foreach (var f in p.TrainsFiles) if (f != null) f.HasChanged = false;
                foreach (var f in p.ScenarioFiles) if (f != null) f.HasChanged = false;
                foreach (var f in p.AudioRelFiles) if (f != null) f.HasChanged = false;
                projCtl.CloseProject();
                ProjWin.Visible = false;
            }
            return WorldRevertFiles_V22(files.Distinct(), "revert all");
        }

        private int resetTestStage_V22;
        private uint rtArchA_V22, rtArchB_V22, rtYmapA_V22, rtYmapB_V22;
        private Vector3 rtPosA_V22, rtPosB_V22;
        private int rtFrame_V22;

        partial void OnWorldTick_ResetTest_V22()
        {
            if (resetTestStage_V22 >= 4 || !worldBuilt || screenshotPath == null) return;
            var rtMode = Environment.GetEnvironmentVariable("RLE_RESETTEST");
            if (rtMode != "1" && rtMode != "new") { resetTestStage_V22 = 4; return; }
            rtFrame_V22++;
            if (resetTestStage_V22 < 3) screenshotFrames = Math.Max(screenshotFrames, 30);

            if (resetTestStage_V22 == 0)
            {
                if (rtFrame_V22 < 240) return;
                var cands = World.Visible
                    .Where(e => e?.Ymap?.RpfFileEntry != null && e.Archetype != null && e.MloInstance == null && e.MloParent == null
                                && e._CEntityDef.lodLevel == rage__eLodType.LODTYPES_DEPTH_HD
                                && (e.Position - camera.Position).Length() < 80f)
                    .OrderBy(e => (e.Position - camera.Position).Length())
                    .ToList();
                if (cands.Count < 2) { if (rtFrame_V22 > 900) { Console.WriteLine("RESETTEST FAIL: fewer than two HD entities in view"); resetTestStage_V22 = 4; } return; }
                var a = cands[0]; var b = cands[1];
                rtArchA_V22 = a.Archetype.Hash; rtPosA_V22 = a.Position; rtYmapA_V22 = YmapHash_V22(a.Ymap);
                rtArchB_V22 = b.Archetype.Hash; rtPosB_V22 = b.Position; rtYmapB_V22 = YmapHash_V22(b.Ymap);

                WorldEdit.Select(a);
                WorldEdit.SetPosition(rtPosA_V22 + new Vector3(0, 0, 8f));
                WorldEntityChanged(a);
                WorldEdit.Select(b);
                WorldDeleteSelected();
                int projYmaps = ProjWin.Project?.YmapFiles.Count ?? 0;
                Console.WriteLine($"RESETTEST edited: moved {a.Archetype.Name} from {rtPosA_V22} (+8 m up) in {a.Ymap?.Name}; " +
                                  $"deleted {b.Archetype.Name} at {rtPosB_V22} from {(b.Ymap?.Name ?? "(detached)")}; " +
                                  $"project now holds {projYmaps} ymap(s), dirty {WorldEdit.DirtyCount}");
                resetTestStage_V22 = 1;
                rtFrame_V22 = 0;
                return;
            }

            if (resetTestStage_V22 == 1)
            {
                if (rtFrame_V22 < 60) return;
                bool stillMoved = World.Visible.Any(e => e?.Archetype?.Hash == rtArchA_V22 && (e.Position - (rtPosA_V22 + new Vector3(0, 0, 8f))).Length() < 0.05f);
                bool stillGone = !World.Visible.Any(e => e?.Archetype?.Hash == rtArchB_V22 && (e.Position - rtPosB_V22).Length() < 0.05f);
                Console.WriteLine($"RESETTEST before reset: A drawn at the moved place {stillMoved}, B absent {stillGone}, overrides {(World.ProjectOverrides?.Count ?? 0)}");
                var p = ProjWin.Project;
                if (p != null)
                {
                    p.HasChanged = false;
                    foreach (var y in p.YmapFiles) if (y != null) y.HasChanged = false;
                    foreach (var t in p.YtypFiles) if (t != null) t.HasChanged = false;
                }
                if (rtMode == "new") ProjWin.RequestNewProject = true; else ProjWin.RequestCloseProject = true;
                projCtl.Tick();
                bool gone = rtMode == "new" ? (ProjWin.Project != null && ProjWin.Project.YmapFiles.Count == 0) : ProjWin.Project == null;
                if (gone && WorldEdit.DirtyCount > 0) WorldRevertFiles_V22(WorldEdit.Dirty.ToList(), "leftovers");
                Console.WriteLine($"RESETTEST {(rtMode == "new" ? "new project" : "closed")}: old project {(gone ? "gone" : "STILL THERE")}, overrides {(World.ProjectOverrides?.Count ?? 0)}, dirty {WorldEdit.DirtyCount}");
                resetTestStage_V22 = 2;
                rtFrame_V22 = 0;
                return;
            }

            if (resetTestStage_V22 == 2)
            {
                if (rtFrame_V22 < 180) return;
                bool aBack = World.Visible.Any(e => e?.Archetype?.Hash == rtArchA_V22 && (e.Position - rtPosA_V22).Length() < 0.05f);
                bool aStillMoved = World.Visible.Any(e => e?.Archetype?.Hash == rtArchA_V22 && (e.Position - (rtPosA_V22 + new Vector3(0, 0, 8f))).Length() < 0.05f);
                bool bBack = World.Visible.Any(e => e?.Archetype?.Hash == rtArchB_V22 && (e.Position - rtPosB_V22).Length() < 0.05f);
                var ya = gameFiles?.Cache?.GetYmap(rtYmapA_V22);
                bool fileA = ya?.AllEntities?.Any(e => e?.Archetype?.Hash == rtArchA_V22 && (e.Position - rtPosA_V22).Length() < 0.05f) == true;
                var yb = gameFiles?.Cache?.GetYmap(rtYmapB_V22);
                bool fileB = yb?.AllEntities?.Any(e => e?.Archetype?.Hash == rtArchB_V22 && (e.Position - rtPosB_V22).Length() < 0.05f) == true;
                Console.WriteLine($"RESETTEST {(aBack && !aStillMoved && bBack ? "OK" : "FAIL")}: moved entity back at its place {aBack} (still at the moved place {aStillMoved}); " +
                                  $"deleted entity back {bBack}; in the re-read files: A {fileA}, B {fileB}");
                resetTestStage_V22 = 4;
                screenshotFrames = 8;
                return;
            }
        }
    }
}

