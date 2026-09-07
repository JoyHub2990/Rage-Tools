using System;
using System.Collections.Generic;
using System.IO;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        partial void SeqTest_T4(Action<string, bool, string> check)
        {
            PrecisionScaleTest_T4(check);
            RpfDropTargetTest_T4(check);
            DeleteKeyTest_T4(check);
        }

        private void PrecisionScaleTest_T4(Action<string, bool, string> check)
        {
            var saved = camera.Capture();
            float savedH = camera.ViewportHeight;
            try
            {
                camera.ViewportHeight = 900;
                camera.FieldOfView = 54.0f * 0.0174533f;
                camera.Target = new Vector3(0, 0, 0);
                camera.Distance = camera.TargetDistance = 5.0f;
                camera.Yaw = camera.TargetYaw = 0; camera.Pitch = camera.TargetPitch = 0;
                camera.SnapSmoothing(); camera.Update();

                var near = camera.Position + camera.GetForward() * 5.0f;
                var far = camera.Position + camera.GetForward() * 300.0f;
                float wNear = PrecStrokeHalfWorld_T4(near, true);
                float wFar = PrecStrokeHalfWorld_T4(far, true);
                float pxNear = wNear / camera.WorldPerPixel(near);
                float pxFar = wFar / camera.WorldPerPixel(far);
                check("precision stroke is a constant pixel width at 5 m and 300 m",
                      Math.Abs(pxNear - PrecStrokePx * 0.5f) < 1e-4f && Math.Abs(pxFar - PrecStrokePx * 0.5f) < 1e-4f &&
                      Math.Abs(wFar / wNear - 60.0f) < 0.05f,
                      $"{pxNear:0.00} px near / {pxFar:0.00} px far; world {wNear * 1000:0.##} mm -> {wFar * 1000:0.##} mm ({wFar / wNear:0.0}x)");

                float BracketFrac(float edgeLen, float dist)
                {
                    var at = camera.Position + camera.GetForward() * dist;
                    return Math.Min(PrecBracketPx * camera.WorldPerPixel(at) / edgeLen, 0.5f);
                }
                float big = BracketFrac(60.0f, 5.0f);
                float small = BracketFrac(0.2f, 5.0f);
                float pxAt5 = PrecBracketPx * camera.WorldPerPixel(camera.Position + camera.GetForward() * 5.0f);
                check("bracket is 26 px on a big box and the whole edge on a small one",
                      big > 0.0f && big < 0.1f && Math.Abs(big * 60.0f - pxAt5) < 1e-4f && Math.Abs(small - 0.5f) < 1e-5f,
                      $"60 m edge: {big * 100:0.##}% = {big * 60.0f:0.###} m = {PrecBracketPx:0} px; " +
                      $"0.2 m edge: {small * 100:0.#}% (the two stubs meet - a full box)");

                float ProjectedPx(float sizeM, float dist)
                {
                    var at = camera.Position + camera.GetForward() * dist;
                    return sizeM / camera.WorldPerPixel(at);
                }
                float propNear = ProjectedPx(2.0f, 5.0f);
                float propFar = ProjectedPx(2.0f, 300.0f);
                check("a 2 m prop falls under the minimum box size somewhere between 5 m and 300 m",
                      propNear > PrecMinBoxPx && propFar < PrecMinBoxPx,
                      $"{propNear:0.#} px at 5 m, {propFar:0.#} px at 300 m (floor {PrecMinBoxPx:0} px)");

                var m5 = PrecMarkerPx * camera.WorldPerPixel(near);
                var m300 = PrecMarkerPx * camera.WorldPerPixel(far);
                check("the compact marker is the same size on screen at any range",
                      Math.Abs(m300 / m5 - 60.0f) < 0.05f && Math.Abs(m5 / camera.WorldPerPixel(near) - PrecMarkerPx) < 1e-3f,
                      $"{PrecMarkerPx:0} px = {m5:0.###} m at 5 m and {m300:0.##} m at 300 m");

                var wasWorkspace = panel.Workspace;
                var wasMode = panel.SelectionMode;
                panel.SwitchWorkspace(LightPanel.Space.World);
                var probe = new YmapEntityDef();
                bool inPrecision = false, inEntity = false;
                int mi = LightPanel.IndexOfMode(WorldSelectionMode.EntityPrecision);
                if (mi >= 0) { panel.SelectionMode = mi; PrecisionOwnsBox_T4(probe, ref inPrecision); }
                mi = LightPanel.IndexOfMode(WorldSelectionMode.Entity);
                if (mi >= 0) { panel.SelectionMode = mi; PrecisionOwnsBox_T4(probe, ref inEntity); }
                panel.SelectionMode = wasMode;
                panel.SwitchWorkspace(wasWorkspace);
                check("the overlay replaces the wire box in Entity Precision and only there",
                      inPrecision && !inEntity, $"precision={inPrecision} entity={inEntity}");

                check("the precision colours are magenta and cyan, not the selection amber",
                      PrecSelectCol.Y < 0.3f && PrecSelectCol.Z > 0.6f && PrecHoverCol.X < 0.4f && PrecHoverCol.Z > 0.9f,
                      $"selected {PrecSelectCol.X:0.00},{PrecSelectCol.Y:0.00},{PrecSelectCol.Z:0.00} " +
                      $"hover {PrecHoverCol.X:0.00},{PrecHoverCol.Y:0.00},{PrecHoverCol.Z:0.00}");
            }
            finally { camera.ViewportHeight = savedH; camera.Restore(saved); }
        }

        private void RpfDropTargetTest_T4(Action<string, bool, string> check)
        {
            var root = Path.Combine(Path.GetTempPath(), "rle_t4_drop");
            bool wasEditMode = panel.RpfEditMode;
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
            try
            {
                panel.RpfEditMode = true;
                Directory.CreateDirectory(root);
                var dest = Path.Combine(root, "dest");
                Directory.CreateDirectory(dest);
                var f1 = Path.Combine(root, "one.txt"); File.WriteAllText(f1, "one");
                var f2 = Path.Combine(root, "notes.xml"); File.WriteAllText(f2, "<Notes><A>1</A></Notes>");
                var sub = Path.Combine(root, "tree");
                Directory.CreateDirectory(sub);
                Directory.CreateDirectory(Path.Combine(sub, "deep"));
                File.WriteAllText(Path.Combine(sub, "a.dat"), "aaaa");
                File.WriteAllText(Path.Combine(sub, "deep", "b.dat"), "bbbb");

                bool ok = panel.DropRpfFilesForTest_T4(new[] { f1, f2, sub }, dest);
                bool loose = File.Exists(Path.Combine(dest, "one.txt")) && File.Exists(Path.Combine(dest, "notes.xml"));
                bool tree = File.Exists(Path.Combine(dest, "tree", "a.dat")) &&
                            File.Exists(Path.Combine(dest, "tree", "deep", "b.dat"));
                check("a drop of two files and a folder writes all of them, folders recreated",
                      ok && loose && tree,
                      $"loose={loose} tree={tree} status='{panel.RpfStatus}'");

                check("a plain .xml is dropped in as itself, byte for byte",
                      File.Exists(Path.Combine(dest, "notes.xml")) &&
                      File.ReadAllText(Path.Combine(dest, "notes.xml")) == "<Notes><A>1</A></Notes>",
                      "notes.xml kept its name and its bytes");

                panel.RpfEditMode = false;
                var guarded = Path.Combine(root, "guarded");
                Directory.CreateDirectory(guarded);
                panel.DropRpfFilesForTest_T4(new[] { f1 }, guarded);
                check("edit mode off: a drop writes nothing and says so",
                      Directory.GetFiles(guarded).Length == 0 && panel.RpfStatus.Contains("edit mode is off"),
                      $"{Directory.GetFiles(guarded).Length} files, status '{panel.RpfStatus}'");
            }
            catch (Exception ex) { check("the drop test ran", false, ex.Message); }
            finally { panel.RpfEditMode = wasEditMode; try { Directory.Delete(root, true); } catch { } }
        }

        private void DeleteKeyTest_T4(Action<string, bool, string> check)
        {
            var was = panel.Workspace;
            bool wasEdit = panel.RpfEditMode;
            try
            {
                panel.SwitchWorkspace(LightPanel.Space.Archive);

                panel.RpfEditMode = true;
                panel.ClearRpfSelectionForTest_T4();
                bool took = false;
                DeleteKey_T4(ref took);
                check("Delete in the RPF explorer is claimed by the RPF explorer",
                      took && panel.RpfStatus.Contains("nothing is selected"),
                      $"handled={took} status='{panel.RpfStatus}'");

                panel.RpfEditMode = false;
                panel.SelectFirstRpfRowForTest_T4();
                took = false;
                DeleteKey_T4(ref took);
                check("Delete with edit mode off writes nothing and names the file",
                      took && panel.RpfStatus.Contains("edit mode is off"),
                      $"status '{panel.RpfStatus}'");

                panel.SwitchWorkspace(LightPanel.Space.Cinematic);
                int before = panel.Sequence.Shots.Count;
                panel.Sequence.Shots.Add(new CameraShot());
                panel.Sequence.Shots.Add(new CameraShot());
                panel.SelectedShot = panel.Sequence.Shots.Count - 1;
                took = false;
                DeleteKey_T4(ref took);
                check("Delete removes the selected cinematic shot",
                      took && panel.Sequence.Shots.Count == before + 1,
                      $"{before + 2} shots -> {panel.Sequence.Shots.Count}");
                while (panel.Sequence.Shots.Count > before) panel.Sequence.Shots.RemoveAt(panel.Sequence.Shots.Count - 1);
                panel.SelectedShot = -1;

                panel.SwitchWorkspace(LightPanel.Space.World);
                took = false;
                DeleteKey_T4(ref took);
                check("Delete in the world is left to the world's own delete",
                      !took, "the hook declined it");
            }
            finally { panel.RpfEditMode = wasEdit; panel.SwitchWorkspace(was); }
        }
    }
}

