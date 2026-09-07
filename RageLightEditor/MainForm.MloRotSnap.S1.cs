using System;
using System.Collections.Generic;
using System.Linq;
using RageLightEditor.Editor;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private readonly Dictionary<object, (BoundingBox box, int meshes)> mloLocalBox_S1 = new Dictionary<object, (BoundingBox, int)>();
        private int mloLocalBoxGeom_S1 = -1;

        private void InvalidateMloLocalBoxes_S1() { mloLocalBox_S1.Clear(); mloLocalBoxGeom_S1 = -1; }

        private void MloLocalBoxGuard_S1()
        {
            int v = mloScene?.GeometryVersion ?? 0;
            if (v == mloLocalBoxGeom_S1) return;
            mloLocalBox_S1.Clear();
            mloLocalBoxGeom_S1 = v;
        }

        private bool MloEntityBox_S1(MloCreatorEntity e, out BoundingBox local, out Matrix world)
        {
            local = default; world = Matrix.Identity;
            if (e == null) return false;
            MloLocalBoxGuard_S1();

            var info = e.SourceInfo;
            if (info != null && info.HasPlacedMeshes_O3)
            {
                world = info.PlacedAt_O3;
                return LocalBoxOf_S1(info, info.PlacedMeshes_O3, world, out local);
            }
            var f = e.SourceFile;
            if (f?.Model?.Meshes != null && f.Model.Meshes.Count > 0)
            {
                world = f.HasPlacement ? f.Placement : Matrix.Identity;
                return LocalBoxOf_S1(f, f.Model.Meshes, world, out local);
            }
            return false;
        }

        private bool LocalBoxOf_S1(object key, IList<RenderMesh> meshes, Matrix world, out BoundingBox local)
        {
            local = default;
            if (meshes == null || meshes.Count == 0) return false;
            if (mloLocalBox_S1.TryGetValue(key, out var hit) && hit.meshes == meshes.Count) { local = hit.box; return true; }

            var inv = world;
            inv.Invert();
            var mn = new Vector3(float.MaxValue); var mx = new Vector3(float.MinValue);
            bool any = false;
            foreach (var m in meshes)
            {
                if (m == null) continue;
                var lb = m.LocalBounds;
                if (lb.Maximum.X <= lb.Minimum.X) continue;
                var toEntity = m.Transform * inv;
                for (int i = 0; i < 8; i++)
                {
                    var c = new Vector3((i & 1) != 0 ? lb.Maximum.X : lb.Minimum.X,
                                        (i & 2) != 0 ? lb.Maximum.Y : lb.Minimum.Y,
                                        (i & 4) != 0 ? lb.Maximum.Z : lb.Minimum.Z);
                    var p = Vector3.TransformCoordinate(c, toEntity);
                    mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p);
                    any = true;
                }
            }
            if (!any) return false;
            local = new BoundingBox(mn, mx);
            mloLocalBox_S1[key] = (local, meshes.Count);
            return true;
        }

        private bool mloRotDemoDone_S1;

        private void ServiceMloRot_S1(MloCreatorPanel ui)
        {
            if (mloRotDemoDone_S1) return;
            var spec = Environment.GetEnvironmentVariable("RLE_MLOROT");
            if (string.IsNullOrEmpty(spec)) return;
            var s = ui?.Session;
            if (s == null || mloScene == null || !mloScene.HasModel) return;
            if (DebugMlo != null && !debugMloDone) return;
            mloRotDemoDone_S1 = true;
            try
            {
                var f = spec.Split(',');
                int ei = int.TryParse(f[0], out var i0) ? i0 : 0;
                float deg = f.Length > 1 && float.TryParse(f[1], System.Globalization.CultureInfo.InvariantCulture, out var d0) ? d0 : 37.0f;
                if (ei < 0 || ei >= s.Entities.Count) return;
                var e = s.Entities[ei];
                ui.SelectEntityMulti(ei, false);
                ui.EntityTool = 2;
                var t = new CreatorEntityTarget(s, e);
                MloEntityBox_S1(e, out var before, out _);
                s.PushUndo("rot demo");
                t.SetOrientation(Quaternion.RotationAxis(Vector3.UnitZ, MathUtil.DegreesToRadians(deg)) * e.Rotation);
                MloTargetChanged_N3(t);
                MloEntityBox_S1(e, out var after, out _);
                var aabb = new BoundingBox();
                e.SourceInfo?.PlacedBounds_O3(out aabb);
                camera.Target = MeshCentre_O3(e);
                camera.Distance = Math.Max((after.Maximum - after.Minimum).Length() * 1.6f, 1.2f);
                camera.Yaw = 0.7f; camera.Pitch = 0.55f;
                camera.SnapSmoothing(); camera.Update();
                ui.WindowVisible = Environment.GetEnvironmentVariable("RLE_MLOWIN") != "0";
                screenshotFrames = Math.Max(screenshotFrames, 6);
                var sb = before.Maximum - before.Minimum; var sa = after.Maximum - after.Minimum;
                Console.WriteLine($"S1ROT entity {ei} '{e.Label}' turned {deg:0.#} deg: oriented box {sb.X:0.###} x {sb.Y:0.###} x {sb.Z:0.###} -> {sa.X:0.###} x {sa.Y:0.###} x {sa.Z:0.###} " +
                                  $"(unchanged={(sa - sb).Length() < 1e-3f}); the world AABB it replaced is now {(aabb.Maximum.X - aabb.Minimum.X):0.###} x {(aabb.Maximum.Y - aabb.Minimum.Y):0.###}");
            }
            catch (Exception ex) { Console.WriteLine("S1ROT threw: " + ex.Message); }
        }

        private void MloRotSnapTest_S1(Action<string, bool, string> check)
        {
            try
            {
                var ui = Creator;
                if (ui == null) { check("mlo rot snap: the creator exists", false, "no creator"); return; }
                check("mlo rot snap: the creator's default step is 5 degrees",
                      Math.Abs(ui.EntityRotateSnapDeg - 5.0f) < 1e-4f, ui.EntityRotateSnapDeg.ToString("0.##"));
                check("mlo rot snap: the snap toggle starts on", ui.EntitySnapOn, ui.EntitySnapOn.ToString());
                bool wasOn = ui.EntitySnapOn;
                ui.EntitySnapOn = true; ui.EntityTool = 2;
                var mode = MloEntityGizmoMode_N3(ui);
                check("mlo rot snap: E gives the gizmo a 5 degree rotate snap",
                      mode == WorldGizmoMode.Rotate && creatorGizmo != null && Math.Abs(creatorGizmo.RotateSnapDeg - 5.0f) < 1e-4f,
                      $"mode {mode}, step {(creatorGizmo == null ? "no gizmo" : creatorGizmo.RotateSnapDeg.ToString("0.##"))}");
                ui.EntitySnapOn = false;
                MloEntityGizmoMode_N3(ui);
                check("mlo rot snap: the toggle still turns it off",
                      creatorGizmo == null || creatorGizmo.RotateSnapDeg <= 0.001f, creatorGizmo?.RotateSnapDeg.ToString("0.##") ?? "-");
                ui.EntitySnapOn = wasOn; ui.EntityTool = 1;

                var s = ui.Session ?? new MloCreatorSession();
                var mesh = new RenderMesh { LocalBounds = new BoundingBox(new Vector3(-1, -0.25f, 0), new Vector3(1, 0.25f, 0.5f)) };
                mesh.Transform = Matrix.Identity;
                mesh.SetBoundsFromLocal();
                var info = new MloEntityInfo();
                var model = new RenderModel();
                model.Meshes.Add(mesh);
                info.TrackPlacedMeshes_O3(model, 1, Matrix.Identity);
                var ent = new MloCreatorEntity { SourceInfo = info, Position = Vector3.Zero, Rotation = Quaternion.Identity, Scale = Vector3.One };
                InvalidateMloLocalBoxes_S1();

                bool got0 = MloEntityBox_S1(ent, out var box0, out _);
                var size0 = box0.Maximum - box0.Minimum;
                check("mlo rot snap: the local box is the prop's own size", got0 && Math.Abs(size0.X - 2.0f) < 1e-3f && Math.Abs(size0.Y - 0.5f) < 1e-3f,
                      $"{size0.X:0.###} x {size0.Y:0.###} x {size0.Z:0.###}");

                for (int i = 0; i < 20; i++)
                {
                    ent.Rotation = Quaternion.RotationAxis(Vector3.UnitZ, MathUtil.DegreesToRadians(45)) * ent.Rotation;
                    info.MovePlacedMeshes_O3(Matrix.Transformation(Vector3.Zero, Quaternion.Identity, ent.Scale, Vector3.Zero, ent.Rotation, ent.Position));
                }
                bool got1 = MloEntityBox_S1(ent, out var box1, out var world1);
                var size1 = box1.Maximum - box1.Minimum;
                check("mlo rot snap: 20 rotations later the oriented box is the same size",
                      got1 && (size1 - size0).Length() < 1e-3f, $"{size0.X:0.###} x {size0.Y:0.###} -> {size1.X:0.###} x {size1.Y:0.###}");
                ent.Rotation = Quaternion.RotationAxis(Vector3.UnitZ, MathUtil.DegreesToRadians(37)) * ent.Rotation;
                info.MovePlacedMeshes_O3(Matrix.Transformation(Vector3.Zero, Quaternion.Identity, ent.Scale, Vector3.Zero, ent.Rotation, ent.Position));
                MloEntityBox_S1(ent, out var box2, out _);
                var size2 = box2.Maximum - box2.Minimum;
                check("mlo rot snap: 37 degrees off square, the oriented box is still the prop's size",
                      (size2 - size0).Length() < 1e-3f, $"{size2.X:0.###} x {size2.Y:0.###}");
                info.PlacedBounds_O3(out var aabb);
                var aabbSize = aabb.Maximum - aabb.Minimum;
                check("mlo rot snap: the world AABB it replaced is the one that swells",
                      aabbSize.Y > size2.Y + 0.2f, $"world AABB {aabbSize.X:0.###} x {aabbSize.Y:0.###} vs oriented {size2.X:0.###} x {size2.Y:0.###}");
                size1 = size2; box1 = box2;
                MloEntityBox_S1(ent, out box1, out world1);
                var c0 = Vector3.TransformCoordinate(box1.Minimum, world1);
                var c1 = Vector3.TransformCoordinate(new Vector3(box1.Maximum.X, box1.Minimum.Y, box1.Minimum.Z), world1);
                check("mlo rot snap: the box's own edges turn with the prop",
                      Math.Abs((c1 - c0).Length() - size1.X) < 1e-3f, $"edge {(c1 - c0).Length():0.###} vs {size1.X:0.###}");
                InvalidateMloLocalBoxes_S1();
            }
            catch (Exception ex)
            {
                check("mlo rot snap: no exception", false, ex.ToString());
            }
        }
    }
}

