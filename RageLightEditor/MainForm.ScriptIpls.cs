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
        private bool scriptIplReported, scriptIplEnvDriven;
        private int scriptIplTick;

        partial void OnWorldTick_ScriptIpls()
        {
            panel.LoadScriptIplOptions();
            if (int.TryParse(Environment.GetEnvironmentVariable("RLE_INTSETS"), out int envSets)) { panel.WorldInteriorSets = Math.Clamp(envSets, 0, 2); scriptIplEnvDriven = true; }
            World.InteriorSetsMode = panel.WorldInteriorSets;
            if (!scriptIplEnvDriven && settings.WorldInteriorSets != panel.WorldInteriorSets)
            {
                settings.WorldInteriorSets = panel.WorldInteriorSets;
                settings.Save();
            }
            var c = gameFiles?.Cache;
            if (c == null || !gameFiles.Ready) return;
            if (!scriptIplReported)
            {
                scriptIplReported = true;
                Console.WriteLine(c.ScriptIplReport);
                Console.WriteLine($"SCRIPTIPL option {(c.ScriptIplsApplied ? "ON" : "OFF")}: {c.ScriptIpls.Count(i => i.Applied)} ymaps in the active map");
            }
            if (worldBuilt && Environment.GetEnvironmentVariable("RLE_SCRIPTIPL") is string sv && sv.Length > 0)
            {
                if (sv == "off" && scriptIplTick == 0) { panel.WorldScriptIpls = false; scriptIplEnvDriven = true; }
                if (sv == "flip" && scriptIplTick == 150) { panel.WorldScriptIpls = false; scriptIplEnvDriven = true; }
                if (sv == "flip" && scriptIplTick == 300) { panel.WorldScriptIpls = true; scriptIplEnvDriven = true; }
            }
            if (worldBuilt && panel.WorldScriptIpls != c.ScriptIplsApplied)
            {
                bool on = panel.WorldScriptIpls;
                c.ApplyScriptIpls(on);
                var ipls = c.ScriptIpls;
                var changed = on
                    ? World.SetScriptIplNodes(ipls.Where(i => i.Applied).Select(i => i.Node), null)
                    : World.SetScriptIplNodes(null, ipls.Select(i => i.Hash));
                if (!on) { WorldHistory.Clear(); WorldEdit.Deselect(); }
                if (!scriptIplEnvDriven)
                {
                    settings.WorldScriptIpls = on;
                    settings.Save();
                }
                Console.WriteLine($"SCRIPTIPL toggled {(on ? "ON" : "OFF")}: {changed.added} nodes added, {changed.removed} removed");
                panel.MloStatus = on ? $"Script IPLs on: {changed.added} ymaps back in the map." : $"Script IPLs off: {changed.removed} ymaps left the map.";
            }
            if (worldBuilt && ++scriptIplTick == 430 && Environment.GetEnvironmentVariable("RLE_MLOAUDIT") is string au && au.Length > 0)
                Console.WriteLine(MloAudit(au == "2", au == "all"));
            if (worldBuilt && scriptIplTick == 430 && Environment.GetEnvironmentVariable("RLE_MLOSETS") == "1")
                Console.WriteLine(InteriorSetsReport(camera.Position, 60.0f));
        }

        private string InteriorSetsReport(Vector3 at, float radius)
        {
            var sb = new System.Text.StringBuilder();
            var visSet = new HashSet<YmapEntityDef>(World.Visible);
            foreach (var y in World.ResidentYmaps)
            {
                var ents = y.AllEntities;
                if (ents == null) continue;
                foreach (var e in ents)
                {
                    if (e?.MloInstance == null || (e.Position - at).Length() > radius) continue;
                    var inst = e.MloInstance;
                    int baseVis = 0; foreach (var ie in inst.Entities ?? Array.Empty<YmapEntityDef>()) if (visSet.Contains(ie)) baseVis++;
                    sb.AppendLine($"MLOSETS {e.Archetype?.Name} ymap={y.Name} base entities {inst.Entities?.Length ?? 0} ({baseVis} visible) sets {inst.EntitySets?.Length ?? 0}:");
                    foreach (var s in inst.EntitySets ?? Array.Empty<MloInstanceEntitySet>())
                    {
                        if (s == null) continue;
                        int vis = 0, built = 0, failed = 0, noArch = 0;
                        string first = "";
                        foreach (var ie in s.Entities ?? new List<YmapEntityDef>())
                        {
                            if (ie == null) continue;
                            if (visSet.Contains(ie)) vis++;
                            if (ie.Archetype == null) { noArch++; continue; }
                            if (worldRender.PeekModel(ie.Archetype.Hash) != null) built++;
                            if (worldRender.IsFailed(ie.Archetype.Hash)) failed++;
                            if (first.Length < 160) first += $" {ie.Archetype.Name}@{ie.Position.X:0},{ie.Position.Y:0},{ie.Position.Z:0}";
                        }
                        sb.AppendLine($"  set {s.EntitySet?.Name} n={s.Entities?.Count ?? 0} placed={s.Visible} visible={vis} built={built} failed={failed} noArch={noArch} :{first}");
                    }
                }
            }
            return sb.Length == 0 ? "MLOSETS no interior within range" : sb.ToString().TrimEnd();
        }

        private string MloAudit(bool loadDrawables, bool listAll = false)
        {
            var sb = new System.Text.StringBuilder();
            var c = gameFiles?.Cache;
            if (c?.YmapDict == null) return "MLOAUDIT no cache";
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int ymaps = 0, ymapsRead = 0, interiors = 0, noArch = 0, notMlo = 0, noShell = 0, ok = 0, scriptIpl = 0;
            var fails = new List<string>();
            var partial = new List<string>();
            var all = new List<string>();
            var failsByRpf = new Dictionary<string, int>();
            var okByRpf = new Dictionary<string, int>();
            string RpfOf(string path)
            {
                int i = path.LastIndexOf(".rpf\\", StringComparison.OrdinalIgnoreCase);
                return i > 0 ? path.Substring(0, i + 4) : path;
            }
            foreach (var kv in c.YmapDict.ToList())
            {
                var fe = kv.Value;
                if (fe == null) continue;
                ymaps++;
                YmapFile ym = null;
                try { ym = c.RpfMan.GetFile<YmapFile>(fe); } catch { }
                if (ym == null) continue;
                ymapsRead++;
                var mlos = ym.CMloInstanceDefs;
                if (mlos == null || mlos.Length == 0) continue;
                bool isIpl = c.IsScriptIpl(fe.ShortNameHash);
                foreach (var md in mlos)
                {
                    interiors++;
                    if (isIpl) scriptIpl++;
                    uint ah = md.CEntityDef.archetypeName;
                    string an = JenkIndex.TryGetString(ah) ?? ah.ToString();
                    var p = md.CEntityDef.position;
                    string where = $"{an} @ {p.X:0.0},{p.Y:0.0},{p.Z:0.0} ymap={fe.Name}{(isIpl ? " [script IPL]" : "")} path={fe.Path}";
                    if (listAll) all.Add("  INT " + where);
                    var arch = c.GetArchetype(ah);
                    string rpf = RpfOf(fe.Path);
                    if (arch == null)
                    {
                        noArch++;
                        fails.Add($"  NO ARCHETYPE (ytyp not active) {where}");
                        failsByRpf[rpf] = failsByRpf.TryGetValue(rpf, out var n0) ? n0 + 1 : 1;
                        continue;
                    }
                    if (!(arch is MloArchetype))
                    {
                        notMlo++;
                        fails.Add($"  NOT AN MLO ARCHETYPE ({arch.GetType().Name}, ytyp {arch.Ytyp?.Name}) {where}");
                        failsByRpf[rpf] = failsByRpf.TryGetValue(rpf, out var n1) ? n1 + 1 : 1;
                        continue;
                    }
                    var mlo = (MloArchetype)arch;
                    int ents = 0, entArch = 0, entDraw = 0, entLoaded = 0, sampled = 0;
                    void CountEntity(MCEntityDef me)
                    {
                        if (me == null) return;
                        ents++;
                        var ea = c.GetArchetype(me._Data.archetypeName);
                        if (ea == null) return;
                        entArch++;
                        bool have = false;
                        if (ea.DrawableDict != 0) have = c.GetYddEntry(ea.DrawableDict) != null;
                        if (!have) have = c.GetYdrEntry(ea.Hash) != null || c.GetYftEntry(ea.Hash) != null;
                        if (!have) return;
                        entDraw++;
                        if (loadDrawables && sampled < 4)
                        {
                            sampled++;
                            DrawableBase d = null;
                            try { d = gameFiles.GetDrawable(ea.Hash, out _); } catch { }
                            if (d != null) entLoaded++;
                        }
                    }
                    if (mlo.entities != null) foreach (var me in mlo.entities) CountEntity(me);
                    if (mlo.entitySets != null)
                        foreach (var es in mlo.entitySets)
                            if (es?.Entities != null) foreach (var me in es.Entities) CountEntity(me);
                    string shell = null;
                    if (ents == 0) shell = "the archetype has no entities at all";
                    else if (entArch * 2 < ents) shell = $"only {entArch} of {ents} entity archetypes resolve (their ytyp is not loaded)";
                    else if (entDraw * 2 < ents) shell = $"only {entDraw} of {ents} entity drawables exist ({entArch} archetypes resolve)";
                    else if (loadDrawables && sampled > 0 && entLoaded == 0) shell = $"none of {sampled} sampled entity drawables loaded";
                    if (shell != null)
                    {
                        noShell++;
                        fails.Add($"  NO SHELL ({shell}; ytyp {arch.Ytyp?.Name} {arch.Ytyp?.RpfFileEntry?.Path}) {where}");
                        failsByRpf[rpf] = failsByRpf.TryGetValue(rpf, out var n2) ? n2 + 1 : 1;
                        continue;
                    }
                    ok++;
                    okByRpf[rpf] = okByRpf.TryGetValue(rpf, out var n3) ? n3 + 1 : 1;
                    if (entArch < ents || entDraw < ents)
                        partial.Add($"  PARTIAL {ents - entDraw} of {ents} entities have no drawable ({ents - entArch} no archetype) {where}");
                }
            }
            sb.AppendLine($"MLOAUDIT {ymaps} active ymaps ({ymapsRead} read) in {sw.ElapsedMilliseconds} ms: {interiors} interior instances, {ok} OK, {noArch} without archetype, {notMlo} not an MLO archetype, {noShell} without shell (under half of the entities resolve){(loadDrawables ? " (a few entity drawables loaded per interior)" : " (file existence)")}; {scriptIpl} of them from script IPL ymaps; {partial.Count} OK interiors with some entities missing");
            foreach (var f in fails.Take(300)) sb.AppendLine(f);
            if (fails.Count > 300) sb.AppendLine($"  ... {fails.Count - 300} more failures");
            foreach (var f in partial.Take(80)) sb.AppendLine(f);
            if (partial.Count > 80) sb.AppendLine($"  ... {partial.Count - 80} more partial interiors");
            foreach (var f in all) sb.AppendLine(f);
            if (failsByRpf.Count > 0)
            {
                sb.AppendLine("MLOAUDIT failures by rpf:");
                foreach (var kv in failsByRpf.OrderByDescending(k => k.Value)) sb.AppendLine($"  {kv.Value} failed / {(okByRpf.TryGetValue(kv.Key, out var o) ? o : 0)} ok  {kv.Key}");
            }
            sb.AppendLine("MLOAUDIT interiors by rpf (ok):");
            foreach (var kv in okByRpf.OrderByDescending(k => k.Value).Take(60)) sb.AppendLine($"  {kv.Value}  {kv.Key}");
            return sb.ToString().TrimEnd();
        }
    }
}

