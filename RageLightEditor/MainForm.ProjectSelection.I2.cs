using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using CodeWalker.World;
using RageLightEditor.Editor;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private bool projSelWired;
        private int projSelSerialSeen = -1;
        private YmapEntityDef projSelLastWorldEnt;
        private ScenarioNode projSelLastWorldScenario;
        private readonly List<YmtFile> projScenarioAdded = new List<YmtFile>();
        private readonly Dictionary<YmtFile, YmtFile> projScenarioReplaced = new Dictionary<YmtFile, YmtFile>();
        private bool projScenarioDirty = true;
        private string projScenarioSig = "";
        private int projSelEnvTick;
        private bool projSelEnvDone;
        private double projSelHoldUntil;

        private void TickProjectSelection_I2()
        {
            if (ProjWin == null || projCtl == null) return;
            WireProjectSelection();
            TickProjectEntitySelection();
            TickProjectScenario();
            TickProjectSelectionEnv();
        }

        private void WireProjectSelection()
        {
            if (projSelWired) return;
            projSelWired = true;
            var inner = ProjWin.FindMloInstance;
            ProjWin.FindMloInstance = mlo =>
            {
                var inst = inner?.Invoke(mlo);
                if (inst != null || mlo == null) return inst;
                var p = ProjWin.Project;
                if (p == null) return null;
                foreach (var y in p.YmapFiles)
                {
                    var mlos = y?.MloEntities;
                    if (mlos == null) continue;
                    foreach (var v in mlos)
                        if (v?.MloInstance != null && (ReferenceEquals(v.Archetype, mlo) || v.Archetype?.Hash == mlo.Hash))
                            return v.MloInstance;
                }
                return null;
            };
            projCtl.ScenarioFilesChanged += () => projScenarioDirty = true;
            projCtl.ProjectYmapsChanged += () => projScenarioDirty = true;
        }

        private void TickProjectEntitySelection()
        {
            if (ProjWin.SelectSerial != projSelSerialSeen)
            {
                projSelSerialSeen = ProjWin.SelectSerial;
                var e = ProjWin.CurrentEntity;
                if (e != null && !ReferenceEquals(WorldEdit.Selected, e))
                {
                    var sel = WorldSelection.FromProjectObject(e);
                    sel.CamRel = e.Position - camera.Position;
                    WorldEdit.Select(sel);
                    lastWorldSelForProject = e;
                    projSelLastWorldEnt = e;
                    string where = e.MloParent != null
                        ? $" in {e.MloParent.Archetype?.Name ?? "interior"}" + DescribeMloSlot(e)
                        : (e.Ymap != null ? " of " + e.Ymap.Name : "");
                    WorldEdit.LastStatus = $"selected {e.Archetype?.Name ?? e._CEntityDef.archetypeName.ToString()}{where}";
                }
            }
            var w = WorldEdit.Selected;
            if (!ReferenceEquals(w, projSelLastWorldEnt))
            {
                projSelLastWorldEnt = w;
                if (w != null && ProjWin.Visible && !ReferenceEquals(w, ProjWin.CurrentEntity) && EntityIsInProject(w))
                    ProjWin.Reveal(w);
            }
        }

        private void MarkInteriorYtypChanged_I2(YmapEntityDef e)
        {
            var ytyp = e?.MloParent?.Archetype?.Ytyp;
            if (ytyp != null) ytyp.HasChanged = true;
        }

        private static string DescribeMloSlot(YmapEntityDef e)
        {
            var mlo = e?.MloParent?.Archetype as MloArchetype;
            var inst = e?.MloParent?.MloInstance;
            if (mlo == null || inst == null) return "";
            var me = inst.TryGetArchetypeEntity(e);
            if (me == null && e.MloEntitySet == null && mlo.entities != null && e.Index >= 0 && e.Index < mlo.entities.Length) me = mlo.entities[e.Index];
            if (me == null) return e.MloEntitySet != null ? $", set {e.MloEntitySet.EntitySet?.Name}" : "";
            var room = mlo.GetEntityRoom(me); if (room != null) return $", room {room.Index}: {room.RoomName}";
            var portal = mlo.GetEntityPortal(me); if (portal != null) return $", portal {portal.Index}";
            var set = mlo.GetEntitySet(me); if (set != null) return $", set {set.Name}";
            return "";
        }

        private bool EntityIsInProject(YmapEntityDef e)
        {
            var p = ProjWin.Project;
            if (p == null || e == null) return false;
            if (e.Ymap != null) return p.ContainsYmap(e.Ymap);
            var ytyp = e.MloParent?.Archetype?.Ytyp;
            if (ytyp != null && p.ContainsYtyp(ytyp)) return true;
            uint h = e.MloParent?.Archetype?.Hash ?? 0;
            return h != 0 && p.YtypFiles.Any(t => t?.AllArchetypes != null && t.AllArchetypes.Any(a => a is MloArchetype && a.Hash == h));
        }

        private void TickProjectScenario()
        {
            if (ProjWin.ScenarioNodeClicked != null)
            {
                var n = ProjWin.ScenarioNodeClicked;
                ProjWin.ScenarioNodeClicked = null;
                SelectScenarioNodeInWorld(n);
            }
            if (ProjWin.ScenarioOverlayRequested)
            {
                ProjWin.ScenarioOverlayRequested = false;
                if (panel != null && !panel.ShowScenarios) { panel.ShowScenarios = true; WorldEdit.LastStatus = "scenario overlay on"; }
            }
            if (ProjWin.RequestGoToScenario)
            {
                ProjWin.RequestGoToScenario = false;
                EnterWorldWorkspaceForGoTo();
                projCtl.GoToScenario(ProjWin.CurrentScenario, WorldEdit.Selection.ScenarioNode);
            }
            var sn = WorldEdit.Selection.ScenarioNode;
            ProjWin.WorldScenarioSelection = sn;
            if (!ReferenceEquals(sn, projSelLastWorldScenario))
            {
                projSelLastWorldScenario = sn;
                if (sn?.Ymt != null && ProjWin.Visible && ProjWin.Project != null && ProjWin.Project.ContainsScenario(sn.Ymt))
                    ProjWin.ShowWorldScenarioSelection(sn);
            }
            SyncProjectScenarioRegions();
        }

        private void SelectScenarioNodeInWorld(ScenarioNode n)
        {
            if (n == null || panel == null) return;
            EnterWorldWorkspaceForGoTo();
            var sel = WorldSelection.FromProjectObject(n);
            sel.AABB = new BoundingBox(new Vector3(-0.5f), new Vector3(0.5f));
            sel.CamRel = n.Position - camera.Position;
            sel.HitDist = sel.CamRel.Length();
            WorldEdit.Select(sel);
            projSelLastWorldScenario = n;
            panel.ShowScenarios = true;
            if (SelMode != WorldSelectionMode.Scenario)
            {
                int idx = LightPanel.IndexOfMode(WorldSelectionMode.Scenario);
                if (idx >= 0) panel.SelectionMode = idx;
            }
            SpaceDataOrNull?.EnsureScenarios();
            var pt = n.MyPoint ?? n.ClusterMyPoint;
            WorldEdit.LastStatus = $"scenario {n.MedTypeName} {pt?.Type?.Name ?? n.ChainingNode?.Type?.Name ?? ""} of {n.Ymt?.Name}";
        }

        private void SyncProjectScenarioRegions()
        {
            var sd = spaceData;
            if (sd == null || !sd.ScenariosReady || sd.Scenarios?.ScenarioRegions == null) return;
            var p = ProjWin.Project;
            var want = (p != null && ProjWin.RenderProjectItems)
                ? p.ScenarioFiles.Where(f => f?.ScenarioRegion != null).ToList()
                : new List<YmtFile>();
            var list = sd.Scenarios.ScenarioRegions;
            string sig = string.Join(",", want.Select(f => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(f)));
            if (!projScenarioDirty && sig == projScenarioSig) return;
            projScenarioDirty = false;
            projScenarioSig = sig;
            foreach (var added in projScenarioAdded)
            {
                int i = list.IndexOf(added);
                if (i < 0) continue;
                if (projScenarioReplaced.TryGetValue(added, out var game) && game != null) list[i] = game; else list.RemoveAt(i);
                pathBatch?.Invalidate(added.ScenarioRegion);
                if (game != null) pathBatch?.Invalidate(game.ScenarioRegion);
            }
            projScenarioAdded.Clear();
            projScenarioReplaced.Clear();
            int replaced = 0;
            foreach (var f in want)
            {
                if (list.Contains(f)) continue;
                ProjectController.ResolveScenarioTypes(f, gameFiles?.Cache);
                string leaf = Path.GetFileName(f.FilePath ?? f.Name ?? "");
                int gi = list.FindIndex(g => g != null && !ReferenceEquals(g, f) &&
                                             string.Equals(Path.GetFileName(g.Name ?? g.RpfFileEntry?.Name ?? ""), leaf, StringComparison.OrdinalIgnoreCase));
                if (gi >= 0) { projScenarioReplaced[f] = list[gi]; pathBatch?.Invalidate(list[gi].ScenarioRegion); list[gi] = f; replaced++; }
                else { projScenarioReplaced[f] = null; list.Add(f); }
                projScenarioAdded.Add(f);
                pathBatch?.Invalidate(f.ScenarioRegion);
            }
            if (projScenarioAdded.Count > 0 || replaced > 0)
                Console.WriteLine($"PROJSCENARIO {projScenarioAdded.Count} project region(s) in the overlay ({replaced} replacing a game region): {string.Join(", ", projScenarioAdded.Select(f => f.Name + "[" + (f.ScenarioRegion?.Nodes?.Count ?? 0) + "]"))}");
        }

        private void TickProjectSelectionEnv()
        {
            if (projSelEnvDone) { if (projSelHoldUntil > 0 && clock.Elapsed.TotalSeconds < projSelHoldUntil) worldWarmup = Math.Min(worldWarmup, 445); return; }
            var env = Environment.GetEnvironmentVariable("RLE_PROJSEL");
            if (string.IsNullOrEmpty(env) || !worldBuilt) { projSelEnvDone = string.IsNullOrEmpty(env); return; }
            if (++projSelEnvTick < 440) return;
            projSelEnvDone = true;
            var parts = env.Split(',');
            string kind = parts[0].Trim().ToLowerInvariant();
            bool projSelShowWin = Environment.GetEnvironmentVariable("RLE_PROJSEL_SHOWWIN") == "1";
            int Num(int i, int def) => i < parts.Length && int.TryParse(parts[i], out var v) ? v : def;
            var p = ProjWin.Project;
            try
            {
                if (kind == "scenario")
                {
                    int fi = Num(1, 0);
                    if (p == null || fi >= p.ScenarioFilenames.Count) { Console.WriteLine($"PROJSEL {env}: no scenario file {fi} in the project ({p?.ScenarioFilenames.Count ?? 0})"); return; }
                    var ymt = projCtl.OpenScenarioFile(p.ScenarioFilenames[fi]);
                    Console.WriteLine($"PROJSEL scenario {p.ScenarioFilenames[fi]} -> {(ymt == null ? "not opened: " + ProjWin.Status : $"{ymt.Name} {ymt.ScenarioRegion?.Nodes?.Count ?? 0} points, page {(ReferenceEquals(ProjWin.CurrentScenario, ymt) ? "shown" : "NOT shown")}")}");
                    if (ymt?.ScenarioRegion?.Nodes != null && parts.Length > 2 && parts[2].Trim().ToLowerInvariant() == "point")
                    {
                        int ni = Num(3, 0);
                        if (ni < ymt.ScenarioRegion.Nodes.Count) { ProjWin.ScenarioNodeClicked = ymt.ScenarioRegion.Nodes[ni]; TickProjectScenario(); }
                        else Console.WriteLine($"PROJSEL point {ni}: the region has {ymt.ScenarioRegion.Nodes.Count} points");
                    }
                    ProjWin.Visible = true; ProjWin.Minimized = !projSelShowWin;
                }
                else
                {
                    var mlo = p?.YtypFiles.SelectMany(t => t?.AllArchetypes ?? Array.Empty<Archetype>()).OfType<MloArchetype>().FirstOrDefault();
                    if (mlo == null) { Console.WriteLine($"PROJSEL {env}: no interior archetype in the project ({p?.YtypFiles.Count ?? 0} ytyps)"); return; }
                    if (kind == "list")
                    {
                        Console.WriteLine($"PROJSEL list {mlo.Name}: {mlo.entities?.Length ?? 0} entities, {mlo.rooms?.Length ?? 0} rooms, {mlo.portals?.Length ?? 0} portals, {mlo.entitySets?.Length ?? 0} sets");
                        for (int i = 0; i < (mlo.rooms?.Length ?? 0); i++)
                        { var r = mlo.rooms[i]; var att = r.AttachedObjects ?? Array.Empty<uint>(); Console.WriteLine($"PROJSEL   room {i}: {r.RoomName} ({att.Length}) " + string.Join(", ", att.Take(8).Select((a, k) => $"[{k}]={a}:{(a < (mlo.entities?.Length ?? 0) ? mlo.entities[a].Name : "?")}"))); }
                        for (int i = 0; i < (mlo.portals?.Length ?? 0); i++)
                        { var pt = mlo.portals[i]; var att = pt.AttachedObjects ?? Array.Empty<uint>(); if (att.Length > 0) Console.WriteLine($"PROJSEL   portal {i}: ({att.Length}) " + string.Join(", ", att.Take(8).Select((a, k) => $"[{k}]={a}:{(a < (mlo.entities?.Length ?? 0) ? mlo.entities[a].Name : "?")}"))); }
                        for (int i = 0; i < (mlo.entitySets?.Length ?? 0); i++)
                        { var s = mlo.entitySets[i]; Console.WriteLine($"PROJSEL   set {i}: {s.Name} ({s.Entities?.Length ?? 0}) " + string.Join(", ", (s.Entities ?? Array.Empty<MCEntityDef>()).Take(8).Select((e2, k) => $"[{k}]={e2.Name}"))); }
                        return;
                    }
                    MCEntityDef me = null; string slot = "";
                    int n = Num(1, 0), k = Num(3, 0);
                    if (kind == "room" && mlo.rooms != null && n < mlo.rooms.Length)
                    { var r = mlo.rooms[n]; var att = r.AttachedObjects; if (att != null && k < att.Length && att[k] < (mlo.entities?.Length ?? 0)) me = mlo.entities[att[k]]; slot = $"room {n}: {r.RoomName} entity {k} (index {(att != null && k < att.Length ? att[k].ToString() : "?")})"; }
                    else if (kind == "portal" && mlo.portals != null && n < mlo.portals.Length)
                    { var pt = mlo.portals[n]; var att = pt.AttachedObjects; if (att != null && k < att.Length && att[k] < (mlo.entities?.Length ?? 0)) me = mlo.entities[att[k]]; slot = $"portal {n} entity {k}"; }
                    else if (kind == "set" && mlo.entitySets != null && n < mlo.entitySets.Length)
                    { var s = mlo.entitySets[n]; if (s.Entities != null && k < s.Entities.Length) me = s.Entities[k]; slot = $"set {n}: {s.Name} entity {k}"; }
                    else if (kind == "entity" && mlo.entities != null && n < mlo.entities.Length) { me = mlo.entities[n]; slot = $"entity {n}"; }
                    if (me == null) { Console.WriteLine($"PROJSEL {env}: no such node in {mlo.Name} (rooms {mlo.rooms?.Length}, portals {mlo.portals?.Length}, sets {mlo.entitySets?.Length}, entities {mlo.entities?.Length})"); return; }
                    ProjWin.Visible = true;
                    ProjWin.SelectMloEntity(me);
                    TickProjectEntitySelection();
                    var live = ProjWin.CurrentEntity;
                    Console.WriteLine($"PROJSEL {mlo.Name} {slot} -> {me.Name}: live {(live != null ? "yes" : "NO (interior not loaded: definition page)")} " +
                                      $"world {(ReferenceEquals(WorldEdit.Selected, live) && live != null ? "selected" : "NOT selected")} pos {live?.Position} gizmo {(WorldEdit.Selection.GizmoTarget() != null)} " +
                                      $"bb {(live?.Archetype != null ? (live.Archetype.BBMax - live.Archetype.BBMin).ToString() : "no archetype")} built {(live?.Archetype != null && worldRender.PeekModel(live.Archetype.Hash) != null)} visible {(live != null && World.Visible.Contains(live))}");
                    ProjWin.Minimized = !projSelShowWin;
                }
                Console.WriteLine(WorldSelReport());
            }
            catch (Exception ex) { Console.WriteLine("PROJSEL failed: " + ex); }
            if (float.TryParse(Environment.GetEnvironmentVariable("RLE_PROJSEL_HOLD"), out float hold) && hold > 0)
                projSelHoldUntil = clock.Elapsed.TotalSeconds + hold;
        }
    }
}

