using System;
using System.Collections.Generic;
using CodeWalker;
using CodeWalker.GameFiles;
using ImGuiNET;
using RageLightEditor.Editor;
using RageLightEditor.Rendering;
using SharpDX;
using SharpDX.Direct3D11;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private const float PrecStrokePx = 2.6f;
        private const float PrecOutlinePx = 1.4f;
        private const float PrecBracketPx = 26.0f;
        private const float PrecMinBoxPx = 30.0f;
        private const float PrecMarkerPx = 15.0f;

        private static readonly Vector4 PrecSelectCol = new Vector4(1.00f, 0.16f, 0.72f, 1.0f);
        private static readonly Vector4 PrecHoverCol = new Vector4(0.25f, 0.95f, 1.00f, 1.0f);
        private static readonly Vector4 PrecOutlineCol = new Vector4(0.02f, 0.02f, 0.04f, 0.85f);

        private struct PrecMark
        {
            public string Name;
            public float BoxPx;
            public float DrawnPx;
            public float StrokePx;
            public float Distance;
            public bool Marker;
        }
        private PrecMark precLastSel, precLastHover;
        private bool precHadSel, precHadHover;

        partial void DrawPrecisionOverlay_T4(DeviceContext context)
        {
            precHadSel = precHadHover = false;
            bool suppressed_U2 = false; PrecisionOverlaySuppressed_U2(ref suppressed_U2); if (suppressed_U2) return;
            if (panel == null || !panel.WorldMode || !worldBuilt || photoMode || renderingStill) return;
            if (SelMode != WorldSelectionMode.EntityPrecision) return;

            var s = WorldEdit.Selection;
            var sel = (s.CollisionBounds == null && s.MloEntityDef == null) ? s.EntityDef : null;
            var hov = worldHoverSel.EntityDef;
            if (ReferenceEquals(hov, sel)) hov = null;
            if (sel == null && hov == null) return;

            if (hov != null) precHadHover = MarkEntity_T4(hov, PrecHoverCol, false, out precLastHover);
            if (sel != null) precHadSel = MarkEntity_T4(sel, PrecSelectCol, true, out precLastSel);

            triRenderer.Flush(context, camera.ViewProjMatrix, CommonStates.BlendAlpha, CommonStates.DepthDisabled);
        }

        private bool MarkEntity_T4(YmapEntityDef e, Vector4 col, bool selected, out PrecMark mark)
        {
            mark = default;
            if (e == null) return false;

            Vector3 mn, mx;
            var arche = e.Archetype;
            if (arche != null && arche.BBMax.X > arche.BBMin.X) { mn = arche.BBMin * e.Scale; mx = arche.BBMax * e.Scale; }
            else { var h = new Vector3(0.5f); mn = -h; mx = h; }
            var ori = e.Orientation; var pos = e.Position;
            Vector3 W(float x, float y, float z) => pos + ori.Multiply(new Vector3(x, y, z));

            var c = new Vector3[8];
            c[0] = W(mn.X, mn.Y, mn.Z); c[1] = W(mx.X, mn.Y, mn.Z);
            c[2] = W(mx.X, mx.Y, mn.Z); c[3] = W(mn.X, mx.Y, mn.Z);
            c[4] = W(mn.X, mn.Y, mx.Z); c[5] = W(mx.X, mn.Y, mx.Z);
            c[6] = W(mx.X, mx.Y, mx.Z); c[7] = W(mn.X, mx.Y, mx.Z);
            var centre = W((mn.X + mx.X) * 0.5f, (mn.Y + mx.Y) * 0.5f, (mn.Z + mx.Z) * 0.5f);

            mark.Name = arche?.Name ?? e._CEntityDef.archetypeName.ToString();
            mark.Distance = (centre - camera.Position).Length();
            mark.StrokePx = PrecStrokePx;

            float minx = float.MaxValue, miny = float.MaxValue, maxx = float.MinValue, maxy = float.MinValue;
            int onScreen = 0;
            for (int i = 0; i < 8; i++)
            {
                if (!GizmoStyle.Project(camera, c[i], out var p)) continue;
                onScreen++;
                minx = Math.Min(minx, p.X); maxx = Math.Max(maxx, p.X);
                miny = Math.Min(miny, p.Y); maxy = Math.Max(maxy, p.Y);
            }
            float boxPx = onScreen > 0 ? Math.Max(maxx - minx, maxy - miny) : 0.0f;
            mark.BoxPx = boxPx;

            bool useMarker = onScreen < 2 || boxPx < PrecMinBoxPx;
            if (useMarker)
            {
                if (!GizmoStyle.Project(camera, centre, out _)) return false;
                DrawPrecMarker_T4(centre, col, selected);
                mark.Marker = true;
                mark.DrawnPx = PrecMarkerPx * 2.0f;
                DrawPrecLabel_T4(centre, PrecMarkerPx + 6.0f, mark, col, selected);
                return true;
            }

            int[,] edges =
            {
                {0,1},{1,2},{2,3},{3,0},
                {4,5},{5,6},{6,7},{7,4},
                {0,4},{1,5},{2,6},{3,7},
            };
            for (int i = 0; i < 12; i++)
            {
                var a = c[edges[i, 0]];
                var b = c[edges[i, 1]];
                var d = b - a;
                float len = d.Length();
                if (len < 1e-4f) continue;
                float wppA = camera.WorldPerPixel(a), wppB = camera.WorldPerPixel(b);
                float tA = Math.Min(PrecBracketPx * wppA / len, 0.5f);
                float tB = Math.Min(PrecBracketPx * wppB / len, 0.5f);
                if (tA + tB >= 0.999f) { PrecStroke_T4(a, b, col, selected); continue; }
                PrecStroke_T4(a, a + d * tA, col, selected);
                PrecStroke_T4(b, b - d * tB, col, selected);
            }
            mark.DrawnPx = boxPx;
            DrawPrecLabel_T4(centre, (maxy - miny) * 0.5f + 6.0f, mark, col, selected);
            return true;
        }

        private void PrecStroke_T4(Vector3 a, Vector3 b, Vector4 col, bool selected)
        {
            var mid = (a + b) * 0.5f;
            float wpp = camera.WorldPerPixel(mid);
            float feather = 1.0f * wpp;
            float half = PrecStrokeHalfWorld_T4(mid, selected);
            var cp = camera.Position;
            triRenderer.AddThickLineAA(a, b, cp, half + PrecOutlinePx * wpp, feather, PrecOutlineCol);
            triRenderer.AddThickLineAA(a, b, cp, half, feather, col);
        }

        private float PrecStrokeHalfWorld_T4(Vector3 at, bool selected) =>
            PrecStrokePx * (selected ? 0.5f : 0.42f) * camera.WorldPerPixel(at);

        private void DrawPrecMarker_T4(Vector3 centre, Vector4 col, bool selected)
        {
            var (right, up, _) = GizmoStyle.ScreenBasis(camera);
            float wpp = camera.WorldPerPixel(centre);
            float r = PrecMarkerPx * wpp;
            float arm = r * 0.55f;
            var rx = right * r; var uy = up * r;
            var ax = right * arm; var ay = up * arm;
            var tl = centre - rx + uy; var tr = centre + rx + uy;
            var bl = centre - rx - uy; var br = centre + rx - uy;
            PrecStroke_T4(tl, tl + ax, col, selected); PrecStroke_T4(tl, tl - ay, col, selected);
            PrecStroke_T4(tr, tr - ax, col, selected); PrecStroke_T4(tr, tr - ay, col, selected);
            PrecStroke_T4(bl, bl + ax, col, selected); PrecStroke_T4(bl, bl + ay, col, selected);
            PrecStroke_T4(br, br - ax, col, selected); PrecStroke_T4(br, br + ay, col, selected);
            float pip = 1.6f * wpp;
            PrecStroke_T4(centre - right * pip, centre + right * pip, col, selected);
        }

        private void DrawPrecLabel_T4(Vector3 centre, float offsetPx, in PrecMark mark, Vector4 col, bool selected)
        {
            if (ImGui.GetCurrentContext() == IntPtr.Zero) return;
            if (!GizmoStyle.Project(camera, centre, out var p)) return;
            string text = mark.Name ?? "?";
            if (selected) text += $"   {mark.Distance:0.#} m";

            var ds = ImGui.GetIO().DisplaySize;
            var size = ImGui.CalcTextSize(text);
            float pad = 4.0f;
            var at = new System.Numerics.Vector2(p.X - size.X * 0.5f, p.Y - offsetPx - size.Y - pad * 2);
            at.X = Math.Clamp(at.X, 4.0f, Math.Max(4.0f, ds.X - size.X - pad * 2 - 4.0f));
            at.Y = Math.Clamp(at.Y, 4.0f, Math.Max(4.0f, ds.Y - size.Y - pad * 2 - 4.0f));

            var dl = ImGui.GetBackgroundDrawList();
            var a = new System.Numerics.Vector2(at.X - pad, at.Y - pad);
            var b = new System.Numerics.Vector2(at.X + size.X + pad, at.Y + size.Y + pad);
            dl.AddRectFilled(a, b, ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(0.03f, 0.03f, 0.05f, selected ? 0.82f : 0.60f)), 3.0f);
            dl.AddRect(a, b, ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(col.X, col.Y, col.Z, selected ? 0.95f : 0.65f)), 3.0f);
            dl.AddText(at, ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(1f, 1f, 1f, selected ? 1f : 0.82f)), text);
        }

        partial void PrecisionOwnsBox_T4(YmapEntityDef e, ref bool owned)
        {
            if (e == null || panel == null || !panel.WorldMode || photoMode) return;
            if (SelMode == WorldSelectionMode.EntityPrecision) owned = true;
        }

        private bool precCapDone_T4;
        private int precCapHold_T4;
        private YmapEntityDef precCapHover_T4;

        partial void OnWorldTick_T4()
        {
            ServicePrecisionCap_T4();
            ServiceRpfDrop_T4();
            ServiceRpfSideButtonProbe_U9();
            ServiceRpfDeleteKey_T4();
        }

        private void ServicePrecisionCap_T4()
        {
            var env = Environment.GetEnvironmentVariable("RLE_PRECISIONCAP");
            if (string.IsNullOrEmpty(env)) return;
            if (precCapHold_T4 > 0)
            {
                precCapHold_T4--;
                if (screenshotPath != null) worldWarmup = Math.Min(worldWarmup, 380);
                if (precCapHover_T4 != null) worldHoverSel = WorldSelection.FromProjectObject(precCapHover_T4);
                if (precCapHold_T4 == 0) ReportPrecisionCap_T4();
                return;
            }
            if (precCapDone_T4 || !worldBuilt || panel == null || deviceResources == null) return;
            if (screenshotPath != null && worldWarmup < 300) return;
            precCapDone_T4 = true;

            if (!panel.WorldMode) panel.SwitchWorkspace(LightPanel.Space.World);
            int mi = LightPanel.IndexOfMode(WorldSelectionMode.EntityPrecision);
            if (mi >= 0) panel.SelectionMode = mi;
            panel.MouseSelectEnabled = true;

            var bits = env.Split(',');
            float want = 50.0f;
            float.TryParse(bits[0], System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture, out want);
            if (want < 0.5f) want = 50.0f;
            string wantName = bits.Length > 1 ? bits[1].Trim() : "";

            YmapEntityDef e;
            if (wantName.Length > 0)
            {
                e = NearestNamed_T4(wantName);
                if (e != null) WorldEdit.Select(e);
            }
            else
            {
                WorldPickAt(deviceResources.Width / 2, deviceResources.Height / 2);
                e = WorldEdit.Selection.EntityDef;
            }
            if (e == null)
            {
                Console.WriteLine($"PRECISIONCAP nothing to mark ({(wantName.Length > 0 ? "no '" + wantName + "' in the streamed world" : "nothing under the crosshair")})");
                return;
            }
            var arche = e.Archetype;
            Vector3 mn = arche != null && arche.BBMax.X > arche.BBMin.X ? arche.BBMin * e.Scale : new Vector3(-0.5f);
            Vector3 mx = arche != null && arche.BBMax.X > arche.BBMin.X ? arche.BBMax * e.Scale : new Vector3(0.5f);
            var centre = e.Position + e.Orientation.Multiply((mn + mx) * 0.5f);

            var eye = centre - camera.GetForward() * want;
            CameraSequence.ApplyToCamera(camera, eye, camera.Yaw, camera.Pitch, settings.FovDeg);
            precCapHold_T4 = 150;
            if (Array.Exists(bits, b => b.Trim().Equals("hover", StringComparison.OrdinalIgnoreCase)) && wantName.Length > 0)
            {
                precCapHover_T4 = NearestNamed_T4(wantName, e);
                Console.WriteLine("PRECISIONCAP hover on '" + (precCapHover_T4?.Archetype?.Name ?? "nothing else matched") + "'");
            }
            Console.WriteLine($"PRECISIONCAP picked '{arche?.Name ?? "?"}' box=({mx.X - mn.X:0.#} x {mx.Y - mn.Y:0.#} x {mx.Z - mn.Z:0.#}) m, " +
                              $"camera pulled to {want:0.#} m");
        }

        private YmapEntityDef NearestNamed_T4(string fragment, YmapEntityDef except = null)
        {
            YmapEntityDef best = null; float bestD = float.MaxValue;
            foreach (var e in World.Visible)
            {
                if (ReferenceEquals(e, except)) continue;
                var n = e?.Archetype?.Name;
                if (string.IsNullOrEmpty(n) || n.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) < 0) continue;
                float d = (e.Position - camera.Position).Length();
                if (d < bestD) { bestD = d; best = e; }
            }
            return best;
        }

        private void ReportPrecisionCap_T4()
        {
            if (!precHadSel) { Console.WriteLine("PRECISIONCAP the overlay drew nothing"); return; }
            var m = precLastSel;
            Console.WriteLine($"PRECISIONCAP '{m.Name}' at {m.Distance:0.#} m: box {m.BoxPx:0.#} px on screen, " +
                              $"drawn {m.DrawnPx:0.#} px ({(m.Marker ? "compact marker" : "corner brackets")}), stroke {m.StrokePx:0.0} px");
            if (screenshotPath != null) screenshotFrames = Math.Max(screenshotFrames, 3);
        }
    }
}

