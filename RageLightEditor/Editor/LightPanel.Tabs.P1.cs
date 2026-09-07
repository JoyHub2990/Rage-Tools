using System;
using System.Collections.Generic;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class LightPanel
    {
        public static readonly Space[] WorkspaceTabOrder_P1 =
        {
            Space.World, Space.Light, Space.Material, Space.Mlo, Space.Archive, Space.Particles, Space.NavMesh, Space.Terrain, Space.Animation, Space.Extension, Space.Cinematic,
        };

        private readonly List<Space> tabOrderSeen_P1 = new List<Space>();
        private Space[] tabOrderDrawn_P1 = Array.Empty<Space>();
        private bool tabOrderWarned_P1;

        public Space[] WorkspaceTabsDrawn_P1 => tabOrderDrawn_P1;

        partial void NoteWorkspaceTab_P1(Space space);
        partial void NoteWorkspaceTab_P1(Space space)
        {
            if (space == WorkspaceTabOrder_P1[0]) tabOrderSeen_P1.Clear();
            tabOrderSeen_P1.Add(space);
        }

        partial void WorkspaceTabsEnd_P1();

        partial void WorkspaceTabsEnd_P1()
        {
            ImGui.SameLine(0, 8);
            DrawWorkspaceTab("Cinematic", Space.Cinematic);

            tabOrderDrawn_P1 = tabOrderSeen_P1.ToArray();
            tabOrderSeen_P1.Clear();
            if (tabOrderWarned_P1) return;
            bool same = tabOrderDrawn_P1.Length == WorkspaceTabOrder_P1.Length;
            for (int i = 0; same && i < tabOrderDrawn_P1.Length; i++)
                same = tabOrderDrawn_P1[i] == WorkspaceTabOrder_P1[i];
            if (same) return;
            tabOrderWarned_P1 = true;
            Console.WriteLine("WORKSPACE TAB ORDER changed: drawn [" + string.Join(" ", tabOrderDrawn_P1) +
                              "] but LightPanel.WorkspaceTabOrder_P1 says [" + string.Join(" ", WorkspaceTabOrder_P1) + "]");
        }
    }
}

