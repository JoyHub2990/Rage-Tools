using System;
using System.Collections.Generic;
using CodeWalker;
using CodeWalker.GameFiles;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Buffer = SharpDX.Direct3D11.Buffer;
using Device = SharpDX.Direct3D11.Device;

namespace RageLightEditor.Rendering
{
    public class PathBatchRenderer : IDisposable
    {
        private sealed class Batch
        {
            public Buffer Lines; public int LineCount;
            public Buffer Tris; public int TriCount;
            public Buffer Nodes; public int NodeCount;
            public int Version;
            public void Dispose() { Lines?.Dispose(); Tris?.Dispose(); Nodes?.Dispose(); Lines = Tris = Nodes = null; }
        }

        private readonly Device device;
        private readonly ShaderSet shader;
        private readonly ConstantBuffer<LineVars> cbuffer;
        private readonly Dictionary<object, Batch> batches = new Dictionary<object, Batch>(ReferenceEqualityComparer.Instance);
        private readonly List<object> unusedKeys = new List<object>();
        private readonly HashSet<object> usedThisFrame = new HashSet<object>(ReferenceEqualityComparer.Instance);

        public float PullToCamera = 0.01f;
        public Vector4 NodeColour = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
        public float NodeHalfSize = 0.25f;

        public int LinesDrawn, TrisDrawn, NodesDrawn, BatchesDrawn;

        public PathBatchRenderer(Device device)
        {
            this.device = device;
            shader = new ShaderSet(device, "lines.hlsl", new[]
            {
                new InputElement("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                new InputElement("COLOR", 0, Format.R32G32B32A32_Float, 12, 0),
            });
            cbuffer = new ConstantBuffer<LineVars>(device);
        }

        public void Invalidate(object key)
        {
            if (key == null) return;
            if (batches.TryGetValue(key, out var b)) { b.Dispose(); batches.Remove(key); }
        }

        public void Clear()
        {
            foreach (var b in batches.Values) b.Dispose();
            batches.Clear();
        }

        private static Vector4 Unpack(uint c)
        {
            return new Vector4((c & 0xFF) / 255.0f, ((c >> 8) & 0xFF) / 255.0f, ((c >> 16) & 0xFF) / 255.0f, ((c >> 24) & 0xFF) / 255.0f);
        }

        private Batch Build(BasePathData key)
        {
            var b = new Batch();
            if (key is CodeWalker.World.ScenarioRegion sr) { try { BuildScenario(sr, b); } catch (Exception ex) { Console.WriteLine("scenario batch: " + ex.Message); } return b; }
            EditorVertex[] pv = null, tv = null; Vector4[] nodes = null;
            try { pv = key.GetPathVertices(); tv = key.GetTriangleVertices(); nodes = key.GetNodePositions(); } catch { }
            if (pv != null && pv.Length >= 2)
            {
                int n = pv.Length - (pv.Length % 2);
                var lv = new LineVertex[n];
                for (int i = 0; i < n; i++) lv[i] = new LineVertex(pv[i].Position, Unpack(pv[i].Colour));
                b.Lines = Buffer.Create(device, BindFlags.VertexBuffer, lv);
                b.LineCount = n;
            }
            if (tv != null && tv.Length >= 3)
            {
                int n = tv.Length - (tv.Length % 3);
                var lv = new LineVertex[n];
                for (int i = 0; i < n; i++) lv[i] = new LineVertex(tv[i].Position, Unpack(tv[i].Colour));
                b.Tris = Buffer.Create(device, BindFlags.VertexBuffer, lv);
                b.TriCount = n;
            }
            if (nodes != null && nodes.Length > 0)
            {
                var lv = new LineVertex[nodes.Length * 36];
                float h = NodeHalfSize;
                var cTop = NodeColour;
                var cSide = new Vector4(NodeColour.X * 0.8f, NodeColour.Y * 0.8f, NodeColour.Z * 0.8f, NodeColour.W);
                var cBot = new Vector4(NodeColour.X * 0.6f, NodeColour.Y * 0.6f, NodeColour.Z * 0.6f, NodeColour.W);
                int k = 0;
                for (int i = 0; i < nodes.Length; i++)
                {
                    var p = new Vector3(nodes[i].X, nodes[i].Y, nodes[i].Z);
                    var p000 = p + new Vector3(-h, -h, -h); var p100 = p + new Vector3(h, -h, -h);
                    var p010 = p + new Vector3(-h, h, -h); var p110 = p + new Vector3(h, h, -h);
                    var p001 = p + new Vector3(-h, -h, h); var p101 = p + new Vector3(h, -h, h);
                    var p011 = p + new Vector3(-h, h, h); var p111 = p + new Vector3(h, h, h);
                    void Q(Vector3 a, Vector3 bb, Vector3 c, Vector3 d, Vector4 col)
                    {
                        lv[k++] = new LineVertex(a, col); lv[k++] = new LineVertex(bb, col); lv[k++] = new LineVertex(c, col);
                        lv[k++] = new LineVertex(a, col); lv[k++] = new LineVertex(c, col); lv[k++] = new LineVertex(d, col);
                    }
                    Q(p001, p101, p111, p011, cTop);
                    Q(p000, p010, p110, p100, cBot);
                    Q(p000, p100, p101, p001, cSide);
                    Q(p010, p011, p111, p110, cSide);
                    Q(p000, p001, p011, p010, cSide);
                    Q(p100, p110, p111, p101, cSide);
                }
                b.Nodes = Buffer.Create(device, BindFlags.VertexBuffer, lv);
                b.NodeCount = lv.Length;
            }
            return b;
        }

        public float LineHdr = 2.2f, FillHdr = 1.8f;
        public object ScenarioSelectedNode, ScenarioSelectedEdge;

        private static Vector4 SysToDx(System.Numerics.Vector4 c, float mul, float alpha) => new Vector4(c.X * mul, c.Y * mul, c.Z * mul, alpha);

        private void BuildScenario(CodeWalker.World.ScenarioRegion sr, Batch b)
        {
            var lines = new List<LineVertex>(1024);
            var tris = new List<LineVertex>(4096);
            var accent = SysToDx(Editor.UiTheme.Accent, FillHdr, 0.85f);
            var accentBright = SysToDx(Editor.UiTheme.AccentBright, FillHdr, 0.9f);
            var amber = SysToDx(Editor.UiTheme.Warn, FillHdr, 0.9f);
            var green = SysToDx(Editor.UiTheme.Ok, FillHdr, 0.85f);
            var red = SysToDx(Editor.UiTheme.Danger, FillHdr, 0.9f);
            var lineAccent = SysToDx(Editor.UiTheme.Accent, LineHdr, 0.9f);
            var lineBright = SysToDx(Editor.UiTheme.AccentBright, LineHdr, 1.0f);
            var lineAmber = SysToDx(Editor.UiTheme.Warn, LineHdr, 1.0f);
            var selFill = new Vector4(1.0f * FillHdr, 0.78f * FillHdr, 0.30f * FillHdr, 0.95f);
            var selLine = new Vector4(1.0f * LineHdr, 0.78f * LineHdr, 0.30f * LineHdr, 1.0f);
            const int discSegs = 16;
            var unitDisc = new Vector3[discSegs];
            for (int i = 0; i < discSegs; i++) { double a = i * Math.PI * 2.0 / discSegs; unitDisc[i] = new Vector3((float)Math.Cos(a), (float)Math.Sin(a), 0.0f); }
            var lift = new Vector3(0, 0, 0.04f);

            void Tri(Vector3 a, Vector3 bb, Vector3 c, Vector4 col) { tris.Add(new LineVertex(a, col)); tris.Add(new LineVertex(bb, col)); tris.Add(new LineVertex(c, col)); }
            void Line(Vector3 a, Vector3 bb, Vector4 col) { lines.Add(new LineVertex(a, col)); lines.Add(new LineVertex(bb, col)); }
            void Disc(Vector3 c, float r, Vector4 col, Vector4 rim)
            {
                var faint = new Vector4(col.X, col.Y, col.Z, col.W * 0.35f);
                for (int i = 0; i < discSegs; i++)
                {
                    int j = (i + 1) % discSegs;
                    Tri(c, c + unitDisc[i] * (r * 1.45f), c + unitDisc[j] * (r * 1.45f), faint);
                    Tri(c + lift, c + lift + unitDisc[i] * r, c + lift + unitDisc[j] * r, col);
                    Line(c + lift + unitDisc[i] * r, c + lift + unitDisc[j] * r, rim);
                }
            }
            void ArrowHead(Vector3 tip, Vector3 dir, float len, float halfW, Vector4 col)
            {
                if (dir.LengthSquared() < 1e-8f) return;
                dir.Normalize();
                var side = Vector3.Cross(dir, Vector3.UnitZ);
                if (side.LengthSquared() < 1e-8f) side = Vector3.UnitX; else side.Normalize();
                var back = tip - dir * len;
                Tri(tip, back + side * halfW, back - side * halfW, col);
                Tri(tip, back - side * halfW, back + side * halfW, col);
            }
            void Ribbon(Vector3 a, Vector3 bb, float halfW, Vector4 colA, Vector4 colB)
            {
                var d = bb - a; if (d.LengthSquared() < 1e-8f) return; d.Normalize();
                var side = Vector3.Cross(d, Vector3.UnitZ);
                if (side.LengthSquared() < 1e-8f) side = Vector3.UnitX; else side.Normalize();
                side *= halfW;
                var a0 = a - side; var a1 = a + side; var b0 = bb - side; var b1 = bb + side;
                tris.Add(new LineVertex(a0, colA)); tris.Add(new LineVertex(a1, colA)); tris.Add(new LineVertex(b1, colB));
                tris.Add(new LineVertex(a0, colA)); tris.Add(new LineVertex(b1, colB)); tris.Add(new LineVertex(b0, colB));
                tris.Add(new LineVertex(a0, colA)); tris.Add(new LineVertex(b1, colB)); tris.Add(new LineVertex(a1, colA));
                tris.Add(new LineVertex(a0, colA)); tris.Add(new LineVertex(b0, colB)); tris.Add(new LineVertex(b1, colB));
            }

            var r = sr.Region;
            if (r?.Paths?.Nodes != null && r.Paths.Chains != null)
            {
                foreach (var chain in r.Paths.Chains)
                {
                    if (chain?.Edges == null) continue;
                    foreach (var edge in chain.Edges)
                    {
                        if (edge == null) continue;
                        int vid1 = edge._Data.NodeIndexFrom, vid2 = edge._Data.NodeIndexTo;
                        if (vid1 >= r.Paths.Nodes.Length || vid2 >= r.Paths.Nodes.Length) continue;
                        var v1 = r.Paths.Nodes[vid1]; var v2 = r.Paths.Nodes[vid2];
                        if (v1 == null || v2 == null) continue;
                        bool selected = ReferenceEquals(edge, ScenarioSelectedEdge);
                        var p1 = v1.Position + lift; var p2 = v2.Position + lift;
                        var d = p2 - p1; float dl = d.Length(); if (dl < 1e-4f) continue; d /= dl;
                        var cA = selected ? selFill : (v1.HasIncomingEdges ? amber : accent);
                        var cB = selected ? selFill : (v2.HasIncomingEdges ? amber : accent);
                        float headLen = Math.Min(0.9f, dl * 0.35f);
                        var headBase = p2 - d * (headLen + 0.45f);
                        Ribbon(p1, headBase, selected ? 0.14f : 0.08f, cA, cB);
                        ArrowHead(p2 - d * 0.45f, d, headLen, selected ? 0.42f : 0.30f, selected ? selFill : (v2.HasIncomingEdges ? amber : accentBright));
                    }
                }
            }
            if (r?.Clusters != null)
            {
                var bubble = SysToDx(Editor.UiTheme.AccentBright, FillHdr, 0.10f);
                foreach (var cl in r.Clusters)
                {
                    if (cl == null) continue;
                    float rad = Math.Max(cl.Radius, 0.5f);
                    if (rad > 200.0f) rad = 200.0f;
                    AddSphere(tris, cl.Position, rad, bubble, 12, 8);
                    for (int i = 0; i < discSegs; i++)
                    {
                        int j = (i + 1) % discSegs;
                        Line(cl.Position + unitDisc[i] * rad, cl.Position + unitDisc[j] * rad, lineBright);
                    }
                    Disc(cl.Position, 0.32f, accentBright, lineBright);
                }
            }
            if (sr.Nodes != null)
            {
                foreach (var n in sr.Nodes)
                {
                    if (n == null) continue;
                    bool selected = ReferenceEquals(n, ScenarioSelectedNode);
                    Vector4 fill, rim;
                    if (n.LoadSavePoint != null || n.ClusterLoadSavePoint != null) { fill = red; rim = SysToDx(Editor.UiTheme.Danger, LineHdr, 1.0f); }
                    else if (n.Entity != null || n.EntityPoint != null) { fill = green; rim = SysToDx(Editor.UiTheme.Ok, LineHdr, 1.0f); }
                    else if (n.ClusterMyPoint != null) { fill = accentBright; rim = lineBright; }
                    else if (n.MyPoint != null) { fill = accent; rim = lineAccent; }
                    else if (n.ChainingNode != null) { fill = amber; rim = lineAmber; }
                    else { fill = accent; rim = lineAccent; }
                    if (selected) { fill = selFill; rim = selLine; }
                    float rad = selected ? 0.55f : 0.35f;
                    Disc(n.Position, rad, fill, rim);
                    var fwd = n.Orientation.Multiply(Vector3.UnitY);
                    if (fwd.LengthSquared() > 1e-6f && (n.MyPoint != null || n.ClusterMyPoint != null || n.EntityPoint != null || n.LoadSavePoint != null))
                    {
                        fwd.Normalize();
                        var tip = n.Position + lift + fwd * (rad + 0.9f);
                        ArrowHead(tip, fwd, 0.7f, 0.28f, fill);
                        Ribbon(n.Position + lift + fwd * rad, tip - fwd * 0.6f, 0.06f, fill, fill);
                    }
                }
            }

            if (lines.Count >= 2)
            {
                b.Lines = Buffer.Create(device, BindFlags.VertexBuffer, lines.ToArray());
                b.LineCount = lines.Count;
            }
            if (tris.Count >= 3)
            {
                b.Tris = Buffer.Create(device, BindFlags.VertexBuffer, tris.ToArray());
                b.TriCount = tris.Count;
            }
        }

        private static void AddSphere(List<LineVertex> tris, Vector3 c, float r, Vector4 col, int slices, int stacks)
        {
            Vector3 P(int i, int j)
            {
                double th = j * Math.PI / stacks, ph = i * Math.PI * 2.0 / slices;
                return c + new Vector3((float)(Math.Sin(th) * Math.Cos(ph)), (float)(Math.Sin(th) * Math.Sin(ph)), (float)Math.Cos(th)) * r;
            }
            for (int j = 0; j < stacks; j++)
                for (int i = 0; i < slices; i++)
                {
                    var a = P(i, j); var bb = P(i + 1, j); var cc = P(i + 1, j + 1); var d = P(i, j + 1);
                    tris.Add(new LineVertex(a, col)); tris.Add(new LineVertex(bb, col)); tris.Add(new LineVertex(cc, col));
                    tris.Add(new LineVertex(a, col)); tris.Add(new LineVertex(cc, col)); tris.Add(new LineVertex(d, col));
                    tris.Add(new LineVertex(a, col)); tris.Add(new LineVertex(cc, col)); tris.Add(new LineVertex(bb, col));
                    tris.Add(new LineVertex(a, col)); tris.Add(new LineVertex(d, col)); tris.Add(new LineVertex(cc, col));
                }
        }

        public void Draw(DeviceContext context, Matrix viewProj, Vector3 camPos, IReadOnlyList<BasePathData> keys,
                         bool drawTris = true, bool drawLines = true, bool drawNodes = true, DepthStencilState depth = null)
        {
            LinesDrawn = TrisDrawn = NodesDrawn = BatchesDrawn = 0;
            if (keys == null || keys.Count == 0) return;
            var list = new List<Batch>(keys.Count);
            foreach (var key in keys)
            {
                if (key == null) continue;
                if (!batches.TryGetValue(key, out var b)) { b = Build(key); batches[key] = b; }
                usedThisFrame.Add(key);
                list.Add(b);
            }
            if (list.Count == 0) return;

            var vars = new LineVars { ViewProj = Matrix.Transpose(viewProj), CamPull = new Vector4(camPos, PullToCamera) };
            cbuffer.Update(context, ref vars);
            shader.Apply(context);
            context.VertexShader.SetConstantBuffer(0, cbuffer.Buffer);
            context.OutputMerger.SetBlendState(CommonStates.BlendAlpha);
            context.OutputMerger.SetDepthStencilState(depth ?? CommonStates.DepthReadOnly);
            context.Rasterizer.State = CommonStates.RasterSolid;

            if (drawTris)
            {
                context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
                foreach (var b in list)
                {
                    if (b.Tris == null) continue;
                    context.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(b.Tris, LineVertex.Stride, 0));
                    context.Draw(b.TriCount, 0);
                    TrisDrawn += b.TriCount / 3;
                }
            }
            if (drawLines)
            {
                context.InputAssembler.PrimitiveTopology = PrimitiveTopology.LineList;
                foreach (var b in list)
                {
                    if (b.Lines == null) continue;
                    context.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(b.Lines, LineVertex.Stride, 0));
                    context.Draw(b.LineCount, 0);
                    LinesDrawn += b.LineCount / 2;
                }
            }
            if (drawNodes)
            {
                context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
                foreach (var b in list)
                {
                    if (b.Nodes == null) continue;
                    context.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(b.Nodes, LineVertex.Stride, 0));
                    context.Draw(b.NodeCount, 0);
                    NodesDrawn += b.NodeCount / 36;
                }
            }
            BatchesDrawn = list.Count;
        }

        public void EndFrame(int keepAtLeast = 64)
        {
            if (batches.Count > keepAtLeast)
            {
                unusedKeys.Clear();
                foreach (var kv in batches) if (!usedThisFrame.Contains(kv.Key)) unusedKeys.Add(kv.Key);
                int drop = batches.Count - keepAtLeast;
                for (int i = 0; i < unusedKeys.Count && i < drop; i++) { batches[unusedKeys[i]].Dispose(); batches.Remove(unusedKeys[i]); }
            }
            usedThisFrame.Clear();
        }

        public void Dispose()
        {
            Clear();
            cbuffer?.Dispose();
            shader?.Dispose();
        }
    }
}

