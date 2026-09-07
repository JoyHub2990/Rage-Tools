using System;
using System.IO;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public static class UiMono_R3
    {
        public static ImFontPtr Font;
        public static bool Ready;

        private static float charWidth;

        public static void Build(ImGuiIOPtr io, float scale)
        {
            try
            {
                var dir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
                foreach (var name in new[] { "consola.ttf", "lucon.ttf", "cour.ttf" })
                {
                    var p = Path.Combine(dir, name);
                    if (!File.Exists(p)) continue;
                    Font = io.Fonts.AddFontFromFileTTF(p, (float)Math.Round(15.0f * scale));
                    Ready = true;
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("MONOFONT unavailable: " + ex.Message);
            }
        }

        public static bool Push()
        {
            if (!Ready) return false;
            ImGui.PushFont(Font);
            return true;
        }

        public static void Pop(bool pushed)
        {
            if (pushed) ImGui.PopFont();
        }

        public static float CharWidth()
        {
            charWidth = Math.Max(ImGui.CalcTextSize("0000000000").X / 10.0f, 1.0f);
            return charWidth;
        }
    }
}

