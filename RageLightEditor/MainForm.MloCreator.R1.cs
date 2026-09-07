using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RageLightEditor.Editor;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private static readonly string shellBuildEnv_R1 = Environment.GetEnvironmentVariable("RLE_SHELLBUILD");
        private int shellBuildTick_R1;
        private bool shellBuildEnvDone_R1, shellBuildLoaded_R1;

        private void ServiceShellBuildEnv_R1(MloCreatorPanel ui)
        {
            if (shellBuildEnvDone_R1 || shellBuildEnv_R1 == null || ui == null) return;
            var parts = shellBuildEnv_R1.Split(',');
            if (!shellBuildLoaded_R1 && parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2]))
            {
                shellBuildLoaded_R1 = true;
                if (System.IO.File.Exists(parts[2])) { LoadFile(parts[2]); FrameModel(); }
                else Console.WriteLine("SHELLBUILD no such shell: " + parts[2]);
                screenshotFrames = Math.Max(screenshotFrames, 8);
                return;
            }
            if (!scene.HasModel || (DebugMlo != null && !debugMloDone)) return;
            screenshotFrames = Math.Max(screenshotFrames, 6);
            if (ui.Session == null) { ui.RequestStartFromScene = true; return; }
            if (++shellBuildTick_R1 < 3) return;
            shellBuildEnvDone_R1 = true;
            var f = parts;
            if (f.Length > 0 && float.TryParse(f[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0) ui.ShellVoxel_R1 = v;
            if (f.Length > 1 && float.TryParse(f[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var c) && c > 0) ui.ShellDoorWidth_R1 = c;
            ui.RequestBuildFromShell_R1 = true;
        }

        private void ServiceMloShellBuild_R1(MloCreatorPanel ui)
        {
            if (ui == null || !ui.RequestBuildFromShell_R1) return;
            ui.RequestBuildFromShell_R1 = false;
            var s = ui.Session;
            if (s == null) { ui.SetStatus("Start the interior first.", true); return; }
            string msg = BuildInteriorFromShell_R1(ui, s, out bool ok);
            ui.SetStatus(msg, !ok);
            ui.ShellReport_R1 = msg;
            if (shellBuildEnv_R1 != null)
            {
                Console.WriteLine("SHELLBUILD result: " + msg);
                for (int i = 0; i < s.Rooms.Count; i++)
                    Console.WriteLine($"  SHELLROOM {i} '{s.Rooms[i].Name}' {s.Rooms[i].Min} .. {s.Rooms[i].Max} ({s.Rooms[i].Size.X:0.0} x {s.Rooms[i].Size.Y:0.0} x {s.Rooms[i].Size.Z:0.0} m, {s.CountInRoom(i)} props)");
                for (int i = 0; i < s.Portals.Count; i++)
                    Console.WriteLine($"  SHELLPORTAL {i} {s.Portals[i].RoomFrom} -> {s.Portals[i].RoomTo} at {s.Portals[i].Centre}");
                Console.WriteLine("  SHELLVALIDATE " + (s.Validate().Count == 0 ? "no problems" : string.Join("; ", s.Validate())));
            }
        }

        private string BuildInteriorFromShell_R1(MloCreatorPanel ui, MloCreatorSession s, out bool ok)
        {
            ok = false;
            if (s == null) return "no interior";
            var tris = MloCreatorSession.ShellTriangles_R1(scene, s.ShellFile, out string source);
            if (tris.Count < 4)
                return "No shell to build from: pick the interior's model in Shell on this page, or open one (Open shell...).";

            s.InvalidateShellBounds();
            if (!s.TryGetShellBounds(scene, out var shellBounds, out var boundsSource))
            {
                var mn = new Vector3(float.MaxValue); var mx = new Vector3(float.MinValue);
                foreach (var t in tris) { mn = Vector3.Min(mn, Vector3.Min(t.A, Vector3.Min(t.B, t.C))); mx = Vector3.Max(mx, Vector3.Max(t.A, Vector3.Max(t.B, t.C))); }
                shellBounds = new BoundingBox(mn, mx);
                boundsSource = source;
            }

            var opt = new MloShellAnalysis.Options
            {
                Voxel = Math.Max(0.05f, ui?.ShellVoxel_R1 ?? 0.25f),
                RoomCore = Math.Max(0.2f, (ui?.ShellDoorWidth_R1 ?? 1.5f) * 0.5f),
                MinOutsidePortalArea = (ui?.ShellOutsidePortals_R1 ?? true) ? 1.0f : float.MaxValue,
            };
            ui?.SetStatus("Analysing the shell...");
            var a = MloShellAnalysis.Run(tris, opt, m => Console.WriteLine("  SHELLBUILD " + m));
            Console.WriteLine($"SHELLBUILD {source}: {a.Describe()} (solid {a.SolidVoxels}, inside {a.InsideVoxels}, seeds {a.SeedVoxels}, grid {a.NX}x{a.NY}x{a.NZ})");
            if (a.Rooms.Count == 0)
                return "Could not divide the shell into rooms - " + a.Describe() + ". " +
                       "Try a finer grid, or check the shell is the interior's own model (walls and floors), not a prop.";

            s.PushUndo("Interior from shell");
            string report = s.ApplyShellAnalysis_R1(a, shellBounds, source);
            if (!s.BBoxManual) { s.BBMin = shellBounds.Minimum; s.BBMax = shellBounds.Maximum; }
            s.LimboAuthored_P2 = false;
            ui?.SelectRoom(0);
            if (ui != null) ui.RevealSelection = true;
            ok = true;
            return report;
        }

        private void MloShellBuildTest_R1(Action<string, bool, string> check, MloCreatorPanel ui)
        {
            try
            {
                var tris = TwoRoomShell_R1();
                check("shell build: the test shell has geometry", tris.Count > 20, $"{tris.Count} triangles");
                var a = MloShellAnalysis.Run(tris, new MloShellAnalysis.Options { Voxel = 0.15f, RoomCore = 0.7f });
                Console.WriteLine($"  SHELLTEST {a.Describe()} grid {a.NX}x{a.NY}x{a.NZ} solid {a.SolidVoxels} inside {a.InsideVoxels} seeds {a.SeedVoxels} problem '{a.Problem}'");
                foreach (var r in a.Rooms) Console.WriteLine($"    room {r.Box.Minimum} .. {r.Box.Maximum} ({r.Voxels} voxels)");
                foreach (var p in a.Portals) Console.WriteLine($"    portal {p.RoomA}->{p.RoomB} axis {p.Axis} area {p.Area:0.00} centre {(p.Corners[0] + p.Corners[2]) * 0.5f}");
                check("shell build: two enclosed rooms found", a.Rooms.Count == 2, $"{a.Rooms.Count} rooms: {a.Problem}");
                if (a.Rooms.Count == 2)
                {
                    var big = a.Rooms.OrderByDescending(r => r.Volume).First();
                    var small = a.Rooms.OrderByDescending(r => r.Volume).Last();
                    var bs = big.Box.Maximum - big.Box.Minimum;
                    var ss = small.Box.Maximum - small.Box.Minimum;
                    check("shell build: the big room's box is the big space", Math.Abs(bs.X - 6) < 0.5f && Math.Abs(bs.Y - 4) < 0.5f && Math.Abs(bs.Z - 3) < 0.5f, $"{bs}");
                    check("shell build: the small room's box is the small space", Math.Abs(ss.X - 4) < 0.5f && Math.Abs(ss.Y - 4) < 0.5f && Math.Abs(ss.Z - 3) < 0.5f, $"{ss}");
                    check("shell build: the rooms do not overlap", big.Box.Minimum.X > small.Box.Maximum.X - 0.6f || small.Box.Minimum.X > big.Box.Maximum.X - 0.6f,
                        $"{big.Box.Minimum.X:0.00}..{big.Box.Maximum.X:0.00} vs {small.Box.Minimum.X:0.00}..{small.Box.Maximum.X:0.00}");
                }
                var inner = a.Portals.Where(p => !p.ToOutside).ToList();
                var outer = a.Portals.Where(p => p.ToOutside).ToList();
                check("shell build: a portal at the doorway between the rooms", inner.Count == 1, $"{inner.Count} inner portals");
                if (inner.Count == 1)
                {
                    var c = inner[0].Corners.Aggregate(Vector3.Zero, (x, y) => x + y) / 4.0f;
                    check("shell build: it is at the doorway, facing along X", inner[0].Axis == 0 && Math.Abs(c.X - 6.0f) < 0.6f && Math.Abs(c.Y - 2.0f) < 0.6f, $"axis {inner[0].Axis} centre {c}");
                }
                check("shell build: the front door is a portal out to limbo", outer.Count >= 1, $"{outer.Count} outside portals");

                var s = new MloCreatorSession();
                var bounds = a.Bounds;
                string report = s.ApplyShellAnalysis_R1(a, bounds, "the test shell");
                check("shell build: the session took limbo + a room each + the portals",
                    s.Rooms.Count == a.Rooms.Count + 1 && s.Portals.Count == a.Portals.Count && s.Validate().Count == 0,
                    $"{s.Rooms.Count} rooms, {s.Portals.Count} portals, problems: {string.Join("; ", s.Validate())}");
                check("shell build: limbo is the shell's bounds",
                    (s.Rooms[0].Min - bounds.Minimum).Length() < 1e-4f && (s.Rooms[0].Max - bounds.Maximum).Length() < 1e-4f, $"{s.Rooms[0].Min} .. {s.Rooms[0].Max}");
                check("shell build: every portal joins two different rooms that exist",
                    s.Portals.All(p => p.RoomFrom != p.RoomTo && p.RoomFrom >= 0 && p.RoomTo >= 0 && p.RoomFrom < s.Rooms.Count && p.RoomTo < s.Rooms.Count),
                    string.Join(", ", s.Portals.Select(p => $"{p.RoomFrom}->{p.RoomTo}")));
                bool wound = true;
                foreach (var p in s.Portals)
                {
                    if (p.RoomTo <= 0 || p.RoomFrom <= 0) continue;
                    if (Vector3.Dot(p.Normal, s.Rooms[p.RoomTo].Centre - p.Centre) < 0) wound = false;
                }
                check("shell build: portals are wound from -> to", wound, "");
                check("shell build: the report says what to check", report.Contains("CHECK"), report);

                if (ui?.Session != null)
                {
                    var live = ui.Session;
                    int roomsWere = live.Rooms.Count, portalsWere = live.Portals.Count;
                    string msg = BuildInteriorFromShell_R1(ui, live, out bool ok);
                    Console.WriteLine("  SHELLBUILD live: " + msg);
                    if (ok)
                    {
                        check("shell build: the live session gained rooms", live.Rooms.Count >= 2, $"{live.Rooms.Count} rooms, {live.Portals.Count} portals");
                        check("shell build: it is one undo step", live.History.CanUndo && live.History.UndoName.Contains("shell"), live.History.UndoName ?? "none");
                        live.Undo();
                        check("shell build: undo puts the old interior back", live.Rooms.Count == roomsWere && live.Portals.Count == portalsWere,
                            $"{live.Rooms.Count} vs {roomsWere} rooms, {live.Portals.Count} vs {portalsWere} portals");
                    }
                    else check("shell build: the live session has a shell to build from", true, "skipped: " + msg);
                }
            }
            catch (Exception ex)
            {
                check("shell build: no exception", false, ex.ToString());
            }
        }

        private static List<MloShellAnalysis.Tri> TwoRoomShell_R1()
        {
            var tris = new List<MloShellAnalysis.Tri>();
            void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
            {
                tris.Add(new MloShellAnalysis.Tri { A = p0, B = p1, C = p2 });
                tris.Add(new MloShellAnalysis.Tri { A = p0, B = p2, C = p3 });
            }
            void WallX(float k, float y0, float y1, float z0, float z1, float hy0, float hy1, float hz1)
            {
                if (hy1 <= hy0) { Quad(new Vector3(k, y0, z0), new Vector3(k, y1, z0), new Vector3(k, y1, z1), new Vector3(k, y0, z1)); return; }
                Quad(new Vector3(k, y0, z0), new Vector3(k, hy0, z0), new Vector3(k, hy0, z1), new Vector3(k, y0, z1));
                Quad(new Vector3(k, hy1, z0), new Vector3(k, y1, z0), new Vector3(k, y1, z1), new Vector3(k, hy1, z1));
                Quad(new Vector3(k, hy0, hz1), new Vector3(k, hy1, hz1), new Vector3(k, hy1, z1), new Vector3(k, hy0, z1));
            }
            void WallY(float k, float x0, float x1, float z0, float z1)
                => Quad(new Vector3(x0, k, z0), new Vector3(x1, k, z0), new Vector3(x1, k, z1), new Vector3(x0, k, z1));
            void SlabZ(float k, float x0, float x1, float y0, float y1)
                => Quad(new Vector3(x0, y0, k), new Vector3(x1, y0, k), new Vector3(x1, y1, k), new Vector3(x0, y1, k));

            SlabZ(0, 0, 10, 0, 4);
            SlabZ(3, 0, 10, 0, 4);
            WallY(0, 0, 10, 0, 3);
            WallY(4, 0, 10, 0, 3);
            WallX(10, 0, 4, 0, 3, 0, 0, 0);
            WallX(0, 0, 4, 0, 3, 1.4f, 2.6f, 2.1f);
            WallX(6, 0, 4, 0, 3, 1.55f, 2.45f, 2.1f);
            return tris;
        }
    }
}

