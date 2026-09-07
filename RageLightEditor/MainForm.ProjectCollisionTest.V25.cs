using System;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private void SeqTest_ProjectCollision_V25(Action<string, bool, string> check)
        {
            var c = gameFiles?.Cache;
            if (c?.YbnDict == null || !gameFiles.Ready) { Console.WriteLine("  v25 collision: (skipped - no game folder)"); return; }

            var src = c.YbnDict.Values.FirstOrDefault(e => e != null && (e.Name ?? "").StartsWith("hei_", StringComparison.OrdinalIgnoreCase))
                   ?? c.YbnDict.Values.FirstOrDefault(e => e != null);
            if (src == null) { check("v25 collision: a game .ybn to start from", false, "none"); return; }

            string tmp = Path.Combine(Path.GetTempPath(), "rle_v25_collision");
            Directory.CreateDirectory(tmp);
            string path = Path.Combine(tmp, "v25_custom_collision.ybn");
            var view = new CollisionView(gameFiles);
            try
            {
                File.WriteAllBytes(path, ArchiveBrowser.ExtractForDisk(src));
                var p = new CwProject { Name = "v25", HasChanged = true };
                var ybn = p.LoadYbn(path, out var problem);
                check("v25 collision: a .ybn added to the project is LOADED, not just named",
                      ybn != null && ybn.Bounds != null && p.YbnFiles.Count == 1, problem ?? $"{p.YbnFiles.Count} loaded, bounds {(ybn?.Bounds != null ? "read" : "none")}");
                if (ybn == null) return;
                uint hash = ybn.RpfFileEntry.ShortNameHash;
                check("v25 collision: ...under its own name, which no game file has", !c.YbnDict.ContainsKey(hash), "not in the archives");

                view.SetProjectYbns_V25(p.YbnFiles);
                var bounds = view.ProjectBounds_V25();
                check("v25 collision: the project's file joins the world's wanted list at its own bounds",
                      bounds.Count == 1 && bounds[0].Hash == hash && bounds[0].Box.Maximum.X > bounds[0].Box.Minimum.X,
                      bounds.Count == 1 ? $"box {bounds[0].Box.Minimum} .. {bounds[0].Box.Maximum}" : bounds.Count + " bound(s)");

                var mesh = view.GetImmediate(hash);
                check("v25 collision: ...and the collision view builds it from the project's copy",
                      mesh != null && mesh.TriangleCount > 0, mesh == null ? "no mesh" : $"{mesh.TriangleCount:N0} triangles, '{mesh.Name}'");

                view.SetProjectYbns_V25(null);
                var gone = view.GetImmediate(hash);
                check("v25 collision: closing the project takes it away again", gone == null, gone == null ? "gone" : "still drawn");
            }
            catch (Exception ex) { check("v25 collision: the fixture could be written and read", false, ex.Message); }
            finally
            {
                try { view.Dispose(); } catch { }
                try { File.Delete(path); } catch { }
            }
        }
    }
}

