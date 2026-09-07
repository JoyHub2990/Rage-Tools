using System.Numerics;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class LightPanel
    {
        public bool RpfMode => Workspace == Space.Archive;
        public bool ParticlesMode => Workspace == Space.Particles;

        private static readonly Vector4 ParticlesWorkspaceColour = new Vector4(0.961f, 0.271f, 0.659f, 1f);

        partial void WorkspaceLeft_N4(float displayHeight, ref bool handled);
        partial void WorkspaceRight_N4(ref bool handled);
        partial void WorkspaceTheme_N4(ref bool handled);
        partial void WorkspaceTabs_N4();
        partial void WorkspaceTabColour_N4(Space space, ref Vector4 col);
        partial void WorkspaceTabTip_N4(Space space, ref string tip);
        partial void WorkspaceOverlay_N4(float displayWidth, float displayHeight);

        partial void WorkspaceLeft_N4(float displayHeight, ref bool handled)
        {
            if (RpfMode) { DrawRpfLeft_N4(displayHeight); handled = true; }
            else if (ParticlesMode) { DrawParticlesLeft_N4(displayHeight); handled = true; }
        }

        partial void WorkspaceRight_N4(ref bool handled)
        {
            if (RpfMode) { DrawRpfRight_N4(); handled = true; }
            else if (ParticlesMode) { DrawParticlesRight_N4(); handled = true; }
        }

        partial void WorkspaceTheme_N4(ref bool handled)
        {
            if (RpfMode)
            {
                UiTheme.Apply(settings.ThemeIndex, new Vector3(
                    RpfWorkspaceColour.X, RpfWorkspaceColour.Y, RpfWorkspaceColour.Z));
                handled = true;
            }
            else if (ParticlesMode)
            {
                UiTheme.Apply(settings.ThemeIndex, new Vector3(
                    ParticlesWorkspaceColour.X, ParticlesWorkspaceColour.Y, ParticlesWorkspaceColour.Z));
                handled = true;
            }
        }

        partial void WorkspaceTabs_N4()
        {
            ImGui.SameLine(0, 2);
            DrawWorkspaceTab("RPF", Space.Archive);
            ImGui.SameLine(0, 2);
            DrawWorkspaceTab("Particles", Space.Particles);
        }

        partial void WorkspaceTabColour_N4(Space space, ref Vector4 col)
        {
            if (space == Space.Particles) col = ParticlesWorkspaceColour;
        }

        partial void WorkspaceTabTip_N4(Space space, ref string tip)
        {
            if (space == Space.Archive)
                tip = "RPF workspace: the GTA V folder as a tree - real folders, loose files and .rpf\n" +
                      "archives you walk into - and the folder you are standing in as a sortable list\n" +
                      "(name, type, size, attributes, path). Search this folder, or the whole install\n" +
                      "by name. View a model, texture or meta file in place, extract one file or a\n" +
                      "whole folder, or send a model to the world or the MLO Creator.\n" +
                      "Turn on Edit mode for new folders, new archives, import, copy/paste, rename,\n" +
                      "delete and defragment - written into the archive immediately, with no undo.";
            else if (space == Space.Particles)
                tip = "Particles workspace: open a .ypt, browse every effect the game ships, play it\n" +
                      "in the viewport and edit its keyframe curves while it runs. Place a playing\n" +
                      "effect in the scene, write it into a .ytyp as a particle extension, and save\n" +
                      "the .ypt back out.";
        }

        partial void WorkspaceOverlay_N4(float displayWidth, float displayHeight)
        {
            if (RpfMode) DrawRpfList_N4(displayWidth, displayHeight);
            else if (ParticlesMode) DrawParticlesOverlay_R6(displayWidth, displayHeight);
        }
    }
}

