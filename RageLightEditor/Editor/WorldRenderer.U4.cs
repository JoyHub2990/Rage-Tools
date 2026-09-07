using System.Collections.Generic;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor.Editor
{
    public partial class WorldRenderer
    {
        private readonly List<BoundingFrustum> mirrorFrusta_U4 = new List<BoundingFrustum>();
        private readonly List<Plane> mirrorPlanes_U4 = new List<Plane>();

        public readonly RenderModel MirrorExtras_U4 = new RenderModel { Name = "mirror extras (WS-U4)" };

        public int MirrorExtrasTested, MirrorExtrasKept;
        public int MirrorExtrasCap = 6000;

        public void SetMirrorCull_U4(IReadOnlyList<Plane> planes, IReadOnlyList<Matrix> viewProjs)
        {
            mirrorFrusta_U4.Clear();
            mirrorPlanes_U4.Clear();
            if (planes == null || viewProjs == null) return;
            int n = planes.Count < viewProjs.Count ? planes.Count : viewProjs.Count;
            for (int i = 0; i < n; i++)
            {
                mirrorPlanes_U4.Add(planes[i]);
                mirrorFrusta_U4.Add(new BoundingFrustum(viewProjs[i]));
            }
        }

        private void BeginMirrorExtras_U4()
        {
            MirrorExtras_U4.Meshes.Clear();
            MirrorExtrasTested = 0; MirrorExtrasKept = 0;
        }

        private void NoteFrustumCulled_U4(RenderMesh m, float fade)
        {
            if (mirrorFrusta_U4.Count == 0 || MirrorExtras_U4.Meshes.Count >= MirrorExtrasCap) return;
            if (!m.Visible || m.NeverDraw) return;
            if (m.IsMirror || m.AlphaMode == GeomAlphaMode.Water) return;
            MirrorExtrasTested++;
            for (int i = 0; i < mirrorFrusta_U4.Count; i++)
            {
                var pl = mirrorPlanes_U4[i];
                if (Vector3.Dot(pl.Normal, m.WorldSphere.Center) + pl.D < -m.WorldSphere.Radius) continue;
                if (!mirrorFrusta_U4[i].Intersects(ref m.WorldSphere)) continue;
                m.FadeAlpha = fade;
                MirrorExtras_U4.Meshes.Add(m);
                MirrorExtrasKept++;
                return;
            }
        }
    }
}

