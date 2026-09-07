using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CodeWalker.GameFiles;
using ImGuiNET;
using SDX = SharpDX;

namespace RageLightEditor.Editor
{
    public partial class LightPanel
    {
        public bool NavMode => Workspace == Space.NavMesh;

        private static readonly Vector4 NavWorkspaceColour = new Vector4(0.922f, 0.941f, 0.969f, 1f);

        private const string NavWorkspaceTooltip =
            "NavMesh workspace: open a .ynv from disk or straight out of the game archives, or load\n" +
            "the cells around where you are standing. The mesh draws over the streaming world, shaded\n" +
            "by what each polygon IS - pavement, road, water, steep, underground, interior - with a\n" +
            "colour key in the left panel.\n" +
            "RIGHT-click a polygon to select it - the left button flies the camera and places\n" +
            "the corners of a new polygon. Say what it is in one click, drag its corners with the\n" +
            "gizmo, draw new polygons, delete them, and save the .ynv or add it to the project.\n" +
            "Nothing else can be clicked here: no props, no lights, no scenario points - only the\n" +
            "nav mesh. Every edit is on the undo stack.";

        public NavMeshEditor Nav;

        partial void WorkspaceLeft_P4(float displayHeight, ref bool handled);
        partial void WorkspaceRight_P4(ref bool handled);
        partial void WorkspaceTheme_P4(ref bool handled);
        partial void WorkspaceTabs_P4();
        partial void WorkspaceTabColour_P4(Space space, ref Vector4 col);
        partial void WorkspaceTabTip_P4(Space space, ref string tip);
        partial void WorkspaceOverlay_P4(float displayWidth, float displayHeight);

        partial void WorkspaceLeft_P4(float displayHeight, ref bool handled)
        {
            if (NavMode) { DrawNavLeft_Q2(displayHeight); handled = true; }
        }

        partial void WorkspaceRight_P4(ref bool handled)
        {
            if (NavMode) { DrawNavRight_Q2(); handled = true; }
        }

        partial void WorkspaceTheme_P4(ref bool handled)
        {
            if (!NavMode) return;
            UiTheme.Apply(settings.ThemeIndex, new Vector3(
                NavWorkspaceColour.X, NavWorkspaceColour.Y, NavWorkspaceColour.Z));
            handled = true;
        }

        partial void WorkspaceTabs_P4()
        {
            ImGui.SameLine(0, 2);
            DrawWorkspaceTab("NavMesh", Space.NavMesh);
        }

        partial void WorkspaceTabColour_P4(Space space, ref Vector4 col)
        {
            if (space == Space.NavMesh) col = NavWorkspaceColour;
        }

        partial void WorkspaceTabTip_P4(Space space, ref string tip)
        {
            if (space == Space.NavMesh) tip = NavWorkspaceTooltip;
        }

        partial void WorkspaceOverlay_P4(float displayWidth, float displayHeight)
        {
            if (NavMode) DrawNavHint_Q2(displayWidth, displayHeight);
        }
    }
}

