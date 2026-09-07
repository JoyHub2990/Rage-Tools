using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class ModelViewer
    {
        public class TextureExportRequest_Q1
        {
            public List<AssetTextureInfo> Textures = new List<AssetTextureInfo>();
            public string Format = "png";
            public bool WholeSet;
            public string SourceName = "";
        }

        public TextureExportRequest_Q1 RequestTextureExport_Q1;

        public int TextureExportFormat_Q1;
        public static readonly string[] TextureExportFormats_Q1 = { "PNG", "DDS" };
        private string TextureExportFormatKey_Q1 =>
            TextureExportFormat_Q1 == 1 ? "dds" : "png";

        public IReadOnlyList<AssetTextureInfo> ViewerTextures_Q1()
        {
            var all = ExportableTextures_S3();
            if (all.Count > 0) return all;
            if (Preview == null) return Array.Empty<AssetTextureInfo>();
            IReadOnlyList<AssetTextureInfo> list = Preview.Kind == AssetKind.TextureDict
                ? Preview.Textures
                : Preview.Stats.Textures;
            return list ?? Array.Empty<AssetTextureInfo>();
        }

        public AssetTextureInfo SelectedTexture_Q1()
        {
            var list = ViewerTextures_Q1();
            if (list.Count == 0) return null;
            return list[Math.Clamp(selTexture, 0, list.Count - 1)];
        }

        private void DrawTextureExportBar_Q1(IReadOnlyList<AssetTextureInfo> list)
        {
            if (list == null || list.Count == 0) return;

            ImGui.SetNextItemWidth(110);
            ImGui.Combo("##texfmt", ref TextureExportFormat_Q1,
                        TextureExportFormats_Q1, TextureExportFormats_Q1.Length);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("PNG: decoded to 32-bit colour - what an image editor opens.\n" +
                                 "DDS: the texture exactly as the game stores it, mips and compression\n" +
                                 "included - what you re-import with, and the only lossless one.");

            var sel = SelectedTexture_Q1();
            ImGui.SameLine();
            ImGui.BeginDisabled(sel == null);
            if (ImGui.Button("Export this texture..."))
                RaiseTextureExport_Q1(new List<AssetTextureInfo> { sel }, false);
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(sel == null
                    ? "Click a texture in the list first."
                    : "Write " + sel.Name + " out as " + TextureExportFormats_Q1[TextureExportFormat_Q1] + ".");

            ImGui.SameLine();
            if (ImGui.Button($"Export all {list.Count} textures..."))
                RaiseTextureExport_Q1(new List<AssetTextureInfo>(list), true);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Every texture this file carries, into one folder you pick.\n" +
                                 "The status line afterwards says how many were written and where.");
        }

        private void DrawTextureExportOptions_Q1()
        {
            var list = ViewerTextures_Q1();
            ImGui.Spacing(); ImGui.Separator();
            ImGui.TextDisabled("TEXTURES");
            if (list.Count == 0)
            {
                ImGui.TextWrapped(Preview == null
                    ? "Nothing open."
                    : "This file carries no textures of its own - its materials resolve theirs from a .ytd, " +
                      "which you can open from the explorer and export from here.");
                return;
            }

            ImGui.TextDisabled($"{list.Count} texture(s) in this file");
            ImGui.SetNextItemWidth(-120);
            ImGui.Combo("Format", ref TextureExportFormat_Q1,
                        TextureExportFormats_Q1, TextureExportFormats_Q1.Length);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("PNG is decoded 32-bit colour; DDS is the texture as the game stores it,\n" +
                                 "mips and block compression intact - that is the one to re-import.");

            var sel = SelectedTexture_Q1();
            ImGui.BeginDisabled(sel == null);
            if (ImGui.Button(sel == null ? "Export this texture..." : "Export " + sel.Name + "...", new Vector2(-1, 0)))
                RaiseTextureExport_Q1(new List<AssetTextureInfo> { sel }, false);
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("The one selected on the Textures tab.");

            if (ImGui.Button($"Export all {list.Count} textures...", new Vector2(-1, 0)))
                RaiseTextureExport_Q1(new List<AssetTextureInfo>(list), true);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Into a folder you pick. Nothing else in the editor is touched -\n" +
                                 "this window reads the file it opened and writes copies out.");
        }

        private void RaiseTextureExport_Q1(List<AssetTextureInfo> textures, bool wholeSet)
        {
            if (textures == null || textures.Count == 0) return;
            RequestTextureExport_Q1 = new TextureExportRequest_Q1
            {
                Textures = textures,
                WholeSet = wholeSet,
                Format = TextureExportFormatKey_Q1,
                SourceName = Title ?? "",
            };
        }
    }
}

