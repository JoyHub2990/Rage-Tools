using System;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private void ServiceWorldLightAdd_U18()
        {
            if (panel == null || panel.RequestWorldAddLight_U18 == 0) return;
            byte type = panel.RequestWorldAddLight_U18;
            panel.RequestWorldAddLight_U18 = 0;
            WorldAddLight_U18(type);
        }

        private void WorldAddLight_U18(byte type)
        {
            var sel = WorldEdit.Selection;
            var e = sel.LightEntity ?? sel.EntityDef;
            if (e?.Archetype == null) { WorldEdit.LastStatus = "select a prop first"; return; }
            uint hash = e.Archetype.Hash;
            var db = worldRender.Lights.GetDrawable(hash);
            if (db == null) { WorldEdit.LastStatus = "that prop's drawable is not loaded yet - move closer and try again"; return; }
            if (!WorldLights.CanSave(db)) { WorldEdit.LastStatus = "this prop's model cannot carry lights"; return; }
            var la = WorldLights.NewLight_U18(db, type);
            int index = WorldLights.AppendLight_U18(db, la);
            if (index < 0) { WorldEdit.LastStatus = "could not add a light to this model"; return; }
            worldLightArchOf[la] = hash;
            worldLightUnsaved.Add(hash);
            worldRender.Lights.Invalidate(hash);
            WorldHistory.Push(new DelegateCommand("Add light",
                () => { if (WorldLights.InsertLightAt_U18(db, la, index) >= 0) { worldLightUnsaved.Add(hash); worldRender.Lights.Invalidate(hash); } },
                () => { WorldLights.RemoveLight_U18(db, la); worldRender.Lights.Invalidate(hash); if (ReferenceEquals(WorldEdit.Selection.Light, la)) WorldEdit.Deselect(); }));
            panel.SetEditLight(true);
            if (!WorldSelectLight(e, index, "added")) WorldEdit.LastStatus = "light added";
            WorldEdit.LastStatus = $"{(type == 2 ? "spot" : type == 4 ? "capsule" : "point")} light added to {e.Archetype.Name} - Save as... or Add to project keeps it";
        }

        private bool ApplyPresetToWorldLight_U18(LightPresets_V20.Preset_V20 pr)
        {
            var l = panel.WorldMode ? WorldEdit?.Selection.Light : null;
            if (l == null || pr == null) return false;
            uint hash = ArchOfLight(l);
            var before = Scene.CloneLight(l);
            LightPresets_V20.Apply_V20(pr, l);
            var after = Scene.CloneLight(l);
            worldRender.Lights.Invalidate(hash);
            if (hash != 0) worldLightUnsaved.Add(hash);
            WorldHistory.Push(new SnapshotCommand<LightAttributes>("Light preset", l, before, after,
                st => { WorldLights.CopyAllInto(st, l); worldRender.Lights.Invalidate(ArchOfLight(l)); }));
            panel.WorldLightStatus = "preset '" + pr.Name + "' applied";
            return true;
        }

        public static float InteriorFogFactor_U18(bool inRoom, bool modifierSetsFog) => !inRoom || modifierSetsFog ? 1.0f : 0.06f;

        private float InteriorFogScale_U18()
        {
            if (camera == null) return 1.0f;
            var cam = camera.Position;
            if (panel.WorldMode)
            {
                var visible = World?.Visible;
                if (visible == null) return 1.0f;
                foreach (var e in visible)
                {
                    if (e?.MloInstance == null) continue;
                    if (cam.X < e.BBMin.X || cam.X > e.BBMax.X || cam.Y < e.BBMin.Y || cam.Y > e.BBMax.Y || cam.Z < e.BBMin.Z || cam.Z > e.BBMax.Z) continue;
                    return InteriorFogFactor_U18(true, false);
                }
                return 1.0f;
            }
            var interiors = scene?.MloInfo?.Interiors;
            if (interiors == null || interiors.Count == 0 || !scene.MloVisible) return 1.0f;
            var room = FindCameraRoom_K2(interiors, cam, out _, out _);
            if (room == null) return 1.0f;
            bool modFog = timecycle?.CurrentModifier != null && timecycle.ModifierStrength > 0.001f && timecycle.CurrentModifier.Values.ContainsKey("fog_density");
            return InteriorFogFactor_U18(true, modFog);
        }
    }
}
