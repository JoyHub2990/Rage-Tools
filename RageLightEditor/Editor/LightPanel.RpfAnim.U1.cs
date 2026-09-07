using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class LightPanel
    {
        private const float RowOpenSeconds_U1 = 0.20f;
        private const float ListRevealSeconds_U1 = 0.17f;

        private readonly HashSet<string> rpfWasOpen_U1 = new HashSet<string>();

        private void DrawRpfNodeOpening_U1(RpfExplorer.Node n)
        {
            if (n == null) return;
            Rpf.BackgroundOpen_U1 = true;
            string id = n.Path ?? n.Label ?? "?";

            bool open = n.Expanded;
            bool was = rpfWasOpen_U1.Contains(id);
            if (open != was)
            {
                if (open) { rpfWasOpen_U1.Add(id); UiAnim.Start("rpf.row." + id); }
                else rpfWasOpen_U1.Remove(id);
                if (rpfWasOpen_U1.Count > 512) rpfWasOpen_U1.Clear();
            }

            var mn = ImGui.GetItemRectMin();
            var mx = ImGui.GetItemRectMax();
            var dl = ImGui.GetWindowDrawList();
            var col = RpfWorkspaceColour;

            float t = UiAnim.Progress("rpf.row." + id, RowOpenSeconds_U1);
            if (t < 1.0f)
            {
                float e = UiAnim.EaseOut(t);
                float x1 = mn.X + (mx.X - mn.X) * e;
                float a = (1.0f - t) * 0.30f;
                dl.AddRectFilled(new Vector2(mn.X, mn.Y), new Vector2(x1, mx.Y),
                                 ImGui.GetColorU32(new Vector4(col.X, col.Y, col.Z, a)), 3.0f);
                if (open)
                {
                    float h = (mx.Y - mn.Y) * 1.6f * e;
                    float gx = mn.X + ImGui.GetTreeNodeToLabelSpacing() * 0.5f;
                    dl.AddLine(new Vector2(gx, mx.Y), new Vector2(gx, mx.Y + h),
                               ImGui.GetColorU32(new Vector4(col.X, col.Y, col.Z, (1.0f - t) * 0.55f)), 1.5f);
                }
            }

            if (!Rpf.IsOpening_U1(n.FsPath)) return;
            var job = Rpf.OpenJob_U1Current;
            if (job == null) return;
            float w = Math.Max(24.0f, mx.X - mn.X - 6.0f);
            float y = mx.Y - 2.0f;
            dl.AddRectFilled(new Vector2(mn.X + 3, y), new Vector2(mn.X + 3 + w, y + 2.0f),
                             ImGui.GetColorU32(new Vector4(col.X, col.Y, col.Z, 0.18f)));
            dl.AddRectFilled(new Vector2(mn.X + 3, y), new Vector2(mn.X + 3 + w * job.Fraction, y + 2.0f),
                             ImGui.GetColorU32(new Vector4(col.X, col.Y, col.Z, 0.95f)));
        }

        private object rpfLastListKey_U1;
        private string rpfLastFilterKey_U1 = "\0";
        private bool rpfRevealPushed_U1;

        private void BeginRpfReveal_U1()
        {
            rpfRevealPushed_U1 = false;
            object key = Rpf.Current;
            string fkey = (Rpf.Filter ?? "") + "|" + rpfSearch;
            if (!ReferenceEquals(key, rpfLastListKey_U1) || fkey != rpfLastFilterKey_U1)
            {
                rpfLastListKey_U1 = key;
                rpfLastFilterKey_U1 = fkey;
                UiAnim.Start("rpf.list");
            }

            float t = UiAnim.Progress("rpf.list", ListRevealSeconds_U1);
            if (t >= 1.0f) return;
            float e = UiAnim.EaseOut(t);
            rpfRevealPushed_U1 = true;
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * (0.15f + 0.85f * e));
            ImGui.Indent(12.0f * (1.0f - e));
        }

        private void EndRpfReveal_U1()
        {
            if (!rpfRevealPushed_U1) return;
            rpfRevealPushed_U1 = false;
            float t = UiAnim.Progress("rpf.list", ListRevealSeconds_U1);
            ImGui.Unindent(12.0f * (1.0f - UiAnim.EaseOut(t)));
            ImGui.PopStyleVar();
        }

        private void DrawRpfOpenProgress_U1()
        {
            Rpf.BackgroundOpen_U1 = true;
            Rpf.PumpOpenJob_U1();
            var job = Rpf.OpenJob_U1Current;
            if (job == null)
            {
                var last = Rpf.OpenJob_U1Last;
                if (last == null || last.Clock.Elapsed.TotalSeconds > 2.5) return;
                ImGui.PushStyleColor(ImGuiCol.Text, RpfWorkspaceColour);
                ImGui.TextUnformatted($"Opened {last.Total} archive{(last.Total == 1 ? "" : "s")} - " +
                                      $"{last.Entries:N0} entries in {last.Seconds:0.00}s");
                ImGui.PopStyleColor();
                return;
            }

            var col = RpfWorkspaceColour;
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, col);
            ImGui.ProgressBar(job.Fraction, new Vector2(-1, 6.0f), "");
            ImGui.PopStyleColor();
            ImGui.PushStyleColor(ImGuiCol.Text, col);
            ImGui.TextUnformatted(Rpf.OpenJobText_U1());
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("These archives have not been read before, so their tables of contents are\n" +
                                 "being decrypted and walked on a background thread - the editor stays live\n" +
                                 "while it happens. The bar is bytes actually read, not an estimate.\n" +
                                 "Archives the game loader already opened cost nothing and never show this.");
        }

        public static void ModelViewOpened_U1() => UiAnim.Start("rpf.mv");

        private const float ModelViewSeconds_U1 = 0.18f;

        public static void DrawModelViewTransition_U1()
        {
            float t = UiAnim.Progress("rpf.mv", ModelViewSeconds_U1);
            if (t >= 1.0f) return;
            var mn = ImGui.GetItemRectMin();
            var mx = ImGui.GetItemRectMax();
            if (mx.X - mn.X < 4.0f || mx.Y - mn.Y < 4.0f) return;

            var dl = ImGui.GetWindowDrawList();
            var bg = ImGui.GetStyle().Colors[(int)ImGuiCol.WindowBg];
            float e = UiAnim.Ease(t);
            dl.AddRectFilled(mn, mx, ImGui.GetColorU32(new Vector4(bg.X, bg.Y, bg.Z, 1.0f - e)));

            var col = ArchiveWorkspaceColour;
            float y = mn.Y + (mx.Y - mn.Y) * UiAnim.EaseOut(t);
            float a = MathF.Sin(t * MathF.PI) * 0.8f;
            dl.AddLine(new Vector2(mn.X, y), new Vector2(mx.X, y),
                       ImGui.GetColorU32(new Vector4(col.X, col.Y, col.Z, a)), 2.0f);
        }
    }
}

