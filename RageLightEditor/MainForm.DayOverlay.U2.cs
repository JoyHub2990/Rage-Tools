using System;
using System.Globalization;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using RageLightEditor.Rendering;
using SharpDX;
using SharpDX.Direct3D11;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private static readonly bool legacyOverlay_U2 =
            Environment.GetEnvironmentVariable("RLE_U2_LEGACY") == "1";
        private static readonly bool noMark_U2 =
            Environment.GetEnvironmentVariable("RLE_U2_NOMARK") == "1";

        private DisplayOverlayPass_U2 dayOverlay_U2;
        private bool navDeferred_U2;
        private int dayOverlayFrames_U2;

        private bool NavDrawDeferred_U2() => !legacyOverlay_U2 && !navDeferred_U2;

        private bool NavDrawSuppressed_U2() => noMark_U2;

        partial void DrawDayOverlays_U2(DeviceContext context)
        {
            if (legacyOverlay_U2 || noMark_U2) return;
            if (panel == null || !panel.NavMode || NavEd == null || NavEd.Docs.Count == 0) return;
            if (deviceResources == null) return;

            dayOverlay_U2 ??= new DisplayOverlayPass_U2(deviceResources.Device);
            dayOverlay_U2.Snapshot(context, deviceResources);
            if (!dayOverlay_U2.Ready) return;

            navRenderer ??= new NavMeshRenderer(deviceResources.Device);
            navRenderer.Display_U2 = dayOverlay_U2;
            navRenderer.DisplayDepth_U2 = deviceResources.DepthSRV;
            navDeferred_U2 = true;
            try { DrawNavMesh_P4(context); }
            finally
            {
                navDeferred_U2 = false;
                navRenderer.Display_U2 = null;
                navRenderer.DisplayDepth_U2 = null;
                dayOverlay_U2.Unbind(context);
            }
            dayOverlayFrames_U2++;
        }

        partial void NavOverlayInk_U2(ref Vector4 sel, ref Vector4 selLine, ref Vector4 draft)
        {
            if (!navDeferred_U2) return;
            static Vector4 Flat(Vector4 c)
            {
                float m = Math.Max(1.0f, Math.Max(c.X, Math.Max(c.Y, c.Z)));
                return new Vector4(c.X / m, c.Y / m, c.Z / m, c.W);
            }
            sel = Flat(sel); selLine = Flat(selLine); draft = Flat(draft);
        }

        partial void PrecisionTint_U2(ref uint tint)
        {
            if (noMark_U2) { tint = 0u; return; }
            if (legacyOverlay_U2) return;
            if (tint == 5u) tint = 7u;
            else if (tint == 6u) tint = 8u;
        }

        partial void PrecisionOverlaySuppressed_U2(ref bool suppressed)
        {
            if (noMark_U2) suppressed = true;
        }

        private bool dayReportSaid_U2;
        partial void OnWorldTick_U2()
        {
            if (dayReportSaid_U2 || Environment.GetEnvironmentVariable("RLE_U2_REPORT") != "1") return;
            if (!worldBuilt && !(panel?.NavMode ?? false)) return;
            if (dayOverlayFrames_U2 < 2 && !legacyOverlay_U2 && !noMark_U2) return;
            dayReportSaid_U2 = true;
            Console.WriteLine($"U2OVERLAY path={(noMark_U2 ? "none" : legacyOverlay_U2 ? "legacy-hdr" : "display")} " +
                              $"navFrames={dayOverlayFrames_U2} navDocs={NavEd?.Docs.Count ?? 0} " +
                              $"precTint={(noMark_U2 ? "off" : legacyOverlay_U2 ? "5/6 additive" : "7/8 multiply")} " +
                              $"hour={(panel?.PreviewHour ?? 0):0.0}");
        }

        private static readonly float expFix_U2 = ReadExpFix_U2();

        private static float ReadExpFix_U2()
        {
            var v = Environment.GetEnvironmentVariable("RLE_U2_EXPFIX");
            if (!string.IsNullOrWhiteSpace(v) &&
                float.TryParse(v, System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out float s))
                return s;
            return float.NaN;
        }

        partial void PinExposure_U2()
        {
            if (float.IsNaN(expFix_U2) || postFx == null) return;
            var eg = postFx.Vars.ExposureGame;
            eg.X = 1.0f;
            eg.Z = eg.W = expFix_U2;
            postFx.Vars.ExposureGame = eg;
        }

        partial void SeqTest_U2(Action<string, bool, string> check)
        {
            NavMeshRenderer.SelfTest_U2(check);

            float worst = 1.0f; float worstBg = -1.0f;
            for (int i = 0; i <= 100; i++)
            {
                float bg = i / 100.0f;
                foreach (var hue in new[] { new Vector3(0.35f, 0.85f, 0.35f), new Vector3(0.20f, 0.40f, 1.00f),
                                            new Vector3(1.00f, 0.16f, 0.72f), new Vector3(0.95f, 0.30f, 0.25f) })
                {
                    float d = Math.Abs(InkLuma_U2(hue, bg, NavMeshRenderer.LineGap_U2) - bg);
                    if (d < worst) { worst = d; worstBg = bg; }
                }
            }
            check("u2 ink: the mark separates from EVERY background, not just a dark one",
                  worst > 0.25f, $"worst gap {worst:0.000} at background luma {worstBg:0.00}");

            var surf = new Vector3(0.30f, 0.29f, 0.27f);
            var mark = new Vector3(1.00f, 0.16f, 0.72f);
            var atNight = PrecisionCast_U2(surf * 0.02f, mark);
            var atNoon = PrecisionCast_U2(surf * 4.90f, mark);
            float rNight = atNight.X / Math.Max(surf.X * 0.02f, 1e-6f);
            float rNoon = atNoon.X / Math.Max(surf.X * 4.90f, 1e-6f);
            check("u2 precision: the cast on the mesh is exposure-invariant (a multiply, not an add)",
                  Math.Abs(rNight - rNoon) < 1e-4f, $"x{rNight:0.000} at 0.02 lum, x{rNoon:0.000} at 4.9 lum");
            float oldNight = 0.05f + 0.20f * 0.02f, oldNoon = 0.05f + 0.20f * 4.90f;
            check("u2 precision: WS-S4's additive rule was ~13x weaker at noon, relative to the surface",
                  (oldNight / 0.02f) / (oldNoon / 4.90f) > 10.0f,
                  $"relative strength {(oldNight / 0.02f):0.0} at night vs {(oldNoon / 4.90f):0.00} at noon");
        }

        private static float InkLuma_U2(Vector3 want, float bgl, float gap)
        {
            static float L(Vector3 c) => c.X * 0.2126f + c.Y * 0.7152f + c.Z * 0.0722f;
            float target = bgl < 0.5f ? Math.Min(1.0f, bgl + gap) : Math.Max(0.0f, bgl - gap);
            float wl = Math.Max(L(want), 1e-3f);
            var ink = want * (target / wl);
            float m = Math.Max(Math.Max(Math.Max(ink.X, ink.Y), ink.Z), 1.0f);
            ink /= m;
            float il = L(ink);
            float miss = Math.Clamp((bgl < 0.5f ? target - il : il - target) / Math.Max(gap, 1e-3f), 0.0f, 1.0f);
            var pole = bgl < 0.5f ? new Vector3(1, 1, 1) : Vector3.Zero;
            return L(Vector3.Lerp(ink, pole, 0.85f * miss));
        }

        private static Vector3 PrecisionCast_U2(Vector3 c, Vector3 tint)
        {
            static float L(Vector3 v) => v.X * 0.2126f + v.Y * 0.7152f + v.Z * 0.0722f;
            float lum = Math.Max(L(c), 1e-6f);
            var hue = tint / Math.Max(L(tint), 1e-4f);
            var marked = hue * (lum * 1.15f);
            return Vector3.Lerp(c, marked, 0.60f);
        }
    }
}

