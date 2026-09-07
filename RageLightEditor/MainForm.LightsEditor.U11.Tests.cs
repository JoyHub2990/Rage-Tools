using System;
using System.Linq;
using RageLightEditor.Editor;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private void SeqTest_LightsEditor_U11(Action<string, bool, string> check)
        {
            try
            {
                var axes = new Vector3[3];
                Gizmo.LocalFrame_U11(new Vector3(0, 0, -1), new Vector3(1, 0, 0), axes);
                bool ortho = Math.Abs(Vector3.Dot(axes[0], axes[1])) < 1e-5f && Math.Abs(Vector3.Dot(axes[1], axes[2])) < 1e-5f &&
                             Math.Abs(Vector3.Dot(axes[0], axes[2])) < 1e-5f;
                check("u11 local axes: Z runs down the beam and the frame is square",
                      ortho && (axes[2] - new Vector3(0, 0, -1)).Length() < 1e-5f && Math.Abs(axes[0].X - 1f) < 1e-5f,
                      $"x {axes[0]} y {axes[1]} z {axes[2]}");

                Gizmo.LocalFrame_U11(new Vector3(0, 0, -1), new Vector3(0, 0, -1), axes);
                bool okDegenerate = Math.Abs(axes[0].Length() - 1f) < 1e-4f && Math.Abs(axes[1].Length() - 1f) < 1e-4f &&
                                    Math.Abs(Vector3.Dot(axes[0], axes[2])) < 1e-4f;
                check("u11 local axes: a tangent lying along the beam still gives a usable frame",
                      okDegenerate, $"x {axes[0]} y {axes[1]}");

                var s = new Scene(null);
                var f = new LoadedFile { Path = "u11_lights.ydr" };
                s.Files.Add(f);
                var a = s.AddLight(1);
                var b = s.AddLight(1);
                var c = s.AddLight(2);
                a.Intensity = 42f; a.Falloff = 7f;
                b.Intensity = 3f; b.Falloff = 1f;
                b.Position = new Vector3(5, 0, 0);
                s.SelectedIndices.Clear();
                s.SelectedIndices.AddRange(new[] { 1, 2, 0 });

                int linked = s.LinkSelectedAsInstance();
                int ga = s.InstanceGroup(a), gb = s.InstanceGroup(b), gc = s.InstanceGroup(c);
                check("u11 link: three plain lights become one instance group",
                      linked == 2 && ga != 0 && ga == gb && gb == gc, $"linked {linked}, groups {ga}/{gb}/{gc}");
                check("u11 link: the others take the last-clicked light's settings but keep their own place",
                      Math.Abs(b.Intensity - 42f) < 1e-5f && Math.Abs(b.Falloff - 7f) < 1e-5f && b.Position.X == 5f,
                      $"intensity {b.Intensity} falloff {b.Falloff} at x {b.Position.X}");

                var d = s.AddLight(1);
                d.Intensity = 9f;
                s.SelectedIndices.Clear();
                s.SelectedIndices.AddRange(new[] { s.Lights.IndexOf(b), s.Lights.IndexOf(d) });
                s.LinkSelectedAsInstance();
                check("u11 link: a plain light linked with an instance joins that instance whatever was clicked last",
                      ga != 0 && s.InstanceGroup(d) == ga && Math.Abs(d.Intensity - 42f) < 1e-5f && Math.Abs(a.Intensity - 42f) < 1e-5f,
                      $"group {s.InstanceGroup(d)} vs {ga}, intensity {d.Intensity}");

                s.SelectedIndices.Clear();
                s.SelectedIndices.Add(s.Lights.IndexOf(c));
                int cut = s.UnlinkSelected();
                check("u11 unlink: the cut light leaves the group and the rest stay linked",
                      cut == 1 && s.InstanceGroup(c) == 0 && s.InstanceGroup(a) == ga && s.InstanceGroup(b) == ga,
                      $"cut {cut}, c {s.InstanceGroup(c)}, a {s.InstanceGroup(a)}");

                s.SelectedIndices.Clear();
                s.SelectedIndices.Add(0);
                check("u11 link: one light alone cannot be linked", s.LinkSelectedAsInstance() == 0, "needs two");
            }
            catch (Exception ex) { check("u11 lights editor", false, ex.Message); }
        }
    }
}
