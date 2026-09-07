using System;
using System.Collections.Generic;
using System.Linq;
using SDX = SharpDX;
using CodeWalker.GameFiles;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class MaterialPanel
    {

        public sealed class ClipParam_V27
        {
            public uint Hash;
            public bool IsTexture;
            public SDX.Vector4 Value;
            public string TexName;
        }

        public sealed class MaterialClip_V27
        {
            public string ShaderName;
            public uint NameHash, SpsHash;
            public byte Bucket;
            public readonly List<ClipParam_V27> Params = new List<ClipParam_V27>();

            public int TextureCount => Params.Count(p => p.IsTexture);
            public int ValueCount => Params.Count(p => !p.IsTexture);
            public string Summary => $"{ShaderName} - {ValueCount} parameter(s), {TextureCount} texture(s)";
        }

        public static MaterialClip_V27 Clipboard_V27;

        public static MaterialClip_V27 CopyMaterial_V27(MaterialRef m)
        {
            if (m?.Shader == null) return null;
            var clip = new MaterialClip_V27
            {
                ShaderName = m.Name,
                NameHash = m.Shader.Name.Hash,
                SpsHash = m.Shader.FileName.Hash,
                Bucket = m.Shader.RenderBucket,
            };
            foreach (var h in MaterialEditing.ParamHashes(m.Shader).ToList())
            {
                int i = MaterialEditing.IndexOf(m.Shader, h);
                if (i < 0) continue;
                var p = m.Shader.ParametersList.Parameters[i];
                if (p.DataType == 0)
                {
                    clip.Params.Add(new ClipParam_V27
                    {
                        Hash = h,
                        IsTexture = true,
                        TexName = (p.Data as TextureBase)?.Name,
                    });
                }
                else
                {
                    MaterialEditing.TryGetValue(m.Shader, h, out var v);
                    clip.Params.Add(new ClipParam_V27 { Hash = h, Value = v });
                }
            }
            return clip;
        }

        public static bool PasteMaterial_V27(MaterialClip_V27 clip, MaterialRef m)
        {
            if (clip == null || m?.Shader == null) return false;

            if (!ShaderPresets.Apply(m.Shader, clip.ShaderName))
            {
                m.Shader.Name = new MetaHash(clip.NameHash);
                m.Shader.FileName = new MetaHash(clip.SpsHash);
            }
            MaterialEditing.SetBucket(m.Shader, clip.Bucket);

            foreach (var p in clip.Params)
            {
                if (p.IsTexture)
                {
                    var tb = string.IsNullOrEmpty(p.TexName) ? null : MaterialEditing.MakeTextureRef(p.TexName);
                    if (!MaterialEditing.SetTexture(m.Shader, p.Hash, tb) && tb != null)
                        MaterialEditing.AddParam(m.Shader, p.Hash, tb, true);
                }
                else
                {
                    if (!MaterialEditing.SetValue(m.Shader, p.Hash, p.Value))
                        MaterialEditing.AddParam(m.Shader, p.Hash, p.Value, false);
                }
            }
            return true;
        }

        private void DrawClipboardButtons_V27(MaterialRef m)
        {
            if (ImGui.SmallButton("Copy material"))
            {
                Clipboard_V27 = CopyMaterial_V27(m);
                Status = Clipboard_V27 != null ? "Copied " + Clipboard_V27.Summary : "Nothing to copy";
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Take this material whole - shader, pass, every parameter and every " +
                                 "texture - so it can be put on another one.");

            var clip = Clipboard_V27;
            ImGui.SameLine();
            bool can = clip != null && m.CanSave;
            if (!can) ImGui.BeginDisabled();
            int onto = Math.Max(1, Selection.Count);
            if (ImGui.SmallButton(onto > 1 ? $"Paste onto {onto} materials" : "Paste material") && can)
            {
                PushUndoSelection("paste material");
                var c = clip;
                ApplyToSelection(x => PasteMaterial_V27(c, x));
                listedGeomVersion = -1;
                Status = $"Pasted {c.ShaderName} onto {onto} material(s)";
            }
            if (!can) ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                if (clip == null) ImGui.SetTooltip("Copy a material first.");
                else if (!m.CanSave)
                    ImGui.SetTooltip("This prop came from the game archives, so there is no file to write to.");
                else ImGui.SetTooltip("Put " + clip.Summary + " on every selected material.");
            }

            if (clip != null)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("holding " + clip.Summary);
            }
        }

        private static (uint Hash, string Name)[] allSamplers_V27;
        public static (uint Hash, string Name)[] AllSamplers_V27
        {
            get
            {
                if (allSamplers_V27 != null) return allSamplers_V27;
                var names = Enum.GetNames(typeof(ShaderParamNames));
                var vals = (ShaderParamNames[])Enum.GetValues(typeof(ShaderParamNames));
                var known = new HashSet<uint>(MaterialDefs.KnownTextures.Select(t => t.Hash));
                var list = new List<(uint, string)>();
                var seen = new HashSet<uint>();
                for (int i = 0; i < names.Length && i < vals.Length; i++)
                {
                    if (names[i].IndexOf("Sampler", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    uint h = (uint)vals[i];
                    if (known.Contains(h) || !seen.Add(h)) continue;
                    list.Add((h, names[i]));
                }
                allSamplers_V27 = list.OrderBy(x => x.Item2, StringComparer.OrdinalIgnoreCase).ToArray();
                return allSamplers_V27;
            }
        }

        private void DrawMoreSamplers_V27(MaterialRef m, string filter)
        {
            var all = AllSamplers_V27;
            bool any = false;
            foreach (var s in all)
            {
                if (MaterialEditing.Has(m.Shader, s.Hash)) continue;
                if (filter.Length > 0 && s.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!any)
                {
                    ImGui.Separator();
                    ImGui.TextDisabled("every other sampler in the format");
                    any = true;
                }
                if (ImGui.Selectable($"{s.Name}  [texture]"))
                {
                    PushUndoSelection("add " + s.Name);
                    var hash = s.Hash;
                    ApplyToSelection(x => MaterialEditing.AddParam(x.Shader, hash, null, true));
                    Status = "Added texture slot " + s.Name;
                    ImGui.CloseCurrentPopup();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("A sampler this editor has no label for. The game reads parameters " +
                                     "by name, so one its shader does not use is simply ignored.");
            }
        }

        private void RemoveTextureSlot_V27(MatTexInfo slot)
        {
            PushUndoSelection("remove " + slot.Label);
            var hash = slot.Hash;
            ApplyToSelection(x => MaterialEditing.RemoveParam(x.Shader, hash));
            Status = "Removed the " + slot.Label + " slot";
        }
    }
}

