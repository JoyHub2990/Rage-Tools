using System;
using System.Collections.Generic;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private MloPlacedInterior k2TcInterior;
        private int k2TcRoom = -1;
        private int k2TcModifier = -1;
        private float k2TcStrength;
        private bool k2TcApplied;
        private bool k2TcSuppressSun = true;
        private bool k2TcManual;
        private double k2TcLastTime = -1;
        private string k2TcLastLine = "";
        private bool k2TcHooked;
        private object k2TcRoomsLogged;

        public string InteriorTcRoomName_K2 { get; private set; } = "";
        public string InteriorTcModifierName_K2 { get; private set; } = "";
        public float InteriorTcStrength_K2 => k2TcStrength;

        private void TickInteriorTimecycle_K2()
        {
            if (panel == null || timecycle == null) return;
            if (!k2TcHooked)
            {
                k2TcHooked = true;
                panel.WorkspaceSwitching += (from, to) => { if (from != LightPanel.Space.World && to == LightPanel.Space.World) RestoreInteriorTimecycle_K2(); };
            }
            if (panel.WorldMode) return;
            panel.LoadInteriorTimecycleOption();
            double now = clock.Elapsed.TotalSeconds;
            float dt = k2TcLastTime < 0 ? 0.0f : (float)Math.Min(0.1, now - k2TcLastTime);
            k2TcLastTime = now;

            var interiors = scene?.MloInfo?.Interiors;
            bool on = panel.WorldInteriorTimecycle && !interiorTcDisabledByEnv && interiors != null && interiors.Count > 0 && scene.MloVisible;
            if (!on)
            {
                RestoreInteriorTimecycle_K2();
                panel.InteriorTimecycleStatus = "";
                panel.InteriorTimecycleStrength = 0.0f;
                InteriorTcRoomName_K2 = ""; InteriorTcModifierName_K2 = "";
                return;
            }
            if (timecycle.Modifiers.Count == 0 && gameFiles != null && gameFiles.Ready) RefreshGameTimecycles();
            if (screenshotPath != null && !ReferenceEquals(interiors, k2TcRoomsLogged))
            {
                k2TcRoomsLogged = interiors;
                foreach (var it in interiors)
                {
                    var rooms = it?.Arch?.rooms; if (rooms == null) continue;
                    Console.WriteLine($"INTERIORTC rooms of {it.Name} at {it.Position.X:0.0},{it.Position.Y:0.0},{it.Position.Z:0.0}: {rooms.Length}");
                    for (int r = 1; r < rooms.Length; r++)
                    {
                        var rm = rooms[r]; if (rm == null) continue;
                        var c = it.ToWorld((rm._Data.bbMin + rm._Data.bbMax) * 0.5f);
                        int mi = FindRoomModifier(rm, out _);
                        Console.WriteLine($"  room {r} '{rm.RoomName}' centre {c.X:0.0},{c.Y:0.0},{c.Z:0.0} size {(rm._Data.bbMax - rm._Data.bbMin).X:0.0}x{(rm._Data.bbMax - rm._Data.bbMin).Y:0.0}x{(rm._Data.bbMax - rm._Data.bbMin).Z:0.0} flags {rm._Data.flags} tc {rm._Data.timecycleName} -> {(mi >= 0 ? timecycle.Modifiers[mi].Name : "none")}");
                    }
                }
            }

            var room = FindCameraRoom_K2(interiors, camera.Position, out var interior, out int roomIndex);
            int wantMod = -1; string wantWhy = ""; uint flags = 0;
            if (room != null) { flags = room._Data.flags; wantMod = FindRoomModifier(room, out wantWhy); }
            string mloName = interior?.Name ?? "";
            string roomName = room?.RoomName ?? "";
            string modName = wantMod >= 0 ? timecycle.Modifiers[wantMod].Name : "";

            if (k2TcApplied && !k2TcManual && k2TcModifier >= 0 && timecycle.SelectedModifier != k2TcModifier)
            {
                k2TcManual = true;
                Console.WriteLine($"INTERIORTC manual: modifier changed by hand to {(timecycle.SelectedModifier >= 0 && timecycle.SelectedModifier < timecycle.Modifiers.Count ? "'" + timecycle.Modifiers[timecycle.SelectedModifier].Name + "'" : "none")} - the room rule waits for the next room");
            }

            if (!ReferenceEquals(interior, k2TcInterior) || roomIndex != k2TcRoom || wantMod != k2TcModifier)
            {
                string line;
                if (room == null) line = $"INTERIORTC leave: outside every room (was {InteriorTcRoomName_K2}), fading out from {k2TcStrength:0.00}";
                else line = $"INTERIORTC enter {mloName} room {roomIndex} '{roomName}' flags {flags} blend {room._Data.blend:0.00} tc {room._Data.timecycleName} 2nd {room._Data.secondaryTimecycleName} -> modifier {(wantMod >= 0 ? "'" + modName + "'" : "none")} ({wantWhy}); sun {((flags & 4) != 0 && (flags & 128) == 0 ? "off" : "kept")} [{panel.Workspace}]";
                if (line != k2TcLastLine)
                {
                    if (wantMod >= 0)
                    {
                        var vals = timecycle.Modifiers[wantMod].Values;
                        var sb = new System.Text.StringBuilder(); int n = 0;
                        foreach (var kv in vals) { if (n++ >= 14) { sb.Append(" ..."); break; } sb.Append(' ').Append(kv.Key).Append('=').Append(kv.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)); }
                        line += $" | {vals.Count} vars:{sb}";
                    }
                    Console.WriteLine(line); k2TcLastLine = line;
                }
                bool roomChanged = !ReferenceEquals(interior, k2TcInterior) || roomIndex != k2TcRoom;
                k2TcInterior = interior; k2TcRoom = roomIndex;
                if (roomChanged) k2TcManual = false;
                if (wantMod >= 0) k2TcModifier = wantMod;
                InteriorTcRoomName_K2 = room == null ? "" : $"{mloName}/{roomName}";
                InteriorTcModifierName_K2 = modName;
            }
            if (k2TcManual)
            {
                panel.InteriorTimecycleStatus = room != null ? $"{mloName} / {roomName}: chosen by hand" : "";
                panel.InteriorTimecycleStrength = 0.0f;
                return;
            }

            float target = (room != null && wantMod >= 0) ? 1.0f : 0.0f;
            k2TcStrength = MoveToward(k2TcStrength, target, dt * InteriorTcBlendPerSecond);
            if (k2TcStrength <= 0.0001f && target <= 0.0f)
            {
                RestoreInteriorTimecycle_K2();
                k2TcModifier = -1;
                panel.InteriorTimecycleStatus = room != null ? $"{mloName} / {roomName}: no modifier" : "";
                panel.InteriorTimecycleStrength = 0.0f;
                return;
            }
            if (k2TcModifier < 0 || k2TcModifier >= timecycle.Modifiers.Count) { RestoreInteriorTimecycle_K2(); return; }
            if (!k2TcApplied)
            {
                k2TcApplied = true;
                k2TcSuppressSun = timecycle.SuppressDirectionalIndoors;
            }
            timecycle.SelectedModifier = k2TcModifier;
            timecycle.ModifierStrength = k2TcStrength;
            timecycle.SuppressDirectionalIndoors = (flags & 4) != 0 && (flags & 128) == 0;
            panel.InteriorTimecycleStrength = k2TcStrength;
            panel.InteriorTimecycleStatus = room != null
                ? $"{mloName} / {roomName}: {timecycle.Modifiers[k2TcModifier].Name} {k2TcStrength * 100:0}%"
                : $"leaving: {timecycle.Modifiers[k2TcModifier].Name} {k2TcStrength * 100:0}%";
        }

        private void RestoreInteriorTimecycle_K2()
        {
            if (!k2TcApplied || timecycle == null) { k2TcApplied = false; k2TcStrength = 0.0f; return; }
            k2TcApplied = false;
            if (timecycle.SelectedModifier == k2TcModifier) { timecycle.SelectedModifier = -1; timecycle.ModifierStrength = 1.0f; }
            timecycle.SuppressDirectionalIndoors = k2TcSuppressSun;
            k2TcStrength = 0.0f;
        }

        public static MCMloRoomDef FindCameraRoom_K2(IReadOnlyList<MloPlacedInterior> interiors, Vector3 worldPos, out MloPlacedInterior which, out int roomIndex)
        {
            which = null; roomIndex = -1;
            MCMloRoomDef best = null;
            float bestVol = float.MaxValue;
            if (interiors == null) return null;
            for (int s = 0; s < interiors.Count; s++)
            {
                var it = interiors[s];
                var arch = it?.Arch;
                var rooms = arch?.rooms;
                if (rooms == null || rooms.Length < 2) continue;
                var local = it.ToLocal(worldPos);
                if ((arch.BBMax - arch.BBMin).LengthSquared() > 1e-4f)
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
                    if (vol < bestVol) { bestVol = vol; best = room; which = it; roomIndex = r; }
                }
            }
            return best;
        }
    }
}

