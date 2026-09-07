using System;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private bool projectAutoAddEnabled => !DebugWorldTest || projectAutoAddTestOverride;
        private bool projectAutoAddTestOverride;
        private int projectAddedCount;

        private void ProjectAutoAddForEdit(YmapEntityDef e)
        {
            if (e == null) return;
            if (e.Ymap != null) ProjectAutoAddYmap(e.Ymap, e);
            else if (e.MloParent?.Archetype?.Ytyp != null) ProjectAutoAddYtyp(e.MloParent.Archetype.Ytyp, e);
            if (e.MloInstance != null && e.Archetype?.Ytyp != null && e.Archetype.Ytyp.AllArchetypes != null)
                ProjectAutoAddYtyp(e.Archetype.Ytyp, e.Archetype);
        }

        private void ProjectAutoAddForEdit(object key)
        {
            switch (key)
            {
                case YmapEntityDef e: ProjectAutoAddForEdit(e); break;
                case YmapCarGen cg: ProjectAutoAddYmap(cg.Ymap, cg); break;
                case YmapLODLight l:
                    ProjectAutoAddYmap(l.LodLights?.Ymap ?? l.Ymap, l);
                    ProjectAutoAddYmap(l.DistLodLights?.Ymap, null);
                    break;
                case YmapBoxOccluder bo: ProjectAutoAddYmap(bo.Ymap, bo); break;
                case YmapOccludeModelTriangle ot: ProjectAutoAddYmap(ot.Ymap, ot); break;
                case YmapTimeCycleModifier t: ProjectAutoAddYmap(t.Ymap, t); break;
                case YmapGrassInstanceBatch g: ProjectAutoAddYmap(g.Ymap, g); break;
                case MloArchetype mlo: ProjectAutoAddYtyp(mlo.Ytyp, mlo); break;
                case MCMloRoomDef r: ProjectAutoAddYtyp(r.OwnerMlo?.Ytyp, r); break;
                case MCMloPortalDef p: ProjectAutoAddYtyp(p.OwnerMlo?.Ytyp, p); break;
                case MCMloEntitySet s: ProjectAutoAddYtyp(s.OwnerMlo?.Ytyp, s); break;
                case YndNode pn: ProjectAutoAddSpaceFile(pn.Ynd, pn.Ynd?.Name); break;
                case YnvPoly np: ProjectAutoAddSpaceFile(np.Ynv, np.Ynv?.Name); break;
                case YnvPoint npt: ProjectAutoAddSpaceFile(npt.Ynv, npt.Ynv?.Name); break;
                case YnvPortal npo: ProjectAutoAddSpaceFile(npo.Ynv, npo.Ynv?.Name); break;
                case CodeWalker.World.TrainTrackNode tn: ProjectAutoAddSpaceFile(tn.Track, tn.Track?.Name); break;
                case CodeWalker.World.ScenarioNode sn: ProjectAutoAddSpaceFile(sn.Ymt, sn.Ymt?.Name); break;
                case CodeWalker.World.AudioPlacement au: ProjectAutoAddSpaceFile(au.RelFile, au.RelFile?.Name); break;
            }
        }

        private void ProjectAddSelection(WorldSelection s)
        {
            if (!s.HasValue) { WorldEdit.LastStatus = "nothing selected to add"; return; }
            var p = ProjWin.Project;
            var key = s.GetProjectObject();
            if (s.MloEntityDef != null && s.CollisionBounds == null) key = s.MloEntityDef;
            if (s.CollisionBounds != null && s.EntityDef == null)
            {
                var ybn = s.CollisionBounds.GetRootYbn();
                var name = ybn?.Name ?? ybn?.RpfFileEntry?.Name;
                if (string.IsNullOrEmpty(name)) { WorldEdit.LastStatus = "collision: no file to add"; return; }
                if (p != null && p.YbnFiles.Contains(ybn))
                {
                    ProjWin.Visible = true; ProjWin.Minimized = false;
                    WorldEdit.LastStatus = name + " is already in the project";
                    return;
                }
                if (p == null) { p = new CwProject { Name = "New Project", HasChanged = true }; ProjWin.Project = p; }
                if (!p.YbnFilenames.Contains(name)) { p.YbnFilenames.Add(name); p.HasChanged = true; }
                ProjWin.Visible = true; ProjWin.Minimized = false;
                WorldEdit.LastStatus = name + " listed in the project (collision is read-only in this build)";
                return;
            }
            bool wasEnabled = projectAutoAddTestOverride;
            projectAutoAddTestOverride = true;
            projectAddedCount = 0;
            ProjectAutoAddForEdit(key);
            projectAutoAddTestOverride = wasEnabled;
            if (projectAddedCount == 0)
            {
                ProjWin.Visible = true; ProjWin.Minimized = false;
                if (key is YmapEntityDef e) ProjWin.ShowWorldSelection(e);
                ProjWin.Reveal(key);
                WorldEdit.LastStatus = "already in the project";
            }
        }

        private void TickProjectAddSelection_H2()
        {
            if (ProjWin == null) return;
            var s = WorldEdit.Selection;
            ProjWin.WorldSelectionSummary = ProjectAddSelectionSummary(s);
            if (ProjWin.RequestAddWorldSelectionToProject)
            {
                ProjWin.RequestAddWorldSelectionToProject = false;
                ProjectAddSelection(s);
            }
        }

        private static string ProjectAddSelectionSummary(in WorldSelection s)
        {
            if (!s.HasValue) return null;
            string what = s.GetNameString(s.TypeName);
            string file = null;
            if (s.CollisionBounds != null && s.EntityDef == null)
            {
                var ybn = s.CollisionBounds.GetRootYbn();
                file = ybn?.Name ?? ybn?.RpfFileEntry?.Name;
                return $"{what}  ->  {file ?? "(embedded bounds: nothing to add)"}";
            }
            if (s.MloEntityDef != null && s.CollisionBounds == null)
            {
                var ytyp = s.MloEntityDef.Archetype?.Ytyp?.Name;
                var ymap = s.MloEntityDef.Ymap?.Name;
                file = ymap != null && ytyp != null ? ymap + " + " + ytyp : ymap ?? ytyp;
            }
            else if (s.EntityDef != null)
            {
                file = s.EntityDef.Ymap?.Name ?? s.EntityDef.MloParent?.Archetype?.Ytyp?.Name;
            }
            else if (s.LodLight != null)
            {
                var a = s.LodLight.LodLights?.Ymap?.Name ?? s.LodLight.Ymap?.Name; var b = s.LodLight.DistLodLights?.Ymap?.Name;
                file = a != null && b != null && a != b ? a + " + " + b : a ?? b;
            }
            else if (s.OwnerYmap != null) file = s.OwnerYmap.Name;
            else if (s.ScenarioNode != null) file = s.ScenarioNode.Ymt?.Name;
            else if (s.PathNode != null) file = s.PathNode.Ynd?.Name;
            else if (s.NavPoly != null) file = s.NavPoly.Ynv?.Name;
            else if (s.NavPoint != null) file = s.NavPoint.Ynv?.Name;
            else if (s.NavPortal != null) file = s.NavPortal.Ynv?.Name;
            else if (s.TrainTrackNode != null) file = s.TrainTrackNode.Track?.Name;
            else if (s.Audio != null) file = s.Audio.RelFile?.Name;
            else if (s.WaterQuad != null || s.CalmingQuad != null || s.WaveQuad != null) return what + "  ->  water.xml is read-only in this build";
            return $"{what}  ->  {file ?? "(no file to add)"}";
        }

        private void ProjectAutoAddSpaceFile(object file, string name)
        {
            if (file == null || projCtl == null || !projectAutoAddEnabled) return;
            var p = ProjWin.Project;
            bool added = false;
            switch (file)
            {
                case YndFile ynd: if (p != null && p.ContainsYnd(ynd)) return; added = projCtl.AddGameFileToProject(ynd); break;
                case YnvFile ynv: if (p != null && p.ContainsYnv(ynv)) return; added = projCtl.AddGameFileToProject(ynv); break;
                case CodeWalker.World.TrainTrack tt: if (p != null && p.ContainsTrainTrack(tt)) return; added = projCtl.AddGameFileToProject(tt); break;
                case YmtFile ymt: if (p != null && p.ContainsScenario(ymt)) return; added = projCtl.AddGameFileToProject(ymt); break;
                case RelFile rel: if (p != null && p.ContainsAudioRel(rel)) return; added = projCtl.AddGameFileToProject(rel); break;
            }
            if (!added) return;
            ProjWin.Visible = true;
            ProjWin.Minimized = false;
            ProjWin.Select(ProjWin.Project);
            projectAddedCount++;
            WorldEdit.LastStatus = (name ?? "file") + " added to the project (edited)";
            Console.WriteLine("PROJECT auto-added " + name);
        }

        private void ProjectAutoAddYmap(YmapFile ymap, object select)
        {
            if (ymap == null || projCtl == null || !projectAutoAddEnabled) return;
            var p = ProjWin.Project;
            if (p != null && p.ContainsYmap(ymap)) return;
            if (!projCtl.AddGameFileToProject(ymap)) return;
            ProjectRevealAdded(select, ymap, ymap.Name);
        }

        private void ProjectAutoAddYtyp(YtypFile ytyp, object select)
        {
            if (ytyp == null || projCtl == null || !projectAutoAddEnabled) return;
            var p = ProjWin.Project;
            if (p != null && p.ContainsYtyp(ytyp)) return;
            if (!projCtl.AddGameFileToProject(ytyp)) return;
            ProjectRevealAdded(select, ytyp, ytyp.Name);
        }

        private void ProjectRevealAdded(object select, object file, string fileName)
        {
            ProjWin.Visible = true;
            ProjWin.Minimized = false;
            object page = select;
            switch (select)
            {
                case YmapEntityDef _: case YmapFile _: case YtypFile _: case Archetype _:
                case MCMloRoomDef _: case MCMloPortalDef _: case MCMloEntitySet _: break;
                default: page = file; break;
            }
            if (page is YmapEntityDef e) ProjWin.ShowWorldSelection(e);
            else if (page != null) ProjWin.Select(page);
            ProjWin.Reveal(select ?? file);
            projectAddedCount++;
            WorldEdit.LastStatus = fileName + " added to the project (edited)";
            Console.WriteLine("PROJECT auto-added " + fileName);
        }
    }
}

