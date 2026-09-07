using System;
using System.Collections.Generic;
using CodeWalker;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Buffer = SharpDX.Direct3D11.Buffer;
using Device = SharpDX.Direct3D11.Device;

namespace RageLightEditor.Rendering
{
    public partial class NavMeshRenderer : IDisposable
    {
        private sealed class Batch
        {
            public Buffer Tris; public int TriCount;
            public Buffer Lines; public int LineCount;
            public int Polys;
            public int Version = -1;
            public int LayerStamp = -1;
            public void Dispose() { Tris?.Dispose(); Lines?.Dispose(); Tris = Lines = null; TriCount = LineCount = Polys = 0; }
        }

        private readonly Device device;
        private readonly ShaderSet shader;
        private readonly ConstantBuffer<LineVars> cbuffer;
        private readonly Dictionary<NavMeshEditor.NavDoc, Batch> batches = new Dictionary<NavMeshEditor.NavDoc, Batch>();
        private readonly List<NavMeshEditor.NavDoc> dropped = new List<NavMeshEditor.NavDoc>();

        public float PullToCamera = 0.012f;
        public float FillHdr = Legacy_U2 ? 1.3f : 1.0f, LineHdr = Legacy_U2 ? 2.4f : 1.0f;

        public int PolysDrawn, TrisDrawn, LinesDrawn, FilesDrawn, Rebuilds;

        public NavMeshRenderer(Device device)
        {
            this.device = device;
            shader = new ShaderSet(device, "lines.hlsl", new[]
            {
                new InputElement("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                new InputElement("COLOR", 0, Format.R32G32B32A32_Float, 12, 0),
            });
            cbuffer = new ConstantBuffer<LineVars>(device);
        }

        public void Invalidate(NavMeshEditor.NavDoc doc)
        {
            if (doc != null && batches.TryGetValue(doc, out var b)) { b.Dispose(); batches.Remove(doc); }
        }

        public void Clear()
        {
            foreach (var b in batches.Values) b.Dispose();
            batches.Clear();
        }

        private void Build(NavMeshEditor ed, NavMeshEditor.NavDoc doc, Batch b)
        {
            b.Dispose();
            Rebuilds++;
            var tris = new List<LineVertex>(8192);
            var lines = new List<LineVertex>(8192);
            var polys = doc?.Ynv?.Polys;

            void Tri(Vector3 a, Vector3 c, Vector3 d, Vector4 col)
            { tris.Add(new LineVertex(a, col)); tris.Add(new LineVertex(c, col)); tris.Add(new LineVertex(d, col)); }
            void Line(Vector3 a, Vector3 c, Vector4 col)
            { lines.Add(new LineVertex(a, col)); lines.Add(new LineVertex(c, col)); }

            if (polys != null)
            {
                for (int i = 0; i < polys.Count; i++)
                {
                    var p = polys[i];
                    var vs = p?.Vertices;
                    if (vs == null || vs.Length < 3) continue;
                    if (!ed.PolyVisible(p)) continue;
                    var c = NavMeshEditor.CatColours[(int)ed.CategoryOf(p)];
                    var fill = new Vector4(c.X * FillHdr, c.Y * FillHdr, c.Z * FillHdr, ed.FillAlpha);
                    var edge = new Vector4(c.X * LineHdr, c.Y * LineHdr, c.Z * LineHdr, 0.95f);

                    if (ed.ShowsAsIsolated(p))
                    {
                        var f = NavMeshEditor.CatColours[(int)NavMeshEditor.NavCat.Isolated];
                        edge = new Vector4(f.X * LineHdr, f.Y * LineHdr, f.Z * LineHdr, 1.0f);
                    }

                    if (ed.ShowFills)
                    {
                        for (int t = 0; t < vs.Length - 2; t++) Tri(vs[0], vs[t + 1], vs[t + 2], fill);
                    }
                    if (ed.ShowEdges)
                        for (int e = 0; e < vs.Length; e++) Line(vs[e], vs[(e + 1) % vs.Length], edge);

                    if (ed.ShowLinks && p.Edges != null)
                    {
                        var lift = new Vector3(0, 0, 0.10f);
                        var from = p.Position + lift;
                        for (int e = 0; e < p.Edges.Length && e < vs.Length; e++)
                        {
                            var ed2 = p.Edges[e];
                            if (ed2 == null || ed2.PolyID1 == NavMeshEditor.NoPoly) continue;
                            var mid = (vs[e] + vs[(e + 1) % vs.Length]) * 0.5f + lift;
                            bool foreign = ed2.AreaID1 != (uint)doc.Ynv.AreaID;
                            var col = foreign ? new Vector4(2.8f, 1.9f, 0.30f, 0.95f) : new Vector4(0.35f, 1.9f, 2.6f, 0.9f);
                            Line(from, mid, col);
                            if (!foreign && ed2.PolyID1 < polys.Count)
                            {
                                var n = polys[(int)ed2.PolyID1];
                                if (n != null) Line(mid, n.Position + lift, col);
                            }
                        }
                    }
                    b.Polys++;
                }
            }

            if (ed.ShowPortals && doc?.Ynv?.Portals != null)
            {
                var col = new Vector4(1.0f * FillHdr, 0.35f * FillHdr, 0.85f * FillHdr, 0.90f);
                var line = new Vector4(1.0f * LineHdr, 0.55f * LineHdr, 0.95f * LineHdr, 1.0f);
                foreach (var po in doc.Ynv.Portals)
                {
                    if (po == null) continue;
                    Line(po.PositionFrom, po.PositionTo, line);
                    AddCube(tris, po.PositionFrom, 0.22f, col);
                    AddCube(tris, po.PositionTo, 0.16f, col);
                    var dir = po.Orientation.Multiply(Vector3.UnitX);
                    if (dir.LengthSquared() > 1e-6f) Line(po.PositionFrom, po.PositionFrom + Vector3.Normalize(dir) * 0.7f, line);
                }
            }

            if (ed.ShowPoints && doc?.Ynv?.Points != null)
            {
                foreach (var pt in doc.Ynv.Points)
                {
                    if (pt == null) continue;
                    var c = PointColour(pt.Type);
                    var col = new Vector4(c.X * FillHdr, c.Y * FillHdr, c.Z * FillHdr, c.W);
                    AddCube(tris, pt.Position, 0.16f, col);
                    var dir = pt.Orientation.Multiply(Vector3.UnitX);
                    if (dir.LengthSquared() > 1e-6f)
                        Line(pt.Position, pt.Position + Vector3.Normalize(dir) * 0.8f,
                             new Vector4(c.X * LineHdr, c.Y * LineHdr, c.Z * LineHdr, 1.0f));
                }
            }

            if (tris.Count >= 3) { b.Tris = Buffer.Create(device, BindFlags.VertexBuffer, tris.ToArray()); b.TriCount = tris.Count; }
            if (lines.Count >= 2) { b.Lines = Buffer.Create(device, BindFlags.VertexBuffer, lines.ToArray()); b.LineCount = lines.Count; }
        }

        private static Vector4 PointColour(byte type)
        {
            switch (type)
            {
                case 0: return new Vector4(0.95f, 0.95f, 0.95f, 0.9f);
                case 1: return new Vector4(0.35f, 0.95f, 0.55f, 0.9f);
                case 2: return new Vector4(0.35f, 0.70f, 1.00f, 0.9f);
                case 3: return new Vector4(1.00f, 0.85f, 0.30f, 0.9f);
                case 4: return new Vector4(1.00f, 0.55f, 0.25f, 0.9f);
                case 5: return new Vector4(0.80f, 0.45f, 1.00f, 0.9f);
                default: return new Vector4(0.60f, 0.85f, 0.85f, 0.9f);
            }
        }

        internal static void AddCube(List<LineVertex> tris, Vector3 p, float h, Vector4 col)
        {
            var cTop = col;
            var cSide = new Vector4(col.X * 0.8f, col.Y * 0.8f, col.Z * 0.8f, col.W);
            var cBot = new Vector4(col.X * 0.6f, col.Y * 0.6f, col.Z * 0.6f, col.W);
            var p000 = p + new Vector3(-h, -h, -h); var p100 = p + new Vector3(h, -h, -h);
            var p010 = p + new Vector3(-h, h, -h); var p110 = p + new Vector3(h, h, -h);
            var p001 = p + new Vector3(-h, -h, h); var p101 = p + new Vector3(h, -h, h);
            var p011 = p + new Vector3(-h, h, h); var p111 = p + new Vector3(h, h, h);
            void Q(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector4 cc)
            {
                tris.Add(new LineVertex(a, cc)); tris.Add(new LineVertex(b, cc)); tris.Add(new LineVertex(c, cc));
                tris.Add(new LineVertex(a, cc)); tris.Add(new LineVertex(c, cc)); tris.Add(new LineVertex(d, cc));
            }
            Q(p001, p101, p111, p011, cTop);
            Q(p000, p010, p110, p100, cBot);
            Q(p000, p100, p101, p001, cSide);
            Q(p110, p010, p011, p111, cSide);
            Q(p100, p110, p111, p101, cSide);
            Q(p010, p000, p001, p011, cSide);
        }

        public void Draw(DeviceContext context, Matrix viewProj, Vector3 camPos, NavMeshEditor ed)
        {
            PolysDrawn = TrisDrawn = LinesDrawn = FilesDrawn = 0;
            if (ed == null || ed.Docs.Count == 0) return;
            int stamp = ed.LayerStamp();

            var list = new List<Batch>(ed.Docs.Count);
            foreach (var doc in ed.Docs)
            {
                if (!doc.Visible) continue;
                if (!batches.TryGetValue(doc, out var b)) batches[doc] = b = new Batch();
                if (b.Version != doc.Version || b.LayerStamp != stamp)
                {
                    Build(ed, doc, b);
                    b.Version = doc.Version;
                    b.LayerStamp = stamp;
                }
                list.Add(b);
                PolysDrawn += b.Polys;
            }
            dropped.Clear();
            foreach (var kv in batches) if (!ed.Docs.Contains(kv.Key)) dropped.Add(kv.Key);
            foreach (var k in dropped) { batches[k].Dispose(); batches.Remove(k); }
            if (list.Count == 0) return;

            var vars = new LineVars { ViewProj = Matrix.Transpose(viewProj), CamPull = new Vector4(camPos, PullToCamera),
                                      PullClamp = new Vector4(PullMax_T5, 0, 0, 0) };
            cbuffer.Update(context, ref vars);
            var sh = BindDisplay_U2(context) ?? shader;
            sh.Apply(context);
            context.VertexShader.SetConstantBuffer(0, cbuffer.Buffer);
            context.OutputMerger.SetBlendState(CommonStates.BlendAlpha);
            context.OutputMerger.SetDepthStencilState(ed.DrawOnTop ? CommonStates.DepthDisabled : CommonStates.DepthReadOnly);
            context.Rasterizer.State = CommonStates.RasterSolid;

            context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
            InkFor_U2(context, ed, true);
            foreach (var b in list)
            {
                if (b.Tris == null) continue;
                context.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(b.Tris, LineVertex.Stride, 0));
                context.Draw(b.TriCount, 0);
                TrisDrawn += b.TriCount / 3;
            }
            context.InputAssembler.PrimitiveTopology = PrimitiveTopology.LineList;
            InkFor_U2(context, ed, false);
            foreach (var b in list)
            {
                if (b.Lines == null) continue;
                context.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(b.Lines, LineVertex.Stride, 0));
                context.Draw(b.LineCount, 0);
                LinesDrawn += b.LineCount / 2;
            }
            FilesDrawn = list.Count;
        }

        public void Dispose()
        {
            Clear();
            cbuffer?.Dispose();
            shader?.Dispose();
        }
    }
}

