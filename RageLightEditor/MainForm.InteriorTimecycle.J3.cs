using System;
using System.Collections.Generic;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private MloInstanceData interiorTcInst;
        private int interiorTcRoom = -1;
        private int interiorTcModifier = -1;
        private float interiorTcStrength;
        private bool interiorTcApplied;
        private bool interiorTcSuppressSun = true;
        private bool interiorTcHooked;
        private double interiorTcLastTime = -1;
        private string interiorTcLastLine = "";
        private const float InteriorTcBlendPerSecond = 2.0f;
        private static readonly bool interiorTcDisabledByEnv = Environment.GetEnvironmentVariable("RLE_INTTC") == "0";

        public string InteriorTcRoomName { get; private set; } = "";
        public string InteriorTcModifierName { get; private set; } = "";
        public float InteriorTcStrengthNow => interiorTcStrength;

        private void TickInteriorTimecycle_J3()
        {
            if (!interiorTcHooked && panel != null)
            {
                interiorTcHooked = true;
                panel.WorkspaceSwitching += (from, to) => { if (from == LightPanel.Space.World) RestoreInteriorTimecycle(); };
            }
            panel.LoadInteriorTimecycleOption();
            if (settings != null && settings.WorldInteriorTimecycle != panel.WorldInteriorTimecycle)
            {
                settings.WorldInteriorTimecycle = panel.WorldInteriorTimecycle;
                settings.Save();
            }
            double now = clock.Elapsed.TotalSeconds;
            float dt = interiorTcLastTime < 0 ? 0.0f : (float)Math.Min(0.1, now - interiorTcLastTime);
            interiorTcLastTime = now;

            bool on = panel.WorldMode && panel.WorldInteriorTimecycle && !interiorTcDisabledByEnv && timecycle != null;
            if (!on)
            {
                RestoreInteriorTimecycle();
                panel.InteriorTimecycleStatus = "";
                panel.InteriorTimecycleStrength = 0.0f;
                return;
            }
            if (timecycle.Modifiers.Count == 0 && gameFiles != null && gameFiles.Ready) RefreshGameTimecycles();

            var room = FindCameraRoom(camera.Position, out var inst, out int roomIndex);
            CullerRoomOverride_L1(ref room, ref inst, ref roomIndex);
            int wantMod = -1;
            string wantWhy = "";
            uint flags = 0;
            if (room != null)
            {
                flags = room._Data.flags;
                wantMod = FindRoomModifier(room, out wantWhy);
            }
            string mloName = inst?.Owner?.Archetype?.Name ?? inst?.Owner?._CEntityDef.archetypeName.ToString() ?? "";
            string roomName = room?.RoomName ?? "";
            string modName = wantMod >= 0 ? timecycle.Modifiers[wantMod].Name : "";

            if (!ReferenceEquals(inst, interiorTcInst) || roomIndex != interiorTcRoom || wantMod != interiorTcModifier)
            {
                string line;
                if (room == null) line = $"INTERIORTC leave: outside (was {InteriorTcRoomName}), fading out from {interiorTcStrength:0.00}";
                else line = $"INTERIORTC enter {mloName} room {roomIndex} '{roomName}' flags {flags} blend {room._Data.blend:0.00} tc {room._Data.timecycleName} 2nd {room._Data.secondaryTimecycleName} -> modifier {(wantMod >= 0 ? "'" + modName + "'" : "none")} ({wantWhy}); sun {((flags & 4) != 0 && (flags & 128) == 0 ? "off" : "kept")}";
                if (line != interiorTcLastLine)
                {
                    if (wantMod >= 0)
                    {
                        var vals = timecycle.Modifiers[wantMod].Values;
                        var sb = new System.Text.StringBuilder(); int n = 0;
                        foreach (var kv in vals) { if (n++ >= 14) { sb.Append(" ..."); break; } sb.Append(' ').Append(kv.Key).Append('=').Append(kv.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)); }
                        line += $" | {vals.Count} vars:{sb}";
                    }
                    Console.WriteLine(line); interiorTcLastLine = line;
                }
                interiorTcInst = inst; interiorTcRoom = roomIndex;
                if (wantMod >= 0) interiorTcModifier = wantMod;
                InteriorTcRoomName = room == null ? "" : $"{mloName}/{roomName}";
                InteriorTcModifierName = modName;
            }

            float target = (room != null && wantMod >= 0) ? 1.0f : 0.0f;
            if (dt <= 0.0f && interiorTcStrength == 0.0f && target > 0.0f) interiorTcStrength = 0.0f;
            interiorTcStrength = MoveToward(interiorTcStrength, target, dt * InteriorTcBlendPerSecond);
            if (interiorTcStrength <= 0.0001f && target <= 0.0f)
            {
                RestoreInteriorTimecycle();
                interiorTcModifier = -1;
                panel.InteriorTimecycleStatus = "";
                panel.InteriorTimecycleStrength = 0.0f;
                return;
            }
            if (interiorTcModifier < 0 || interiorTcModifier >= timecycle.Modifiers.Count) { RestoreInteriorTimecycle(); return; }
            if (!interiorTcApplied)
            {
                interiorTcApplied = true;
                interiorTcSuppressSun = timecycle.SuppressDirectionalIndoors;
            }
            timecycle.SelectedModifier = interiorTcModifier;
            timecycle.ModifierStrength = interiorTcStrength;
            timecycle.SuppressDirectionalIndoors = (flags & 4) != 0 && (flags & 128) == 0;
            panel.InteriorTimecycleStrength = interiorTcStrength;
            panel.InteriorTimecycleStatus = room != null
                ? $"{mloName} / {roomName}: {timecycle.Modifiers[interiorTcModifier].Name} {interiorTcStrength * 100:0}%"
                : $"leaving: {timecycle.Modifiers[interiorTcModifier].Name} {interiorTcStrength * 100:0}%";
        }

        private void RestoreInteriorTimecycle()
        {
            if (!interiorTcApplied || timecycle == null) { interiorTcApplied = false; return; }
            interiorTcApplied = false;
            if (timecycle.SelectedModifier == interiorTcModifier) { timecycle.SelectedModifier = -1; timecycle.ModifierStrength = 1.0f; }
            timecycle.SuppressDirectionalIndoors = interiorTcSuppressSun;
            interiorTcStrength = 0.0f;
        }

        private static float MoveToward(float v, float target, float step)
        {
            if (v < target) return Math.Min(target, v + step);
            if (v > target) return Math.Max(target, v - step);
            return v;
        }

        private int FindRoomModifier(MCMloRoomDef room, out string why)
        {
            why = "no timecycle named";
            var d = room._Data;
            if (d.timecycleName.Hash != 0)
            {
                int mi = timecycle.FindModifier(d.timecycleName.Hash, d.timecycleName.ToString());
                if (mi >= 0) { why = "the room's timecycleName"; return mi; }
                why = $"timecycleName {d.timecycleName} not among the {timecycle.Modifiers.Count} modifiers loaded";
            }
            if (d.secondaryTimecycleName.Hash != 0)
            {
                int mi = timecycle.FindModifier(d.secondaryTimecycleName.Hash, d.secondaryTimecycleName.ToString());
                if (mi >= 0) { why = d.timecycleName.Hash != 0 ? why + "; the secondaryTimecycleName stands in" : "the room's secondaryTimecycleName"; return mi; }
                if (d.timecycleName.Hash == 0) why = $"secondaryTimecycleName {d.secondaryTimecycleName} not among the modifiers loaded";
            }
            return -1;
        }

        public MCMloRoomDef FindCameraRoom(Vector3 worldPos, out MloInstanceData inst, out int roomIndex)
        {
            inst = null; roomIndex = -1;
            MCMloRoomDef best = null;
            float bestVol = float.MaxValue;
            var shells = World?.InteriorsEmitted;
            if (shells == null) return null;
            for (int s = 0; s < shells.Count; s++)
            {
                var shell = shells[s];
                var mi = shell?.MloInstance;
                var rooms = mi?.MloArch?.rooms;
                if (rooms == null || rooms.Length < 2) continue;
                var inv = Quaternion.Invert(shell.Orientation);
                var local = Vector3.Transform(worldPos - shell.Position, inv);
                var arch = mi.MloArch;
                if (arch != null && (arch.BBMax - arch.BBMin).LengthSquared() > 1e-4f)
                {
                    const float slack = 0.5f;
                    if (local.X < arch.BBMin.X - slack || local.Y < arch.BBMin.Y - slack || local.Z < arch.BBMin.Z - slack ||
                        local.X > arch.BBMax.X + slack || local.Y > arch.BBMax.Y + slack || local.Z > arch.BBMax.Z + slack) continue;
                }
                for (int r = 1; r < rooms.Length; r++)
                {
                    var room = rooms[r];
                    if (room == null) continue;
                    var mn = room._Data.bbMin; var mx = room._Data.bbMax;
                    if (!(mx.X > mn.X) || !(mx.Y > mn.Y) || !(mx.Z > mn.Z)) continue;
                    if (local.X < mn.X || local.Y < mn.Y || local.Z < mn.Z || local.X > mx.X || local.Y > mx.Y || local.Z > mx.Z) continue;
                    float vol = (mx.X - mn.X) * (mx.Y - mn.Y) * (mx.Z - mn.Z);
                    if (vol < bestVol) { bestVol = vol; best = room; inst = mi; roomIndex = r; }
                }
            }
            return best;
        }
    }
}

