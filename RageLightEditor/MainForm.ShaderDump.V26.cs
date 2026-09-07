using System;
using System.Collections.Generic;
using System.Linq;
using CodeWalker.GameFiles;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private bool shaderDumpDone_V26;

        partial void OnWorldTick_ShaderDump_V26()
        {
            if (shaderDumpDone_V26) return;
            var want = Environment.GetEnvironmentVariable("RLE_SHADERDUMP");
            if (string.IsNullOrEmpty(want)) return;
            var c = gameFiles?.Cache;
            if (c?.YftDict == null || !gameFiles.Ready) return;
            shaderDumpDone_V26 = true;

            var names = want.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            var found = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            void Take(ShaderFX sh)
            {
                var nm = sh?.Name.ToString();
                if (nm == null || found.ContainsKey(nm)) return;
                if (!names.Any(w => nm.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)) return;
                var ps = sh.ParametersList?.Parameters; var hs = sh.ParametersList?.Hashes;
                if (ps == null || hs == null) return;
                var list = new List<string>();
                for (int i = 0; i < ps.Length && i < hs.Length; i++)
                {
                    string val = "";
                    if (ps[i].DataType != 0)
                    {
                        if (ps[i].Data is SharpDX.Vector4 v4)
                            val = $"  = {v4.X:0.####}, {v4.Y:0.####}, {v4.Z:0.####}, {v4.W:0.####}";
                        else if (ps[i].Data is SharpDX.Vector4[] va && va.Length > 0)
                            val = $"  = [{va.Length}] {va[0].X:0.####}, {va[0].Y:0.####}, {va[0].Z:0.####}, {va[0].W:0.####}";
                    }
                    else
                    {
                        var tb = ps[i].Data as TextureBase;
                        val = "  = " + (tb?.Name ?? "(none)");
                        if (tb is Texture rt) val += $"  [{rt.Width}x{rt.Height} {rt.Format}, {rt.Levels} mip(s)]";
                        else if (tb != null) val += "  [reference only - lives in a .ytd]";
                    }
                    list.Add((ps[i].DataType == 0 ? "[tex] " : "      ") + (ShaderParamNames)hs[i] + val);
                }
                found[nm] = list;
            }

            foreach (var kv in c.YftDict.ToList())
            {
                if (found.Count >= names.Count * 3) break;
                try
                {
                    var f = c.RpfMan.GetFile<YftFile>(kv.Value);
                    var shaders = f?.Fragment?.Drawable?.ShaderGroup?.Shaders?.data_items;
                    if (shaders != null) foreach (var sh in shaders) Take(sh);
                }
                catch { }
            }
            foreach (var kv in c.YdrDict.ToList())
            {
                if (found.Count >= names.Count * 3) break;
                try
                {
                    var d = c.RpfMan.GetFile<YdrFile>(kv.Value);
                    var shaders = d?.Drawable?.ShaderGroup?.Shaders?.data_items;
                    if (shaders != null) foreach (var sh in shaders) Take(sh);
                } catch { }
            }
            foreach (var kv in (c.YddDict ?? new System.Collections.Generic.Dictionary<uint, RpfFileEntry>()).ToList())
            {
                if (found.Count >= names.Count * 3) break;
                try
                {
                    var dd = c.RpfMan.GetFile<YddFile>(kv.Value);
                    if (dd?.Drawables == null) continue;
                    foreach (var dr in dd.Drawables)
                    {
                        var shaders = dr?.ShaderGroup?.Shaders?.data_items;
                        if (shaders == null) continue;
                        foreach (var sh in shaders)
                        {
                            int before = found.Count;
                            Take(sh);
                            if (found.Count > before)
                                Console.WriteLine($"SHADERDUMP   ...found on {dr.Name ?? "?"} in {kv.Value.Name}");
                        }
                    }
                } catch { }
            }

            foreach (var kv in found.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine($"SHADERDUMP ===== {kv.Key} : {kv.Value.Count} parameter(s) IN THE GAME'S FILE =====");
                foreach (var l in kv.Value) Console.WriteLine("SHADERDUMP   " + l);

                var t = Editor.ShaderPresets.Template(kv.Key);
                if (t == null) { Console.WriteLine("SHADERDUMP   (no template)"); continue; }
                Console.WriteLine($"SHADERDUMP   ---- the editor's template: {t.Params.Count} parameter(s), harvested={t.Harvested} ----");
                var mine = new HashSet<string>(t.Params.Select(p => ((ShaderParamNames)p.Hash).ToString()), StringComparer.OrdinalIgnoreCase);
                var theirs = new HashSet<string>(kv.Value.Select(l => l.Trim()), StringComparer.OrdinalIgnoreCase);
                var missing = theirs.Where(x => !mine.Contains(x)).ToList();
                var extra = mine.Where(x => !theirs.Contains(x)).ToList();
                Console.WriteLine("SHADERDUMP   MISSING from the template: " + (missing.Count == 0 ? "(none)" : string.Join(", ", missing)));
                Console.WriteLine("SHADERDUMP   extra in the template: " + (extra.Count == 0 ? "(none)" : string.Join(", ", extra)));
            }
            if (found.Count == 0) Console.WriteLine("SHADERDUMP nothing in the install uses a shader matching that");
        }
    }
}

