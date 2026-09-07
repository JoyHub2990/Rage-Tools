using System.Collections.Generic;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Rendering
{
    public sealed class DecodedGeom_V66
    {
        public MeshVertex[] Verts;
        public Vector3[] Pick;
        public bool Sheet;
    }

    public partial class ModelRenderer
    {
        public Dictionary<DrawableGeometry, DecodedGeom_V66> Precomputed_V66;

        public static Dictionary<DrawableGeometry, DecodedGeom_V66> PrecomputeDrawable_V66(DrawableBase drawable)
        {
            var models = drawable?.AllModels;
            if (models == null || models.Length == 0) return null;
            Dictionary<DrawableGeometry, DecodedGeom_V66> dict = null;
            foreach (var m in models)
            {
                var geoms = m?.Geometries;
                if (geoms == null) continue;
                foreach (var geom in geoms)
                {
                    var vdata = geom?.VertexData;
                    var indices = geom?.IndexBuffer?.Indices;
                    if (vdata?.VertexBytes == null || indices == null || indices.Length < 3) continue;
                    var verts = VertexDecoder.Decode(vdata, indices);
                    if (verts == null || verts.Length == 0) continue;
                    var pick = new Vector3[verts.Length];
                    for (int i = 0; i < verts.Length; i++) pick[i] = verts[i].Position;
                    dict ??= new Dictionary<DrawableGeometry, DecodedGeom_V66>();
                    dict[geom] = new DecodedGeom_V66
                    {
                        Verts = verts,
                        Pick = pick,
                        Sheet = IsOpenSheet(pick, indices, verts),
                    };
                }
            }
            return dict;
        }
    }
}
