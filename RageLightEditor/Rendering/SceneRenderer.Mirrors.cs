using System;
using System.Collections.Generic;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;

namespace RageLightEditor.Rendering
{
    public partial class SceneRenderer
    {
        public const int MaxMirrorPasses = 2;
        public bool MirrorReflections = true;
        public float MirrorTargetScale = 1.0f;

        public static readonly bool MirrorDebugView = Environment.GetEnvironmentVariable("RLE_MIRRORDEBUG") == "1";
        public int MirrorPassesThisFrame, MirrorMeshesInView, MirrorPlanesInView;
        public float MirrorNearestDist = -1;

        private readonly Vector4[] reflRect = new Vector4[MaxMirrorPasses];
        private readonly Vector2[] reflUsed = { Vector2.One, Vector2.One };
        private float reflMinPixels = 0.0f;
        public float MirrorFloorScale = 0.5f;
        public int MirrorHoldFrames = 4;
        public int ReflectionMeshesSkipped, ReflectionMeshesDrawn;
        public float MirrorFloorFarDist = 120.0f, MirrorFloorFarMinRadius = 12.0f;
        private bool reflFloorPass;
        public float MirrorCropArea = 1.0f; public bool MirrorReused;
        public string MirrorHoldWhy = "";
        public int MirrorReusedFrames;
        private Vector3 heldEye, heldFwd; private int heldLights = -1; private int heldMeshCount = -1; private int heldAge;
        private readonly Plane[] heldPlanes = new Plane[MaxMirrorPasses];
        private readonly Vector4[] heldRect = new Vector4[MaxMirrorPasses];
        private readonly Vector2[] heldUsed = new Vector2[MaxMirrorPasses];
        private int heldPasses;
        private readonly List<RenderMesh>[] heldMeshes = { new List<RenderMesh>(), new List<RenderMesh>() };
        public int LastFrameMeshCount;

        private readonly Texture2D[] reflTex = new Texture2D[MaxMirrorPasses];
        private readonly RenderTargetView[] reflRtv = new RenderTargetView[MaxMirrorPasses];
        private readonly ShaderResourceView[] reflSrv = new ShaderResourceView[MaxMirrorPasses];
        private Texture2D reflDepthTex;
        private DepthStencilView reflDsv;
        private int reflW, reflH;

        private bool reflectionPass;
        private Matrix reflViewProj;
        private Vector3 reflCamPos;
        private Plane reflPlane;
        private readonly Dictionary<RenderMesh, int> mirrorSlot = new Dictionary<RenderMesh, int>();
        private readonly List<(RenderMesh mesh, Plane plane, float dist)> mirrorsSeen = new List<(RenderMesh, Plane, float)>();
        private readonly List<MirrorGroup> mirrorGroups = new List<MirrorGroup>();

        private class MirrorGroup
        {
            public Plane Plane;
            public float Dist;
            public readonly List<RenderMesh> Meshes = new List<RenderMesh>();
        }

        private float MirrorSlotFor(RenderMesh mesh) => mirrorSlot.TryGetValue(mesh, out int slot) ? slot : 0;

        private void EnsureReflectionTargets(int w, int h)
        {
            w = Math.Max(64, w); h = Math.Max(64, h);
            if (reflW == w && reflH == h && reflTex[0] != null) return;
            DisposeReflectionTargets();
            for (int i = 0; i < MaxMirrorPasses; i++)
            {
                reflTex[i] = new Texture2D(device, new Texture2DDescription
                {
                    Width = w, Height = h, MipLevels = 1, ArraySize = 1,
                    Format = Format.R16G16B16A16_Float,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                });
                reflRtv[i] = new RenderTargetView(device, reflTex[i]);
                reflSrv[i] = new ShaderResourceView(device, reflTex[i]);
            }
            reflDepthTex = new Texture2D(device, new Texture2DDescription
            {
                Width = w, Height = h, MipLevels = 1, ArraySize = 1,
                Format = Format.D32_Float,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.DepthStencil,
            });
            reflDsv = new DepthStencilView(device, reflDepthTex);
            reflW = w; reflH = h;
        }

        private void DisposeReflectionTargets()
        {
            for (int i = 0; i < MaxMirrorPasses; i++)
            {
                reflSrv[i]?.Dispose(); reflSrv[i] = null;
                reflRtv[i]?.Dispose(); reflRtv[i] = null;
                reflTex[i]?.Dispose(); reflTex[i] = null;
            }
            reflDsv?.Dispose(); reflDsv = null;
            reflDepthTex?.Dispose(); reflDepthTex = null;
            reflW = reflH = 0;
        }

        public void RenderMirrorReflections(DeviceContext context, Camera camera, IEnumerable<RenderModel> models,
            GpuLight[] lights, int lightCount, ShaderResourceView[] projTextures, ShadowSetup shadow,
            int viewportWidth, int viewportHeight)
        {
            mirrorSlot.Clear();
            MirrorPassesThisFrame = 0; MirrorMeshesInView = 0; MirrorPlanesInView = 0; MirrorNearestDist = -1;
            ReflectionMeshesSkipped = 0; ReflectionMeshesDrawn = 0; MirrorReused = false; MirrorCropArea = 0;
            for (int i = 0; i < MaxMirrorPasses; i++) { reflRect[i] = Vector4.Zero; reflUsed[i] = Vector2.One; }
            context.PixelShader.SetShaderResource(29, null);
            context.PixelShader.SetShaderResource(30, null);
            if (!MirrorReflections || RenderMode != 0 || models == null) { heldPasses = 0; return; }

            var frustum = new BoundingFrustum(camera.ViewProjMatrix);
            var eye = camera.Position;
            mirrorsSeen.Clear();
            int meshTotal = 0;
            foreach (var m in models)
            {
                meshTotal += m.Meshes.Count;
                foreach (var mesh in m.Meshes)
                {
                    if (!mesh.IsMirror || !Drawable(mesh)) continue;
                    if (frustum.Contains(ref mesh.WorldSphere) == ContainmentType.Disjoint) continue;
                    if (!mesh.MirrorPlaneWorld(out var plane)) continue;
                    float side = Vector3.Dot(plane.Normal, eye) + plane.D;
                    if (side < 0) { plane = new Plane(-plane.Normal, -plane.D); side = -side; }
                    if (side < 0.02f) continue;
                    float dist = Math.Max(Vector3.Distance(eye, mesh.WorldSphere.Center) - mesh.WorldSphere.Radius, 0.0f);
                    mirrorsSeen.Add((mesh, plane, dist));
                }
            }
            ScanAllMirrors_T6(models, eye);
            MirrorMeshesInView = mirrorsSeen.Count;
            LastFrameMeshCount = meshTotal;
            if (mirrorsSeen.Count == 0) { heldPasses = 0; return; }

            mirrorGroups.Clear();
            foreach (var (mesh, plane, dist) in mirrorsSeen)
            {
                MirrorGroup g = null;
                foreach (var cand in mirrorGroups)
                {
                    if (Vector3.Dot(cand.Plane.Normal, plane.Normal) < 0.9994f) continue;
                    float off = Vector3.Dot(cand.Plane.Normal, mesh.WorldSphere.Center) + cand.Plane.D;
                    float own = Vector3.Dot(plane.Normal, mesh.WorldSphere.Center) + plane.D;
                    if (Math.Abs(off - own) > 0.05f) continue;
                    g = cand; break;
                }
                if (g == null) { g = new MirrorGroup { Plane = plane, Dist = dist }; mirrorGroups.Add(g); }
                g.Meshes.Add(mesh);
                g.Dist = Math.Min(g.Dist, dist);
            }
            MirrorPlanesInView = mirrorGroups.Count;
            mirrorGroups.Sort((a, b) => a.Dist.CompareTo(b.Dist));
            MirrorNearestDist = mirrorGroups[0].Dist;

            int w = Math.Max(1, (int)(viewportWidth * MirrorTargetScale));
            int h = Math.Max(1, (int)(viewportHeight * MirrorTargetScale));
            EnsureReflectionTargets(w, h);
            int passes = Math.Min(MaxMirrorPasses, mirrorGroups.Count);
            reflMinPixels = 2.0f * (float)Math.Tan(camera.FieldOfView * 0.5f) / Math.Max(reflH, 1);

            var fwd = camera.GetForward();
            bool sameView = Vector3.DistanceSquared(eye, heldEye) < 1e-8f && Vector3.DistanceSquared(fwd, heldFwd) < 1e-10f;
            bool sameScene = heldLights == lightCount && heldMeshCount == meshTotal;
            bool samePlanes = heldPasses == passes;
            for (int gi = 0; samePlanes && gi < passes; gi++)
            {
                var p = mirrorGroups[gi].Plane;
                if (Vector3.Dot(p.Normal, heldPlanes[gi].Normal) < 0.99999f || Math.Abs(p.D - heldPlanes[gi].D) > 0.001f) samePlanes = false;
                else if (mirrorGroups[gi].Meshes.Count != heldMeshes[gi].Count) samePlanes = false;
                else for (int k = 0; k < heldMeshes[gi].Count; k++) if (!ReferenceEquals(heldMeshes[gi][k], mirrorGroups[gi].Meshes[k])) { samePlanes = false; break; }
            }
            bool slowMove = !sameView && Vector3.DistanceSquared(eye, heldEye) < 0.3f * 0.3f && Vector3.DistanceSquared(fwd, heldFwd) < 0.05f * 0.05f;
            bool holdOk = sameView ? heldAge < MirrorHoldFrames : (slowMove && heldAge < 1);
            MirrorHoldWhy = (sameView || slowMove) ? (sameScene ? (samePlanes ? "ok" : "planes") : $"scene(l {heldLights}->{lightCount} m {heldMeshCount}->{meshTotal})") : "view";
            if (MirrorHoldFrames > 0 && (sameView || slowMove) && sameScene && samePlanes && holdOk && reflTex[0] != null)
            {
                heldAge++;
                MirrorReused = true; MirrorReusedFrames++;
                for (int gi = 0; gi < passes; gi++)
                {
                    reflRect[gi] = heldRect[gi]; reflUsed[gi] = heldUsed[gi];
                    foreach (var mesh in mirrorGroups[gi].Meshes) mirrorSlot[mesh] = gi + 1;
                }
                MirrorPassesThisFrame = passes;
                MirrorCropArea = heldRect[0].Z * heldRect[0].W;
                context.PixelShader.SetShaderResource(29, passes > 0 ? reflSrv[0] : null);
                context.PixelShader.SetShaderResource(30, passes > 1 ? reflSrv[1] : null);
                return;
            }
            heldAge = 0; heldEye = eye; heldFwd = fwd; heldLights = lightCount; heldMeshCount = meshTotal; heldPasses = passes;

            var prevRtvs = context.OutputMerger.GetRenderTargets(1, out var prevDsv);
            var prevVps = context.Rasterizer.GetViewports<SharpDX.Mathematics.Interop.RawViewportF>();
            var savedRaster = curRaster;

            try
            {
                for (int gi = 0; gi < passes; gi++)
                {
                    var g = mirrorGroups[gi];
                    var plane = g.Plane;
                    heldPlanes[gi] = plane;
                    heldMeshes[gi].Clear(); heldMeshes[gi].AddRange(g.Meshes);

                    var rect = MirrorScreenRect(g.Meshes, camera.ViewProjMatrix);
                    bool floor = true;
                    foreach (var mesh in g.Meshes) if (mesh.DecalKind != 8) { floor = false; break; }
                    float scale = floor ? Math.Min(MirrorFloorScale, 1.0f) : 1.0f;
                    reflFloorPass = floor;
                    float hw = rect.Z, hh = rect.W;
                    int uw = Math.Clamp((int)Math.Ceiling(reflW * hw * scale), 8, reflW);
                    int uh = Math.Clamp((int)Math.Ceiling(reflH * hh * scale), 8, reflH);
                    reflRect[gi] = rect;
                    reflUsed[gi] = new Vector2(uw / (float)reflW, uh / (float)reflH);
                    heldRect[gi] = reflRect[gi]; heldUsed[gi] = reflUsed[gi];
                    if (gi == 0) MirrorCropArea = hw * hh;

                    var R = ReflectionMatrix(plane);
                    var view = R * camera.ViewMatrix;
                    var crop = new Matrix(
                        1.0f / hw, 0, 0, 0,
                        0, 1.0f / hh, 0, 0,
                        0, 0, 1, 0,
                        -rect.X / hw, -rect.Y / hh, 0, 1);
                    var proj = camera.ProjMatrix * crop * Matrix.Scaling(-1, 1, 1);
                    reflViewProj = view * proj;
                    NoteMirrorCullView_U4(gi, view, camera.ProjMatrix, rect);
                    reflCamPos = Vector3.TransformCoordinate(eye, R);
                    reflPlane = new Plane(plane.Normal, plane.D - MirrorClipFront_U4);

                    context.OutputMerger.SetRenderTargets(reflDsv, reflRtv[gi]);
                    context.Rasterizer.SetViewport(0, 0, uw, uh, 0.0f, 1.0f);
                    context.ClearRenderTargetView(reflRtv[gi], new Color4(0, 0, 0, 0));
                    context.ClearDepthStencilView(reflDsv, DepthStencilClearFlags.Depth, 0.0f, 0);

                    reflectionPass = true;
                    int drawnBefore = DrawnMeshes;
                    try
                    {
                        Render(context, camera, models, lights, lightCount, null, projTextures, shadow);
                    }
                    finally { reflectionPass = false; }
                    ReflectionMeshesDrawn += DrawnMeshes - drawnBefore;
                    ReflectionMirrorBounds_T6 = MirrorWorldBounds_T6(g.Meshes);
                    ReflectionExtras_S6?.Invoke(context, reflViewProj, plane, gi);

                    foreach (var mesh in g.Meshes) mirrorSlot[mesh] = gi + 1;
                    MirrorPassesThisFrame++;
                }
            }
            finally
            {
                context.OutputMerger.SetRenderTargets(prevDsv, prevRtvs);
                if (prevVps != null && prevVps.Length > 0) context.Rasterizer.SetViewports(prevVps);
                if (prevRtvs != null) foreach (var r in prevRtvs) r?.Dispose();
                prevDsv?.Dispose();
                curRaster = savedRaster;
                context.Rasterizer.State = CommonStates.RasterSolid;
                curRaster = CommonStates.RasterSolid;
            }

            context.PixelShader.SetShaderResource(29, MirrorPassesThisFrame > 0 ? reflSrv[0] : null);
            context.PixelShader.SetShaderResource(30, MirrorPassesThisFrame > 1 ? reflSrv[1] : null);
        }

        private static Vector4 MirrorScreenRect(List<RenderMesh> meshes, Matrix viewProj)
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            var corners = new Vector3[8];
            var clip = new Vector4[8];
            const float wMin = 0.05f;
            bool any = false;
            void Take(Vector4 p)
            {
                float nx = p.X / p.W, ny = p.Y / p.W;
                if (nx < x0) x0 = nx; if (nx > x1) x1 = nx;
                if (ny < y0) y0 = ny; if (ny > y1) y1 = ny;
                any = true;
            }
            foreach (var mesh in meshes)
            {
                mesh.WorldBounds.GetCorners(corners);
                for (int i = 0; i < 8; i++) clip[i] = Vector4.Transform(new Vector4(corners[i], 1.0f), viewProj);
                for (int e = 0; e < 12; e++)
                {
                    int a, b;
                    if (e < 4) { a = e; b = (e + 1) & 3; }
                    else if (e < 8) { a = 4 + (e - 4); b = 4 + ((e - 3) & 3); }
                    else { a = e - 8; b = a + 4; }
                    var pa = clip[a]; var pb = clip[b];
                    bool ina = pa.W > wMin, inb = pb.W > wMin;
                    if (!ina && !inb) continue;
                    if (ina) Take(pa);
                    if (inb) Take(pb);
                    if (ina != inb)
                    {
                        float t = (wMin - pa.W) / (pb.W - pa.W);
                        Take(pa + (pb - pa) * t);
                    }
                }
            }
            if (!any) return new Vector4(0, 0, 1, 1);
            const float pad = 0.02f;
            x0 = Math.Max(x0 - pad, -1.0f); x1 = Math.Min(x1 + pad, 1.0f);
            y0 = Math.Max(y0 - pad, -1.0f); y1 = Math.Min(y1 + pad, 1.0f);
            if (x1 <= x0 || y1 <= y0) return new Vector4(0, 0, 1, 1);
            float hw = (x1 - x0) * 0.5f, hh = (y1 - y0) * 0.5f;
            if (hw > 0.97f && hh > 0.97f) return new Vector4(0, 0, 1, 1);
            return new Vector4((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, hw, hh);
        }

        private static Matrix ReflectionMatrix(Plane plane)
        {
            var n = plane.Normal; float d = plane.D;
            return new Matrix(
                1 - 2 * n.X * n.X, -2 * n.X * n.Y,    -2 * n.X * n.Z,    0,
                -2 * n.Y * n.X,    1 - 2 * n.Y * n.Y, -2 * n.Y * n.Z,    0,
                -2 * n.Z * n.X,    -2 * n.Z * n.Y,    1 - 2 * n.Z * n.Z, 0,
                -2 * d * n.X,      -2 * d * n.Y,      -2 * d * n.Z,      1);
        }

        public ShaderResourceView ReflectionSRV(int slot) => slot < MirrorPassesThisFrame ? reflSrv[slot] : null;

        private void DisposeMirrors() => DisposeReflectionTargets();
    }
}

