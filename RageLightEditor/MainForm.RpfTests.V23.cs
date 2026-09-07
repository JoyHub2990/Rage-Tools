using System;
using System.Collections.Generic;
using System.Linq;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private void SeqTest_RpfV23(Action<string, bool, string> check)
        {
            var c = gameFiles?.Cache;
            if (c?.RpfMan == null || !gameFiles.Ready) { Console.WriteLine("  v23 rpf: (skipped - no game folder)"); return; }

            RpfFileEntry jbib = null;
            foreach (var rpf in c.RpfMan.AllRpfs)
            {
                if (rpf?.AllEntries == null) continue;
                foreach (var e in rpf.AllEntries)
                {
                    if (e is RpfFileEntry fe && string.Equals(fe.Name, "jbib_000_u.ydd", StringComparison.OrdinalIgnoreCase) &&
                        (fe.Path ?? "").IndexOf("mp_m_freemode_01", StringComparison.OrdinalIgnoreCase) >= 0)
                    { jbib = fe; break; }
                }
                if (jbib != null) break;
            }
            if (jbib == null) check("v23 variants: a freemode torso is in the archives", false, "mp_m_freemode_01\\jbib_000_u.ydd not found");
            else
            {
                var pv = new AssetPreview(gameFiles, modelRenderer, textureLoader);
                try
                {
                    bool opened = pv.Open(jbib);
                    check("v23 variants: the torso opens and its lettered sets are found beside it",
                          opened && pv.TextureVariants.Count > 1 && pv.TextureVariants.Contains('a'),
                          opened ? "letters: " + string.Join(" ", pv.TextureVariants) : pv.Error);
                    var authored = pv.Model?.Meshes.FirstOrDefault(m => m?.DiffuseName != null)?.DiffuseName;
                    char other = pv.TextureVariants.FirstOrDefault(v => v != pv.AuthoredVariant);
                    if (other != '\0')
                    {
                        pv.SetTextureVariant(other);
                        var now = pv.Model?.Meshes.FirstOrDefault(m => m?.DiffuseName != null);
                        bool renamed = now?.DiffuseName != null && now.DiffuseName != authored && now.DiffuseName.Contains("_" + other + "_");
                        check($"v23 variants: choosing '{other}' rebuilds with that set's texture",
                              renamed && now.DiffuseSRV != null,
                              $"was {authored}, now {now?.DiffuseName} ({(now?.DiffuseSRV != null ? "resolved" : "NOT resolved")})");
                        pv.SetTextureVariant(pv.AuthoredVariant);
                        var back = pv.Model?.Meshes.FirstOrDefault(m => m?.DiffuseName != null)?.DiffuseName;
                        check("v23 variants: ...and the file's own letter puts it back", back == authored, back ?? "(none)");
                    }
                    int before = pv.MissingTextures.Length;
                    pv.RefreshTextures();
                    check("v23 refresh: re-resolving every slot leaves the missing list a fresh count, not a stale one",
                          pv.MissingTextures.Length <= before, $"{before} -> {pv.MissingTextures.Length}");
                }
                finally { pv.Dispose(); }
            }

            var ab = panel?.Archive;
            if (ab == null) { Console.WriteLine("  v23 scope: (skipped - no archive browser)"); return; }
            if (!ab.Ready)
            {
                try { ab.Build(c.RpfMan); } catch (Exception ex) { check("v23 scope: the archive index builds", false, ex.Message); return; }
            }
            var hits = new List<ArchiveBrowser.Entry>();
            int all = ab.Find("mapdetail", null, hits, 50, null);
            int inA = ab.Find("mapdetail", null, hits, 50, "x64a.rpf\\textures");
            bool allUnder = hits.All(h => h.Path.StartsWith("x64a.rpf\\textures\\", StringComparison.OrdinalIgnoreCase));
            int inB = ab.Find("mapdetail", null, hits, 50, "x64b.rpf");
            check("v23 scope: a search under x64a.rpf\\textures finds mapdetail and nothing outside the branch",
                  inA >= 1 && allUnder && inA <= all, $"{inA} under the branch (all: {all})");
            check("v23 scope: the same search under x64b.rpf finds nothing, because it is not there", inB == 0, inB + " hit(s)");
        }
    }
}

