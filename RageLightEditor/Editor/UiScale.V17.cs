using System;
using System.Linq;
using System.IO;
using System.Numerics;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public static class UiScale_V17
    {
        public static float Scale { get; private set; } = 1.0f;

        private static float applied = 1.0f;

        public const float Min = 0.75f;
        public const float Max = 4.0f;

        public const float BaseFontPx = 16.0f;

        public static string FontFile = "";
        public static float FontPx;
        public static int FontVersion;

        public static float Auto(int deviceDpi, int screenWidth)
        {
            float windows = deviceDpi / 96.0f;
            if (windows > 1.05f) return windows;
            if (screenWidth >= 7000) return 3.0f;
            if (screenWidth >= 5000) return 2.25f;
            if (screenWidth >= 3840) return 1.75f;
            if (screenWidth >= 3000) return 1.5f;
            if (screenWidth >= 2400) return 1.25f;
            return 1.0f;
        }

        public static float Resolve(float saved, int deviceDpi, int screenWidth) =>
            saved <= 0.01f ? Auto(deviceDpi, screenWidth) : Math.Clamp(saved, Min, Max);

        private static ImGuiStyle baseStyle;
        private static bool haveBaseStyle;

        public static unsafe void Set(float scale, ImGuiIOPtr io, ImGuiBackend.ImGuiRenderer renderer)
        {
            scale = Math.Clamp(scale, Min, Max);
            if (Math.Abs(scale - Scale) < 0.001f && renderer != null) return;
            Scale = scale;

            ApplyStyleScale(scale);
            applied = scale;

            BuildFonts(io, scale);
            renderer?.RecreateFontTexture();
        }

        /// <summary>
        /// Size the style for this scale, from a pristine copy every time.
        ///
        /// ScaleAllSizes floors every value it touches, so scaling the already-scaled style - as a
        /// slider being dragged does, once per frame - loses a fraction on each call and walks the
        /// sizes down to zero. WindowMinSize reaching 0 trips ImGui's own "Invalid style setting"
        /// assertion in NewFrame.
        /// </summary>
        public static unsafe void ApplyStyleScale(float scale)
        {
            var style = ImGui.GetStyle();
            ImGuiStyle* live = style.NativePtr;
            if (!haveBaseStyle) { baseStyle = *live; haveBaseStyle = true; }
            else *live = baseStyle;
            style.ScaleAllSizes(Math.Clamp(scale, Min, Max));
            if (style.WindowMinSize.X < 1.0f || style.WindowMinSize.Y < 1.0f)
                style.WindowMinSize = new Vector2(Math.Max(1.0f, style.WindowMinSize.X),
                                                  Math.Max(1.0f, style.WindowMinSize.Y));
        }

        /// <summary>ImGui's own ProggyClean, rather than a system UI font.</summary>
        public static bool UseProggy = false;

        public static readonly string[] UiFontFiles =
        {
            "OpenSans-Regular.ttf",
            "NotoSans-Regular.ttf",
            "segoeui.ttf",
        };

        public static string UiFontPath()
        {
            var dir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            foreach (var f in UiFontFiles)
            {
                var p = Path.Combine(dir, f);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        public const float ProggyPx = 13.0f;

        public static unsafe void AddUiFont(ImGuiIOPtr io, float scale)
        {
            string chosen = !string.IsNullOrEmpty(FontFile) && File.Exists(FontFile) ? FontFile : null;
            if (chosen != null)
            {
                float px = (float)Math.Round((FontPx > 0.5f ? FontPx : BaseFontPx) * scale);
                var f = io.Fonts.AddFontFromFileTTF(chosen, Math.Max(px, 6.0f));
                if (f.NativePtr != null) return;
                io.Fonts.Clear();
            }
            var path = UiFontPath();
            if (UseProggy || path == null)
            {
                var cfg = ImGuiNative.ImFontConfig_ImFontConfig();
                cfg->SizePixels = (float)Math.Round((FontPx > 0.5f ? FontPx : ProggyPx) * scale);
                cfg->PixelSnapH = 1;
                io.Fonts.AddFontDefault(cfg);
                ImGuiNative.ImFontConfig_destroy(cfg);
            }
            else io.Fonts.AddFontFromFileTTF(path, (float)Math.Round((FontPx > 0.5f ? FontPx : BaseFontPx) * scale));
        }

        public static unsafe void BuildFonts(ImGuiIOPtr io, float scale)
        {
            io.Fonts.Clear();
            AddUiFont(io, scale);
            UiMono_R3.Build(io, scale);
            if (!io.Fonts.Build())
            {
                io.Fonts.Clear();
                io.Fonts.AddFontDefault();
                UiMono_R3.Build(io, scale);
                io.Fonts.Build();
            }
        }

        public static unsafe void Rebuild(ImGuiIOPtr io, ImGuiBackend.ImGuiRenderer renderer)
        {
            BuildFonts(io, Scale);
            renderer?.RecreateFontTexture();
            FontVersion++;
        }

        public static string Describe(float saved, int deviceDpi, int screenWidth) =>
            saved <= 0.01f
                ? $"Automatic: {Auto(deviceDpi, screenWidth) * 100:0}% " +
                  $"(Windows says {deviceDpi * 100 / 96}%, screen is {screenWidth} wide)"
                : $"{saved * 100:0}%, chosen by hand";

        public static int SelfTest_V17(Action<string, bool, string> check)
        {
            int fails = 0;
            void Chk(string what, bool ok, string d) { if (!ok) fails++; check(what, ok, d); }

            Chk("v17 dpi: a 1080p screen at 100% is left alone",
                Math.Abs(Auto(96, 1920) - 1.0f) < 1e-4f, Auto(96, 1920).ToString("0.##"));
            Chk("v17 dpi: 1440p at 100% is scaled up",
                Auto(96, 2560) > 1.2f, Auto(96, 2560).ToString("0.##"));
            Chk("v17 dpi: 4K at 100% is scaled up more",
                Auto(96, 3840) > Auto(96, 2560), $"{Auto(96, 3840):0.##} vs {Auto(96, 2560):0.##}");
            Chk("v17 dpi: Windows' own setting wins when it is set",
                Math.Abs(Auto(144, 3840) - 1.5f) < 1e-4f, Auto(144, 3840).ToString("0.##"));
            Chk("v17 dpi: ...even on a small screen",
                Math.Abs(Auto(192, 1920) - 2.0f) < 1e-4f, Auto(192, 1920).ToString("0.##"));
            Chk("v17 dpi: a hand-set scale beats the guess",
                Math.Abs(Resolve(1.25f, 192, 3840) - 1.25f) < 1e-4f, Resolve(1.25f, 192, 3840).ToString("0.##"));
            Chk("v17 dpi: 0 means work it out",
                Math.Abs(Resolve(0f, 96, 3840) - Auto(96, 3840)) < 1e-4f, Resolve(0f, 96, 3840).ToString("0.##"));
            Chk("v17 dpi: 8K at 100% is scaled up most",
                Auto(96, 7680) > Auto(96, 3840) && Auto(96, 7680) <= Max,
                $"{Auto(96, 7680):0.##} vs {Auto(96, 3840):0.##}");
            int[] widths = { 1280, 1920, 2560, 3440, 3840, 5120, 7680 };
            bool monotone = true;
            for (int i = 1; i < widths.Length; i++)
                if (Auto(96, widths[i]) < Auto(96, widths[i - 1])) monotone = false;
            Chk("v17 dpi: a wider screen never gets a SMALLER interface", monotone,
                string.Join(" ", widths.Select(w => $"{w}:{Auto(96, w):0.##}")));
            bool fits = true;
            foreach (var w in widths)
                if (180 * Auto(96, w) + 260 * Auto(96, w) > w * 0.75f) fits = false;
            Chk("v17 dpi: the two panels still leave most of the screen for the viewport", fits,
                string.Join(" ", widths.Select(w => $"{w}:{(int)((180 + 260) * Auto(96, w))}px")));

            Chk("v17 dpi: a silly saved value is clamped",
                Resolve(99f, 96, 1920) <= Max && Resolve(0.01f, 96, 1920) >= Min,
                $"{Resolve(99f, 96, 1920):0.##} / {Resolve(0.01f, 96, 1920):0.##}");
            return fails;
        }
    }
}

