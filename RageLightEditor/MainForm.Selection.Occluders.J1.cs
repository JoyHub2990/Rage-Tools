using System;
using System.Collections.Generic;
using CodeWalker;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using RageLightEditor.Rendering;
using SharpDX;
using SharpDX.Direct3D11;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private const int OccluderDrawMax = 60;
        private const float OccEdgePx = 1.2f, OccEdgeFocusPx = 2.2f, OccEdgeFocusModelPx = 1.6f;
        private const float OccFeatureCos = 0.88f;

        private struct OccluderCandidate
        {
            public YmapBoxOccluder Box;
            public YmapOccludeModel Model;
            public float Dist;
        }
        private readonly List<OccluderCandidate> occCandidates = new List<OccluderCandidate>();

        private sealed class OccModelEdges
        {
            public Vector3[] Verts; public byte[] Inds; public Vector3[] Edges;
        }
        private readonly Dictionary<YmapOccludeModel, OccModelEdges> occEdgeCache = new Dictionary<YmapOccludeModel, OccModelEdges>();
        private readonly Dictionary<(int, int), (Vector3 n, int count, bool feature)> occEdgeMap = new Dictionary<(int, int), (Vector3, int, bool)>();

        private static Vector4 OccBoxEdge => T(UiTheme.Accent, 0.95f);
        private static Vector4 OccBoxFill(float a) => TF(UiTheme.Accent, a);
        private static readonly Vector4 OccModelEdge = C(1.0f, 0.42f, 0.32f, 0.95f);
        private static Vector4 OccModelFill(float a) => F(0.95f, 0.40f, 0.32f, a);
        private static Vector4 OccHoverEdge => T(UiTheme.AccentBright, 1.0f);
        private static Vector4 OccHoverFill(float a) => TF(UiTheme.AccentBright, a);
        private static Vector4 OccSelEdge => SelColour;
        private static Vector4 OccSelFill(float a) => F(1.0f, 0.78f, 0.30f, a);

        private float OccluderFillAlpha()
        {
            if (!occFillEnvRead)
            {
                occFillEnvRead = true;
                var env = Environment.GetEnvironmentVariable("RLE_OCCFILL");
                if (!string.IsNullOrEmpty(env) && float.TryParse(env, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f))
                    panel.OccluderFill = Math.Clamp(f, 0f, 0.5f);
            }
            return Math.Clamp(panel.OccluderFill, 0f, 0.5f);
        }
        private bool occFillEnvRead;

        private void CollectOccluderCandidates(Vector3 camPos)
        {
            occCandidates.Clear();
            World.SnapshotWalkedYmaps(selYmaps);
            foreach (var ymap in selYmaps)
            {
                var bos = ymap.BoxOccluders;
                if (bos != null)
                {
                    foreach (var bo in bos)
                    {
                        if (bo == null) continue;
                        float d = (bo.Position - camPos).Length();
                        if (d > SelMaxDist) continue;
                        occCandidates.Add(new OccluderCandidate { Box = bo, Dist = Math.Max(d - bo.Size.Length() * 0.5f, 0f) });
                    }
                }
                var oms = ymap.OccludeModels;
                if (oms != null)
                {
                    foreach (var om in oms)
                    {
                        if (om?.Vertices == null || om.Indices == null) continue;
                        var m = om._OccludeModel;
                        var c = (m.bmin + m.bmax) * 0.5f;
                        float d = (c - camPos).Length();
                        if (d > SelMaxDist) continue;
                        occCandidates.Add(new OccluderCandidate { Model = om, Dist = Math.Max(d - (m.bmax - m.bmin).Length() * 0.5f, 0f) });
                    }
                }
            }
            if (occCandidates.Count > OccluderDrawMax)
            {
                occCandidates.Sort((a, b) => a.Dist.CompareTo(b.Dist));
                occCandidates.RemoveRange(OccluderDrawMax, occCandidates.Count - OccluderDrawMax);
            }
        }

        private Vector3[] OccludeModelFeatureEdges(YmapOccludeModel om)
        {
            var v = om.Vertices; var ix = om.Indices;
            if (occEdgeCache.TryGetValue(om, out var cached) && ReferenceEquals(cached.Verts, v) && ReferenceEquals(cached.Inds, ix))
                return cached.Edges;
            occEdgeMap.Clear();
            for (int i = 0; i + 2 < ix.Length; i += 3)
            {
                int a = ix[i], b = ix[i + 1], c = ix[i + 2];
                if (a >= v.Length || b >= v.Length || c >= v.Length) continue;
                var n = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
                if (n.LengthSquared() > 1e-12f) n.Normalize();
                Fold(a, b, n); Fold(b, c, n); Fold(c, a, n);
            }
            void Fold(int p, int q, Vector3 n)
            {
                var key = p < q ? (p, q) : (q, p);
                if (occEdgeMap.TryGetValue(key, out var e))
                {
                    bool feature = e.feature || e.count >= 2 || Math.Abs(Vector3.Dot(e.n, n)) < OccFeatureCos;
                    occEdgeMap[key] = (e.n, e.count + 1, feature);
                }
                else occEdgeMap[key] = (n, 1, false);
            }
            var edges = new List<Vector3>();
            foreach (var kv in occEdgeMap)
            {
                if (kv.Value.count == 1 || kv.Value.feature)
                {
                    edges.Add(v[kv.Key.Item1]); edges.Add(v[kv.Key.Item2]);
                }
            }
            var arr = edges.ToArray();
            occEdgeCache[om] = new OccModelEdges { Verts = v, Inds = ix, Edges = arr };
            if (occEdgeCache.Count > 512) occEdgeCache.Clear();
            return arr;
        }

        private void DrawOccluderGeometry_J1(DeviceContext context)
        {
            var camPos = camera.Position;
            CollectOccluderCandidates(camPos);
            if (occCandidates.Count == 0) return;
            float fillA = OccluderFillAlpha();
            var hoverBox = worldHoverSel.BoxOccluder;
            var hoverModel = worldHoverSel.OccludeModelTri?.Model;
            var selBox = WorldEdit.Selection.BoxOccluder;
            var selModel = WorldEdit.Selection.OccludeModelTri?.Model;

            int tris = 0;
            if (fillA > 0.005f)
            {
                foreach (var cand in occCandidates)
                {
                    if (cand.Box != null)
                    {
                        var bo = cand.Box;
                        bool sel = ReferenceEquals(bo, selBox), hov = !sel && ReferenceEquals(bo, hoverBox);
                        float a = sel || hov ? Math.Min(fillA * 2.2f, 0.6f) : fillA;
                        var col = sel ? OccSelFill(a) : hov ? OccHoverFill(a) : OccBoxFill(a);
                        BoxCorners(bo, out var v0, out var v1, out var v2, out var v3, out var v4, out var v5, out var v6, out var v7);
                        triRenderer.AddQuad(v0, v1, v3, v2, col); triRenderer.AddQuad(v4, v6, v7, v5, col);
                        triRenderer.AddQuad(v0, v4, v5, v1, col); triRenderer.AddQuad(v2, v3, v7, v6, col);
                        triRenderer.AddQuad(v0, v2, v6, v4, col); triRenderer.AddQuad(v1, v5, v7, v3, col);
                        tris += 12;
                    }
                    else
                    {
                        var om = cand.Model;
                        bool sel = ReferenceEquals(om, selModel), hov = !sel && ReferenceEquals(om, hoverModel);
                        float a = sel || hov ? Math.Min(fillA * 2.0f, 0.6f) : fillA * 0.9f;
                        var col = sel ? OccSelFill(a) : hov ? OccHoverFill(a) : OccModelFill(a);
                        var v = om.Vertices; var ix = om.Indices;
                        for (int i = 0; i + 2 < ix.Length; i += 3)
                        {
                            if (ix[i] >= v.Length || ix[i + 1] >= v.Length || ix[i + 2] >= v.Length) continue;
                            triRenderer.AddTri(v[ix[i]], v[ix[i + 1]], v[ix[i + 2]], col);
                            tris++;
                        }
                    }
                }
                if (tris > 0) triRenderer.Flush(context, camera.ViewProjMatrix, CommonStates.BlendAlpha, CommonStates.DepthReadOnly);
            }

            int edgeTris = 0;
            foreach (var cand in occCandidates)
            {
                if (cand.Box != null)
                {
                    var bo = cand.Box;
                    bool sel = ReferenceEquals(bo, selBox), hov = !sel && ReferenceEquals(bo, hoverBox);
                    var col = sel ? OccSelEdge : hov ? OccHoverEdge : OccBoxEdge;
                    float px = sel || hov ? OccEdgeFocusPx : OccEdgePx;
                    BoxCorners(bo, out var v0, out var v1, out var v2, out var v3, out var v4, out var v5, out var v6, out var v7);
                    OccEdge(v0, v1, col, px); OccEdge(v2, v3, col, px); OccEdge(v4, v5, col, px); OccEdge(v6, v7, col, px);
                    OccEdge(v0, v2, col, px); OccEdge(v1, v3, col, px); OccEdge(v4, v6, col, px); OccEdge(v5, v7, col, px);
                    OccEdge(v0, v4, col, px); OccEdge(v1, v5, col, px); OccEdge(v2, v6, col, px); OccEdge(v3, v7, col, px);
                    edgeTris += 12;
                }
                else
                {
                    var om = cand.Model;
                    bool sel = ReferenceEquals(om, selModel), hov = !sel && ReferenceEquals(om, hoverModel);
                    var col = sel ? OccSelEdge : hov ? OccHoverEdge : OccModelEdge;
                    float px = sel || hov ? OccEdgeFocusModelPx : OccEdgePx;
                    var edges = OccludeModelFeatureEdges(om);
                    for (int i = 0; i + 1 < edges.Length; i += 2) OccEdge(edges[i], edges[i + 1], col, px);
                    edgeTris += edges.Length / 2;
                }
            }
            if (edgeTris > 0) triRenderer.Flush(context, camera.ViewProjMatrix, CommonStates.BlendAlpha, CommonStates.DepthDisabled);
        }

        private void OccEdge(Vector3 a, Vector3 b, Vector4 col, float px)
        {
            float wpp = camera.WorldPerPixel((a + b) * 0.5f);
            triRenderer.AddThickLineAA(a, b, camera.Position, px * 0.5f * wpp, 1.2f * wpp, col);
        }

        private static void BoxCorners(YmapBoxOccluder bo, out Vector3 v0, out Vector3 v1, out Vector3 v2, out Vector3 v3,
                                       out Vector3 v4, out Vector3 v5, out Vector3 v6, out Vector3 v7)
        {
            var s = bo.Size * 0.5f;
            var o = bo.Orientation; var p = bo.Position;
            Vector3 X(float x, float y, float z) => o.Multiply(new Vector3(x, y, z)) + p;
            v0 = X(-s.X, -s.Y, -s.Z); v1 = X(-s.X, -s.Y, s.Z); v2 = X(-s.X, s.Y, -s.Z); v3 = X(-s.X, s.Y, s.Z);
            v4 = X(s.X, -s.Y, -s.Z); v5 = X(s.X, -s.Y, s.Z); v6 = X(s.X, s.Y, -s.Z); v7 = X(s.X, s.Y, s.Z);
        }
    }
}

