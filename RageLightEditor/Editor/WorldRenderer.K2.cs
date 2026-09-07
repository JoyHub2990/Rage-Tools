using System;
using System.Collections.Generic;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor.Editor
{
    public partial class WorldRenderer
    {
        public Vector3 CasterEye;
        public float CasterRadius = 35.0f;
        public float CasterMinRadius = 0.4f;
        public readonly List<RenderMesh> SunCasterExtras = new List<RenderMesh>();
        private int casterExtrasFrame = -1;
        public readonly List<RenderMesh> SunCasterOccluders = new List<RenderMesh>();
        private readonly List<RenderMesh> sunCasterScratch = new List<RenderMesh>();

        private void NoteCulled_K2(RenderMesh m)
        {
            if (CasterRadius <= 0.0f) return;
            if (casterExtrasFrame != frame) { SunCasterExtras.Clear(); casterExtrasFrame = frame; }
            if (!m.Visible) return;
            if (m.AlphaMode != GeomAlphaMode.Opaque && m.AlphaMode != GeomAlphaMode.Cutout) return;
            if (m.WorldSphere.Radius < CasterMinRadius) return;
            float d = Vector3.Distance(m.WorldSphere.Center, CasterEye) - m.WorldSphere.Radius;
            if (d > CasterRadius) return;
            SunCasterExtras.Add(m);
        }

        public IList<RenderMesh> SunCasters()
        {
            if (casterExtrasFrame != frame) { SunCasterExtras.Clear(); casterExtrasFrame = frame; }
            if (SunCasterExtras.Count == 0 && SunCasterOccluders.Count == 0) return Model.Meshes;
            sunCasterScratch.Clear();
            sunCasterScratch.AddRange(Model.Meshes);
            sunCasterScratch.AddRange(SunCasterExtras);
            sunCasterScratch.AddRange(SunCasterOccluders);
            return sunCasterScratch;
        }
    }
}

