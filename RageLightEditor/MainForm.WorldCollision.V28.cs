using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private void PushProjectYbns_V25()
        {
            var p = ProjWin?.Project;
            collisionView?.SetProjectYbns_V25(p != null && ProjWin.RenderProjectItems ? p.YbnFiles : null);
        }

        private CollisionView EnsureWorldCollisionView_V28()
        {
            if (collisionView == null)
            {
                collisionView = new CollisionView(gameFiles);
                PushProjectYbns_V25();
            }
            return collisionView;
        }

        private void ResetWorldCollision_V28()
        {
            var v = collisionView;
            collisionView = null;
            if (v != null)
            {
                try { v.Dispose(); } catch { }
                while (v.Retired.TryDequeue(out var m)) { try { m.ReleaseVB(); } catch { } }
            }
            ybnIndex = null;
            collisionWanted.Clear();
            collisionWantedAt = new SharpDX.Vector3(1e9f);
            collisionProjectVersion_V25 = -1;
            collisionDraw.Clear();
        }

        private HashSet<uint> PickProjectCollision_V28(ref SharpDX.Ray ray, ref CodeWalker.World.SpaceRayIntersectResult res)
        {
            var p = ProjWin?.Project;
            if (p == null || !ProjWin.RenderProjectItems || p.YbnFiles.Count == 0) return null;
            HashSet<uint> covered = null;
            foreach (var ybn in p.YbnFiles)
            {
                var bd = ybn?.Bounds;
                var fe = ybn?.RpfFileEntry;
                if (bd == null || fe == null) continue;
                covered ??= new HashSet<uint>();
                covered.Add(fe.ShortNameHash);
                var mn = bd.BoxMin; var mx = bd.BoxMax;
                if (mx.X > mn.X && mx.Y > mn.Y)
                {
                    var bb = new SharpDX.BoundingBox(mn, mx);
                    if (!ray.Intersects(ref bb, out float boxDist) || boxDist > res.HitDist) continue;
                }
                var bhit = bd.RayIntersect(ref ray, res.HitDist);
                if (bhit.Hit && bhit.HitDist < res.HitDist)
                {
                    bhit.HitYbn = ybn;
                    res.TryUpdate(ref bhit);
                }
            }
            return covered;
        }

        private void SeqTest_WorldCollision_V28(Action<string, bool, string> check)
        {
            var c = gameFiles?.Cache;
            if (c?.YbnDict == null || !gameFiles.Ready) { Console.WriteLine("  v28 collision: (skipped - no game folder)"); return; }

            var src = c.YbnDict.Values.FirstOrDefault(e => e != null && (e.Name ?? "").StartsWith("hei_", StringComparison.OrdinalIgnoreCase))
                   ?? c.YbnDict.Values.FirstOrDefault(e => e != null);
            if (src == null) { check("v28 collision: a game .ybn to start from", false, "none"); return; }

            string dir = Path.Combine(Path.GetTempPath(), "rle_v28_projcol");
            var prevProject = ProjWin.Project;
            bool prevRender = ProjWin.RenderProjectItems;
            try
            {
                Directory.CreateDirectory(dir);
                var bytes = ArchiveBrowser.ExtractForDisk(src);
                string customPath = Path.Combine(dir, "v28_custom_area.ybn");
                string editedPath = Path.Combine(dir, src.Name);
                File.WriteAllBytes(customPath, bytes);
                File.WriteAllBytes(editedPath, bytes);

                ResetWorldCollision_V28();

                ProjWin.Project = new CwProject { Name = "v28", HasChanged = false };
                ProjWin.RenderProjectItems = true;
                int added = projCtl.AddFilesToProject(new[] { customPath, editedPath }, quiet: true);
                var p = ProjWin.Project;
                check("v28 collision: importing a map folder LOADS its .ybn files into the project",
                      added == 2 && p != null && p.YbnFiles.Count == 2,
                      $"{added} added, {p?.YbnFiles.Count ?? 0} loaded");

                var view = EnsureWorldCollisionView_V28();
                check("v28 collision: the view born AFTER the import still knows the project's files",
                      view.HasProjectYbns_V25, view.HasProjectYbns_V25 ? "it does" : "it does not - the import was lost");

                uint customHash = JenkHash.GenHash("v28_custom_area");
                var mesh = view.GetImmediate(customHash);
                check("v28 collision: their custom collision draws, though no cache.dat lists it",
                      mesh != null && mesh.TriangleCount > 0, mesh == null ? "no mesh" : $"{mesh.TriangleCount:N0} triangles");
                check("v28 collision: both files join the wanted list at their own bounds",
                      view.ProjectBounds_V25().Count == 2, view.ProjectBounds_V25().Count + " bound(s)");

                var custom = p.YbnFiles.First(b => b.RpfFileEntry.ShortNameHash == customHash);
                var edited = p.YbnFiles.First(b => !ReferenceEquals(b, custom));
                var cb = custom.Bounds;
                var mid = (cb.BoxMin + cb.BoxMax) * 0.5f;
                var ray = new SharpDX.Ray(new SharpDX.Vector3(mid.X, mid.Y, cb.BoxMax.Z + 50f), -SharpDX.Vector3.UnitZ);
                var hit = WorldSelection.Empty;
                PickCollision(ref ray, ray.Position, ref hit, allowLoad: true);
                var hitYbn = hit.CollisionBounds?.GetRootYbn();
                check("v28 collision: a click on their custom collision selects it, though no cache.dat lists it",
                      hit.CollisionBounds != null && ReferenceEquals(hitYbn, custom) ,
                      hit.CollisionBounds == null ? "nothing hit" : $"hit {hitYbn?.Name ?? "?"} at {hit.HitDist:0.##} m");

                uint gameHash = edited.RpfFileEntry.ShortNameHash;
                var eb = edited.Bounds;
                ybnIndex = new System.Collections.Generic.List<(uint, SharpDX.BoundingBox)>
                {
                    (gameHash, new SharpDX.BoundingBox(eb.BoxMin, eb.BoxMax)),
                };
                var emid = (eb.BoxMin + eb.BoxMax) * 0.5f;
                var ray2 = new SharpDX.Ray(new SharpDX.Vector3(emid.X, emid.Y, eb.BoxMax.Z + 50f), -SharpDX.Vector3.UnitZ);
                p.YbnFiles.Remove(custom);
                var hit2 = WorldSelection.Empty;
                try { PickCollision(ref ray2, ray2.Position, ref hit2, allowLoad: true); }
                finally { p.YbnFiles.Add(custom); }
                var hit2Ybn = hit2.CollisionBounds?.GetRootYbn();
                var gameCopy = c.GetYbn(gameHash);
                check("v28 collision: a click on their edited copy of a game file selects THEIR copy, not the game's",
                      hit2.CollisionBounds != null && ReferenceEquals(hit2Ybn, edited) && !ReferenceEquals(hit2Ybn, gameCopy),
                      hit2.CollisionBounds == null ? "nothing hit"
                      : ReferenceEquals(hit2Ybn, edited) ? "the project's copy"
                      : ReferenceEquals(hit2Ybn, gameCopy) ? "the game's - the old one"
                      : $"something else: {hit2Ybn?.Name ?? "(no file)"}");

                ResetWorldCollision_V28();
                check("v28 collision: a mods/DLC reload drops the stale index and meshes",
                      collisionView == null && ybnIndex == null, "reset");
                view = EnsureWorldCollisionView_V28();
                check("v28 collision: ...and the view reborn after the reload knows the project again",
                      view.HasProjectYbns_V25 && view.GetImmediate(customHash) != null, "still drawn");

                p.YbnFilenames.RemoveAll(n => n.IndexOf("v28_custom_area", StringComparison.OrdinalIgnoreCase) >= 0);
                bool dropped = p.SyncYbnFiles();
                PushProjectYbns_V25();
                check("v28 collision: removing the row from the project takes the loaded file with it",
                      dropped && p.YbnFiles.Count == 1 && view.GetImmediate(customHash) == null,
                      $"{p.YbnFiles.Count} left, custom {(view.GetImmediate(customHash) == null ? "gone" : "still drawn")}");
            }
            catch (Exception ex) { check("v28 collision: the fixture could be written and read", false, ex.Message); }
            finally
            {
                ProjWin.Project = prevProject;
                ProjWin.RenderProjectItems = prevRender;
                RebuildProjectOverrides();
                ResetWorldCollision_V28();
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}

