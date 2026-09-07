using System;
using System.Collections.Generic;
using SharpDX;
using SharpDX.Direct3D11;
using Buffer = SharpDX.Direct3D11.Buffer;

namespace RageLightEditor.Rendering
{
    public partial class ModelRenderer
    {
        public const int MaxFinEdges_U8 = 150000;

        public static bool BuildFinGeometry_U8(MeshVertex[] verts, ushort[] indices,
                                               out MeshVertex[] finVerts, out uint[] finIndices)
        {
            finVerts = null;
            finIndices = null;
            if (verts == null || indices == null || indices.Length < 3) return false;

            var edges = new Dictionary<int, (int A, int B, Vector3 NA, Vector3 NB, bool Two)>();
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                int i0 = indices[i], i1 = indices[i + 1], i2 = indices[i + 2];
                if (i0 >= verts.Length || i1 >= verts.Length || i2 >= verts.Length) continue;
                var fn = Vector3.Cross(verts[i1].Position - verts[i0].Position,
                                       verts[i2].Position - verts[i0].Position);
                float fl = fn.Length();
                if (fl < 1e-12f) continue;
                fn /= fl;
                Note(edges, i0, i1, fn);
                Note(edges, i1, i2, fn);
                Note(edges, i2, i0, fn);
            }
            if (edges.Count == 0 || edges.Count > MaxFinEdges_U8) return false;

            var fv = new MeshVertex[edges.Count * 4];
            var fi = new uint[edges.Count * 12];
            int v = 0, x = 0;
            foreach (var kv in edges)
            {
                var (a, b, na, nb, two) = kv.Value;
                if (!two) nb = na;
                if (Vector3.Dot(na, nb) < 0.0f) nb = -nb;
                var packB = new Vector4(nb * 0.5f + new Vector3(0.5f), 1.0f);

                uint r0 = (uint)v;
                fv[v++] = FinVert(verts[a], na, packB, 0.0f);
                fv[v++] = FinVert(verts[b], na, packB, 0.0f);
                fv[v++] = FinVert(verts[a], na, packB, 1.0f);
                fv[v++] = FinVert(verts[b], na, packB, 1.0f);

                fi[x++] = r0; fi[x++] = r0 + 1; fi[x++] = r0 + 3;
                fi[x++] = r0; fi[x++] = r0 + 3; fi[x++] = r0 + 2;
                fi[x++] = r0 + 1; fi[x++] = r0; fi[x++] = r0 + 2;
                fi[x++] = r0 + 1; fi[x++] = r0 + 2; fi[x++] = r0 + 3;
            }
            finVerts = fv;
            finIndices = fi;
            return true;
        }

        private static void Note(Dictionary<int, (int, int, Vector3, Vector3, bool)> edges,
                                 int a, int b, Vector3 fn)
        {
            int lo = Math.Min(a, b), hi = Math.Max(a, b);
            int key = (lo << 16) | hi;
            if (edges.TryGetValue(key, out var e))
                edges[key] = (e.Item1, e.Item2, e.Item3, fn, true);
            else
                edges[key] = (a, b, fn, fn, false);
        }

        private static MeshVertex FinVert(in MeshVertex src, Vector3 nA, Vector4 packedB, float tip)
        {
            return new MeshVertex
            {
                Position = src.Position,
                Normal = src.Normal,
                Tangent = new Vector4(nA, tip),
                Colour0 = packedB,
                Colour1 = new Vector4(1, 1, 1, 1),
                UV0 = src.UV0,
                UV1 = src.UV1,
            };
        }

        private void BuildPedFurFins_U8(RenderMesh mesh, MeshVertex[] verts, ushort[] indices)
        {
            try
            {
                if (!BuildFinGeometry_U8(verts, indices, out var fv, out var fi)) return;
                mesh.FinVB = Buffer.Create(device, BindFlags.VertexBuffer, fv);
                mesh.FinIB = Buffer.Create(device, BindFlags.IndexBuffer, fi);
                mesh.FinIndexCount = fi.Length;
            }
            catch
            {
                mesh.FinVB?.Dispose();
                mesh.FinIB?.Dispose();
                mesh.FinVB = null;
                mesh.FinIB = null;
                mesh.FinIndexCount = 0;
            }
        }

        public static int SelfTestFins_U8(Action<string, bool, string> check)
        {
            int fails = 0;
            void Chk(string what, bool ok, string d) { if (!ok) fails++; check(what, ok, d); }

            var quad = new[]
            {
                new MeshVertex { Position = new Vector3(0, 0, 0), Normal = Vector3.UnitZ, UV0 = new Vector2(0, 0) },
                new MeshVertex { Position = new Vector3(1, 0, 0), Normal = Vector3.UnitZ, UV0 = new Vector2(1, 0) },
                new MeshVertex { Position = new Vector3(1, 1, 0), Normal = Vector3.UnitZ, UV0 = new Vector2(1, 1) },
                new MeshVertex { Position = new Vector3(0, 1, 0), Normal = Vector3.UnitZ, UV0 = new Vector2(0, 1) },
            };
            var idx = new ushort[] { 0, 1, 2, 0, 2, 3 };

            bool ok1 = BuildFinGeometry_U8(quad, idx, out var fv, out var fi);
            Chk("u8 fins: two triangles share a seam, so five edges get a card",
                ok1 && fv.Length == 5 * 4 && fi.Length == 5 * 12,
                ok1 ? $"{fv.Length / 4} cards" : "no fins built");

            if (ok1)
            {
                bool tipsOk = true, inRange = true;
                for (int i = 0; i < fv.Length; i++)
                {
                    float tip = fv[i].Tangent.W;
                    if ((i % 4 < 2 && tip != 0.0f) || (i % 4 >= 2 && tip != 1.0f)) tipsOk = false;
                }
                foreach (var ii in fi) if (ii >= fv.Length) inRange = false;
                Chk("u8 fins: every card runs root 0 to tip 1", tipsOk, "roots then tips");
                Chk("u8 fins: the indices stay inside the card list", inRange, fi.Length + " indices");

                int seamKey = -1;
                bool seamTwoFaced = false;
                foreach (var vtx in fv)
                    if (vtx.Position == quad[0].Position || vtx.Position == quad[2].Position) seamKey++;
                var e = new Dictionary<int, (int, int, Vector3, Vector3, bool)>();
                Note(e, 0, 2, Vector3.UnitZ);
                Note(e, 2, 0, -Vector3.UnitZ);
                seamTwoFaced = e.Count == 1 && e[(0 << 16) | 2].Item5;
                Chk("u8 fins: a shared edge keeps both face normals for the silhouette test",
                    seamTwoFaced, "one edge, two faces");
            }

            bool ok2 = BuildFinGeometry_U8(null, null, out _, out _);
            Chk("u8 fins: nothing to build resolves to no fins instead of throwing", !ok2, "null in, false out");
            return fails;
        }
    }
}
