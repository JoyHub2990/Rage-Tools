using System;
using System.Collections.Generic;
using CodeWalker;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private const float WorldPickBoxSlack = 0.5f;
        private float worldPickEntityDist = float.MaxValue;

        private bool pickMapPrinted;
        private float pickMapScaleX = 1.0f, pickMapScaleY = 1.0f;

        private void WorldPickScreenToDevice(float mx, float my, out float sx, out float sy)
        {
            int cw = ClientSize.Width, ch = ClientSize.Height;
            int dw = deviceResources?.Width ?? cw, dh = deviceResources?.Height ?? ch;
            pickMapScaleX = (cw > 0 && dw > 0 && cw != dw) ? dw / (float)cw : 1.0f;
            pickMapScaleY = (ch > 0 && dh > 0 && ch != dh) ? dh / (float)ch : 1.0f;
            if (!pickMapPrinted)
            {
                pickMapPrinted = true;
                Console.WriteLine($"PICKMAP client {cw}x{ch} swapchain {dw}x{dh} dpi {DeviceDpi} scale {pickMapScaleX:0.###},{pickMapScaleY:0.###}" +
                                  (pickMapScaleX != 1.0f || pickMapScaleY != 1.0f ? "  (CORRECTED)" : "  (1:1)"));
            }
            sx = mx * pickMapScaleX;
            sy = my * pickMapScaleY;
        }

        private YmapEntityDef WorldPickEntityBoxes(Ray ray, YmapEntityDef surface, float surfaceDist)
        {
            float limit = surface != null ? surfaceDist + WorldPickBoxSlack : float.MaxValue;
            var candidates = World.Visible;
            YmapEntityDef best = null; float bestRadius = float.MaxValue; float bestDist = float.MaxValue;
            YmapEntityDef inside = null; float insideRadius = float.MaxValue;
            if (pickDebugRecording) pickDebugBoxes.Clear();
            if (candidates != null)
            {
                foreach (var e in candidates)
                {
                    var arche = e?.Archetype;
                    if (arche == null) continue;
                    if (e.MloInstance != null) continue;
                    if (!EntityBoxHit(ray, e, arche, out float d)) continue;
                    if (d > limit) continue;
                    if (limit < float.MaxValue)
                    {
                        var centre = e.Position + e.Orientation.Multiply(arche.BSCenter * e.Scale);
                        if (Vector3.Dot(centre - ray.Position, ray.Direction) > limit) continue;
                    }
                    float r = arche.BSRadius * MaxScale(e.Scale);
                    if (pickDebugRecording && pickDebugBoxes.Count < PickDebugMaxBoxes)
                        pickDebugBoxes.Add(new PickDebugBox { Entity = e, Dist = d, Radius = r });
                    if (d <= 0.0f)
                    {
                        if (r < insideRadius) { insideRadius = r; inside = e; }
                        continue;
                    }
                    if (r < bestRadius || (r == bestRadius && d < bestDist)) { bestRadius = r; best = e; bestDist = d; }
                }
            }
            if (surface != null && surface.MloInstance == null)
            {
                float sr = (surface.Archetype?.BSRadius ?? 0.5f) * MaxScale(surface.Scale);
                if (best == null || sr < bestRadius) { best = surface; bestRadius = sr; bestDist = surfaceDist; }
            }
            var picked = best ?? surface ?? inside;
            worldPickEntityDist = picked == null ? float.MaxValue
                                : ReferenceEquals(picked, best) ? (bestDist < float.MaxValue ? bestDist : surfaceDist)
                                : ReferenceEquals(picked, surface) ? surfaceDist : 0.0f;
            if (pickDebugRecording)
            {
                pickDebugSurface = surface; pickDebugSurfaceDist = surfaceDist;
                pickDebugWinner = picked; pickDebugWinnerDist = worldPickEntityDist;
            }
            return picked;
        }

        private static float MaxScale(Vector3 s) => Math.Max(Math.Max(Math.Abs(s.X), Math.Abs(s.Y)), Math.Max(Math.Abs(s.Z), 1e-3f));

        private static bool EntityBoxHit(Ray ray, YmapEntityDef e, Archetype arche, out float d)
        {
            d = 0.0f;
            var orientation = e.Orientation;
            var scale = e.Scale;
            var camrel = e.Position - ray.Position;
            float ms = MaxScale(scale);
            var bsph = new BoundingSphere(camrel + orientation.Multiply(arche.BSCenter * scale), Math.Max(arche.BSRadius * ms, 0.05f));
            var localRay = new Ray(Vector3.Zero, ray.Direction);
            if (!localRay.Intersects(ref bsph)) return false;
            var orinv = Quaternion.Invert(orientation);
            var mray = new Ray(orinv.Multiply(-camrel), orinv.Multiply(ray.Direction));
            var bbox = new BoundingBox(arche.BBMin * scale, arche.BBMax * scale);
            if (bbox.Maximum.X <= bbox.Minimum.X) { var h = new Vector3(0.5f); bbox = new BoundingBox(-h, h); }
            return mray.Intersects(ref bbox, out d);
        }

        private struct PickDebugBox { public YmapEntityDef Entity; public float Dist, Radius; }
        private const int PickDebugMaxBoxes = 64;
        private readonly List<PickDebugBox> pickDebugBoxes = new List<PickDebugBox>();
        private bool pickDebugRecording;
        private bool pickDebugValid;
        private Ray pickDebugRay;
        private WorldSelectionMode pickDebugMode;
        private YmapEntityDef pickDebugSurface, pickDebugWinner;
        private float pickDebugSurfaceDist, pickDebugWinnerDist;
        private WorldSelection pickDebugHit;
        private string pickDebugText = "";

        private void PickDebugRecordHit(in WorldSelection hit)
        {
            pickDebugValid = true;
            pickDebugHit = hit;
            var s = new System.Text.StringBuilder();
            s.Append(pickDebugMode).Append(": ");
            s.Append(hit.HasValue ? hit.GetNameString("?") : "nothing");
            if (hit.HasValue && hit.HitDist < float.MaxValue) s.Append($" at {hit.HitDist:0.0} m");
            if (pickDebugMode == WorldSelectionMode.Entity || pickDebugMode == WorldSelectionMode.EntityPrecision)
            {
                s.Append($" | surface {(pickDebugSurface?.Archetype?.Name ?? "-")}");
                if (pickDebugSurface != null) s.Append($" at {pickDebugSurfaceDist:0.0} m");
                s.Append($" | {pickDebugBoxes.Count} box(es) in front");
                if (pickDebugWinner != null) s.Append($", smallest {pickDebugWinner.Archetype?.Name} r {pickDebugWinner.Archetype?.BSRadius:0.0}");
            }
            pickDebugText = s.ToString();
            if (Environment.GetEnvironmentVariable("RLE_DUMPPICK") == "1") Console.WriteLine("PICKDBG " + pickDebugText);
        }

        private void DrawPickDebug()
        {
            if (!pickDebugValid || panel == null || !panel.ShowPickDebug) return;
            var rayCol = T(UiTheme.Warn, 0.9f);
            var candCol = T(UiTheme.Muted, 0.45f);
            var winCol = T(UiTheme.Ok, 1.0f);
            var surfCol = T(UiTheme.AccentBright, 0.9f);
            float hitDist = pickDebugHit.HasValue && pickDebugHit.HitDist < float.MaxValue ? pickDebugHit.HitDist
                          : pickDebugSurface != null ? pickDebugSurfaceDist : 300.0f;
            if (hitDist <= 0.0f || hitDist > 5000.0f) hitDist = 300.0f;
            var o = pickDebugRay.Position; var dir = pickDebugRay.Direction;
            var hitP = o + dir * hitDist;
            const float dash = 0.5f;
            for (float t = 1.0f; t < hitDist; t += dash * 2)
            {
                float t2 = Math.Min(t + dash, hitDist);
                lineRenderer.AddLine(o + dir * t, o + dir * t2, rayCol);
            }
            float m = Math.Max(hitDist * 0.02f, 0.15f);
            lineRenderer.AddLine(hitP - Vector3.UnitX * m, hitP + Vector3.UnitX * m, winCol);
            lineRenderer.AddLine(hitP - Vector3.UnitY * m, hitP + Vector3.UnitY * m, winCol);
            lineRenderer.AddLine(hitP - Vector3.UnitZ * m, hitP + Vector3.UnitZ * m, winCol);
            lineRenderer.AddSphere(hitP, m * 0.6f, winCol, 12);
            if (pickDebugSurface != null && Math.Abs(pickDebugSurfaceDist - hitDist) > 0.05f)
            {
                var sp = o + dir * pickDebugSurfaceDist;
                lineRenderer.AddSphere(sp, m * 0.5f, surfCol, 10);
            }
            foreach (var b in pickDebugBoxes)
            {
                if (b.Entity == null) continue;
                bool win = ReferenceEquals(b.Entity, pickDebugWinner);
                DrawEntityBox(b.Entity, win ? winCol : candCol);
            }
            if (pickDebugWinner != null)
            {
                bool listed = false;
                foreach (var b in pickDebugBoxes) if (ReferenceEquals(b.Entity, pickDebugWinner)) { listed = true; break; }
                if (!listed) DrawEntityBox(pickDebugWinner, winCol);
            }
            DrawWorldLabel(hitP + new Vector3(0, 0, m * 3.0f), pickDebugText, new Vector4(1.0f, 0.95f, 0.75f, 0.95f));
        }
    }
}

