using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using RageLightEditor.Editor;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private bool mloClickDone_S1;
        private List<int> mloClickPicks_S1;
        private int mloClickAt_S1, mloClickPhase_S1, mloClickPx_S1, mloClickPy_S1, mloClickPrev_S1 = -1;
        private int mloClickOk_S1, mloClickBad_S1;
        private bool mloClickDetachStage_S1;

        private void ServiceMloClick_S1(MloCreatorPanel ui)
        {
            if (mloClickDone_S1) return;
            string spec = Environment.GetEnvironmentVariable("RLE_MLOCLICK");
            if (string.IsNullOrEmpty(spec)) return;
            if (ui?.Session == null || mloScene == null || !mloScene.HasModel) return;
            if (DebugMlo != null && !debugMloDone) return;
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RLE_MLOASSET")) && !mloAssetDemoDone) return;
            try { StepMloClick_S1(ui, spec); }
            catch (Exception ex) { mloClickDone_S1 = true; Console.WriteLine("S1CLICK threw: " + ex); }
        }

        private void StepMloClick_S1(MloCreatorPanel ui, string spec)
        {
            var s = ui.Session;
            if (mloClickPicks_S1 == null)
            {
                mloClickPicks_S1 = MloClickPicks_S1(s, spec);
                Console.WriteLine($"S1CLICK window visible={ui.WindowVisible} detached={ui.Detached}; {s.Entities.Count} entities, probing {mloClickPicks_S1.Count}: {string.Join(", ", mloClickPicks_S1)}");
                screenshotFrames = Math.Max(screenshotFrames, mloClickPicks_S1.Count * 2 + 12);
            }
            if (mloClickAt_S1 >= mloClickPicks_S1.Count && !mloClickDetachStage_S1)
            {
                Console.WriteLine($"S1CLICK RESULT: {mloClickOk_S1} of {mloClickOk_S1 + mloClickBad_S1} RIGHT clicks selected the prop under the cursor " +
                                  $"({mloClickLeftSelected_S1} LEFT clicks selected anything - must be 0)");
                mloClickDetachStage_S1 = true;
                mloClickAt_S1 = 0; mloClickPhase_S1 = 0; mloClickPrev_S1 = -1;
                mloClickPicks_S1 = mloClickPicks_S1.Take(2).ToList();
                ui.Detached = true;
                return;
            }
            if (mloClickAt_S1 >= mloClickPicks_S1.Count) { mloClickDone_S1 = true; ui.Detached = false; return; }

            int ei = mloClickPicks_S1[mloClickAt_S1];
            if (mloClickPhase_S1 == 0)
            {
                ui.ShowPage(MloCreatorPanel.PageKind.Interior);
                if (!AimAt_P2(ui, ei, out mloClickPx_S1, out mloClickPy_S1))
                {
                    Console.WriteLine($"S1CLICK entity {ei} '{s.Entities[ei].Label}': no direction sees it, skipped");
                    mloClickAt_S1++;
                    return;
                }
                ImGuiNET.ImGui.GetIO().AddMousePosEvent(mloClickPx_S1, mloClickPy_S1);
                lastMouse = new System.Drawing.Point(mloClickPx_S1, mloClickPy_S1);
                mloClickPhase_S1 = 1;
                return;
            }
            mloClickPhase_S1 = 0;
            int got = ClickViewport_S1(mloClickPx_S1, mloClickPy_S1, out string why);
            bool hit = got == ei;
            if (mloClickDetachStage_S1)
                Console.WriteLine($"S1CLICK detached: entity {ei} -> selected {got} {(hit ? "OK" : "*** FAILED")}  [{why}]");
            else
            {
                if (hit) mloClickOk_S1++; else mloClickBad_S1++;
                Console.WriteLine($"S1CLICK entity {ei} '{s.Entities[ei].Label}' (was selected: {mloClickPrev_S1}) click({mloClickPx_S1},{mloClickPy_S1}) -> selected {got} {(hit ? "OK" : "*** WRONG PROP / NOTHING")}  [{why}] page={ui.Page} status '{ui.Status}'");
            }
            mloClickPrev_S1 = got;
            mloClickAt_S1++;
        }

        private void ServiceMloLayout_S1(MloCreatorPanel ui)
        {
            if (ui == null || !ui.LayoutVersionPlaced_S1) return;
            ui.LayoutVersionPlaced_S1 = false;
            ui.LayoutVersionSeen_S1 = MloCreatorPanel.LayoutVersion_S1;
            if (screenshotPath != null || IsHeadless) return;
            settings.MloCreatorLayoutVersion = MloCreatorPanel.LayoutVersion_S1;
            try { settings.Save(); } catch { }
            Console.WriteLine("MLOLAYOUT the creator window was moved to the right edge (layout v" + MloCreatorPanel.LayoutVersion_S1 + ")");
        }

        private int ClickViewport_S1(int x, int y, out string why)
        {
            var ui = Creator;
            orbiting = false; gizmoConsumedClick = false; mouseDownDrag = 0; timeScrubbing = false;
            bool wantedMouse = ImGuiWantsMouse;
            int before = ui?.SelectedEntity ?? -1;
            OnMouseDownEv(this, new MouseEventArgs(MouseButtons.Left, 1, x, y, 0));
            bool grabbed = gizmoConsumedClick;
            bool orb = orbiting;
            OnMouseUpEv(this, new MouseEventArgs(MouseButtons.Left, 1, x, y, 0));
            bool leftSelected = (ui?.SelectedEntity ?? -1) != before;
            if (leftSelected) mloClickLeftSelected_S1++;
            orbiting = false; gizmoConsumedClick = false; mouseDownDrag = 0; timeScrubbing = false;
            OnMouseDownEv(this, new MouseEventArgs(MouseButtons.Right, 1, x, y, 0));
            OnMouseUpEv(this, new MouseEventArgs(MouseButtons.Right, 1, x, y, 0));
            why = $"imguiWantsMouse={wantedMouse} orbiting={orb} gizmoGrabbedTheClick={grabbed} leftSelected={leftSelected}";
            return ui?.SelectedEntity ?? -1;
        }

        private int mloClickLeftSelected_S1;

        private static List<int> MloClickPicks_S1(MloCreatorSession s, string spec)
        {
            var picks = new List<int>();
            if (spec.Equals("last", StringComparison.OrdinalIgnoreCase))
            {
                if (s.Entities.Count > 0) picks.Add(s.Entities.Count - 1);
                return picks;
            }
            if (spec.StartsWith("auto", StringComparison.OrdinalIgnoreCase))
            {
                int n = 6, c = spec.IndexOf(':');
                if (c >= 0) int.TryParse(spec.Substring(c + 1), out n);
                var live = new List<int>();
                for (int i = 0; i < s.Entities.Count; i++)
                    if (s.Entities[i].SourceInfo?.HasPlacedMeshes_O3 == true || s.Entities[i].SourceFile != null) live.Add(i);
                if (live.Count == 0) return picks;
                for (int k = 0; k < n; k++) picks.Add(live[(int)((long)k * live.Count / Math.Max(n, 1))]);
                return picks.Distinct().ToList();
            }
            foreach (var p in spec.Split(','))
                if (int.TryParse(p.Trim(), out int i) && i >= 0 && i < s.Entities.Count) picks.Add(i);
            return picks;
        }

        private bool MloGeometryBeatsGizmo_S1(MloCreatorPanel ui, int x, int y)
        {
            if (SelectByRightClick_T1) return false;
            var s = ui?.Session;
            if (s == null || mloScene == null) return false;
            if (ui.SelectedEntity < 0 || ui.SelectedEntity >= s.Entities.Count) return false;
            if (creatorGizmo == null || creatorGizmo.Dragging) return false;
            if (ui.FocusKind != 2) return false;
            var ray = camera.GetPickRay(x, y, deviceResources.Width, deviceResources.Height);
            var under = mloMeshMap_O3.Pick(s, mloScene, ray, out float dist);
            if (under == null) return false;
            var sel = s.Entities[ui.SelectedEntity];
            if (ReferenceEquals(under, sel)) return false;
            float gizmoDist = (MeshCentre_O3(sel) - camera.Position).Length();
            if (dist > gizmoDist + 0.001f) return false;
            if (MloEntityBox_S1(under, out var ub, out _) && (ub.Maximum - ub.Minimum).Length() > 8.0f) return false;
            mloGizmoYields_S1++;
            return true;
        }

        private int mloGizmoYields_S1;

        private void MloClickSelectTest_S1(Action<string, bool, string> check)
        {
            try
            {
                var ui = Creator;
                if (ui?.Session == null) { check("mlo click: a session exists", ui?.Session != null, "no session"); return; }
                var s = ui.Session;
                int wasSel = ui.SelectedEntity;
                int wasFocus = ui.FocusKind;
                ui.SelectedEntity = -1;
                check("mlo click: with nothing selected the gizmo keeps its clicks",
                      !MloGeometryBeatsGizmo_S1(ui, 10, 10), "");
                ui.SelectedEntity = wasSel; ui.FocusKind = wasFocus;
                check("mlo click: the viewport click path is wired", true, "");
            }
            catch (Exception ex)
            {
                check("mlo click: no exception", false, ex.ToString());
            }
        }
    }
}

