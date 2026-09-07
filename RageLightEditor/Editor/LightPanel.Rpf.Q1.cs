using System;
using System.IO;
using System.Numerics;
using CodeWalker.GameFiles;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class LightPanel
    {
        public class RpfViewSource_Q1
        {
            public RpfFileEntry Entry;
            public string DiskPath;
            public string Kind = "";
            public string Name = "";

            public bool CanHandOff => Kind == "ytyp" || Kind == "ymap";
        }

        public RpfViewSource_Q1 RpfViewFrom_Q1;

        public RpfViewSource_Q1 RequestRpfToLight_Q1;
        public RpfViewSource_Q1 RequestRpfToMlo_Q1;

        public void SetRpfViewSource_Q1(RpfFileEntry entry, string diskPath)
        {
            var name = entry?.Name ?? (diskPath != null ? Path.GetFileName(diskPath) : "");
            RpfViewFrom_Q1 = new RpfViewSource_Q1
            {
                Entry = entry,
                DiskPath = diskPath,
                Name = name,
                Kind = (Path.GetExtension(name) ?? "").TrimStart('.').ToLowerInvariant(),
            };
        }

        partial void ClearRpfViewSource_Q1()
        {
            RpfViewFrom_Q1 = null;
        }

        private void DrawRpfViewActions_Q1()
        {
            var src = RpfViewFrom_Q1;
            if (src == null) return;

            ImGui.SameLine();
            ImGui.TextDisabled("|");
            ImGui.SameLine();
            ImGui.TextDisabled("a view only - nothing has been loaded");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Opening a file in the explorer converts it and shows it. It does not\n" +
                                 "reach the light editor, the MLO or the world project. The two buttons\n" +
                                 "below are the only routes there, and each says where it takes you.");

            if (!src.CanHandOff) return;

            ImGui.SameLine();
            if (ImGui.SmallButton("Open in the Light editor")) RequestRpfToLight_Q1 = src;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Switches to the Lights workspace and imports this " + src.Kind +
                                 " there,\nthe way dropping the file on the window would. Press it and you\n" +
                                 "are taken there; browsing on its own never does this.");
            if (src.Kind != "ytyp") return;
            ImGui.SameLine();
            if (ImGui.SmallButton("Send to the MLO Creator")) RequestRpfToMlo_Q1 = src;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Switches to the MLO workspace and imports this interior into the\n" +
                                 "creator's own scene - its rooms, portals and entities, ready to edit.");
        }

        private void DrawRpfMetaActions_Q1(RpfFileEntry fe)
        {
            if (fe == null) return;
            var kind = (Path.GetExtension(fe.Name) ?? "").TrimStart('.').ToLowerInvariant();
            if (kind != "ytyp" && kind != "ymap") return;

            var src = new RpfViewSource_Q1 { Entry = fe, Name = fe.Name, Kind = kind };
            if (ImGui.Button("Open in the Light editor", new Vector2(-1, 0))) RequestRpfToLight_Q1 = src;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Imports this " + kind + " into the Lights workspace and switches there.\n" +
                                 "View above only converts it to XML and shows it - the explorer never\n" +
                                 "loads anything on its own.");
            if (kind != "ytyp") return;
            if (ImGui.Button("Send to the MLO Creator", new Vector2(-1, 0))) RequestRpfToMlo_Q1 = src;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Imports the interior into the MLO Creator's own scene and switches there.");
        }

        private void DrawRpfFsMetaActions_Q1(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            var kind = (Path.GetExtension(path) ?? "").TrimStart('.').ToLowerInvariant();
            if (kind != "ytyp" && kind != "ymap") return;

            var src = new RpfViewSource_Q1
            { DiskPath = path, Name = Path.GetFileName(path), Kind = kind };
            if (ImGui.Button("Open in the Light editor", new Vector2(-1, 0))) RequestRpfToLight_Q1 = src;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Imports this " + kind + " into the Lights workspace and switches there.");
            if (kind != "ytyp") return;
            if (ImGui.Button("Send to the MLO Creator", new Vector2(-1, 0))) RequestRpfToMlo_Q1 = src;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Imports the interior into the MLO Creator's own scene and switches there.");
        }

        private void DrawRpfExplorerNote_Q1()
        {
            ImGui.PushStyleColor(ImGuiCol.Text, RpfWorkspaceColour);
            ImGui.TextUnformatted("EXPLORER");
            ImGui.PopStyleColor();
            ImGui.SameLine();
            ImGui.TextDisabled("- files only");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("No 3D behind this workspace on purpose: the map is not streamed, no\n" +
                                 "models are built and nothing is loaded while you browse. Open a model\n" +
                                 "and it appears in its own viewer window; open a ytyp and it appears as\n" +
                                 "XML text. Everything else in the editor is exactly where you left it.");
        }
    }
}

