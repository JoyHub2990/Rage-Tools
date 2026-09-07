using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class LightPanel
    {
        public bool MirrorSurprise_S6 => settings?.MirrorSurprise ?? true;

        internal void DrawMirrorJokeMenuItem_S6()
        {
            if (settings == null) return;
            bool on = settings.MirrorSurprise;
            if (ImGui.MenuItem("Mirror surprise", null, on))
            {
                settings.MirrorSurprise = !on;
                settings.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Stand close to a mirror and look at it. Occasionally.\n" +
                                 "Never in photo mode, never in Cinematic, and never in a render to file.");
        }
    }
}

