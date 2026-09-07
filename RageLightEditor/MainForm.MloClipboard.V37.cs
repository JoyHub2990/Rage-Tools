using System;
using System.Collections.Generic;
using System.Linq;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private readonly List<MloCreatorEntity> mloClip_V37 = new List<MloCreatorEntity>();
        private int mloClipRoom_V37 = -1;

        public int MloClipboardCount_V37 => mloClip_V37.Count;

        private void MloCopySelected_V37()
        {
            var ui = Creator; var s = ui?.Session;
            if (s == null) return;
            var picked = ui.ActiveEntities().Where(i => i >= 0 && i < s.Entities.Count).Distinct().ToList();
            if (picked.Count == 0) { ui.SetStatus("Nothing selected to copy.", true); return; }

            mloClip_V37.Clear();
            foreach (var i in picked) mloClip_V37.Add(s.Entities[i].Clone());
            mloClipRoom_V37 = s.Entities[picked[0]].Room;
            ui.SetStatus($"Copied {mloClip_V37.Count} prop(s). Select a room and press Ctrl+V.");
            Console.WriteLine($"MLOCLIP copied {mloClip_V37.Count}");
        }

        private void MloPasteClipboard_V37()
        {
            var ui = Creator; var s = ui?.Session;
            if (s == null) return;
            if (mloClip_V37.Count == 0) { ui.SetStatus("The clipboard is empty.", true); return; }

            int room = ui.SelectedRoom >= 0 && ui.SelectedRoom < s.Rooms.Count ? ui.SelectedRoom : 0;
            s.PushUndo(mloClip_V37.Count == 1 ? "Paste prop" : $"Paste {mloClip_V37.Count} props");

            int first = s.Entities.Count;
            foreach (var src in mloClip_V37)
            {
                var e = src.Clone();
                e.RoomOverride = room;
                e.EntitySet = null;
                e.IsShell_V33 = false;
                e.SourceInfo = null;
                s.Entities.Add(e);
                if (e.SourceFile != null) mloEntityFiles_N3.Add(e.SourceFile);
            }
            if (s.Entities.Count > first) ui.SelectEntity(s.Entities.Count - 1);

            string roomName = room >= 0 && room < s.Rooms.Count ? s.Rooms[room].Name : "limbo";
            ui.SetStatus($"Pasted {mloClip_V37.Count} prop(s) into {roomName}.");
            Console.WriteLine($"MLOCLIP pasted {mloClip_V37.Count} into room {room} ({roomName})");
        }

        private void SeqTest_MloClipboard_V37(Action<string, bool, string> check)
        {
            var ui = Creator;
            if (ui == null) { Console.WriteLine("  v37 clipboard: (skipped - no creator panel)"); return; }
            var keep = ui.Session;
            try
            {
                ui.Session = new MloCreatorSession { Name = "rle_v37" };
                var s = ui.Session;
                if (s.Rooms.Count == 0) s.AddRoom("limbo", new SharpDX.Vector3(-9, -9, -3), new SharpDX.Vector3(9, 9, 6));
                s.AddRoom("kitchen", new SharpDX.Vector3(0, 0, 0), new SharpDX.Vector3(4, 4, 3));
                s.BBMin = s.Rooms[0].Min; s.BBMax = s.Rooms[0].Max;

                s.Entities.Add(new MloCreatorEntity
                {
                    ArchetypeName = "prop_v37_a", Include = true, RoomOverride = 0,
                    Position = new SharpDX.Vector3(1, 2, 3), Scale = SharpDX.Vector3.One,
                    Rotation = SharpDX.Quaternion.Identity,
                });
                ui.SelectEntity(0);

                mloClip_V37.Clear();
                MloCopySelected_V37();
                check("v37 clipboard: Ctrl+C takes the selected prop", MloClipboardCount_V37 == 1,
                      MloClipboardCount_V37 + " copied");

                ui.SelectRoom(1);
                int before = s.Entities.Count;
                MloPasteClipboard_V37();
                var pasted = s.Entities.LastOrDefault();
                check("v37 clipboard: Ctrl+V puts it in the SELECTED room, not the one it came from",
                      s.Entities.Count == before + 1 && pasted != null && pasted.Room == 1,
                      pasted == null ? "nothing pasted" : $"room {pasted.Room} ({s.Rooms[pasted.Room].Name})");

                check("v37 clipboard: ...keeping its position, because rooms overlap in space",
                      pasted != null && SharpDX.Vector3.Distance(pasted.Position, new SharpDX.Vector3(1, 2, 3)) < 0.001f,
                      pasted?.Position.ToString() ?? "-");

                check("v37 clipboard: ...and the original is left where it was",
                      s.Entities[0].Room == 0, "room " + s.Entities[0].Room);

                MloPasteClipboard_V37();
                check("v37 clipboard: pasting again adds another rather than replacing",
                      s.Entities.Count == before + 2, $"{before} -> {s.Entities.Count}");

                mloClip_V37.Clear();
                mloClip_V37.Add(new MloCreatorEntity { ArchetypeName = "shell", IsShell_V33 = true, Include = true });
                MloPasteClipboard_V37();
                check("v37 clipboard: a copied shell pastes as an ordinary prop, never a second shell",
                      s.Entities.Count(e => e.IsShell_V33) == 0, "no shell flags set");
            }
            catch (Exception ex) { check("v37 clipboard: copy and paste between rooms", false, ex.Message); }
            finally { ui.Session = keep; mloClip_V37.Clear(); }
        }
    }
}

