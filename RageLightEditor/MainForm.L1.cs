using System;
using System.Globalization;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {

        public int InteriorCullOwnDrawnMin { get; private set; } = -1;
        private string interiorCullOwnMinWhere = "";
        private int interiorCullInsideFrames;

        private void ReportInteriorCull_L1()
        {
            if (interiorCull == null || interiorCull.Inside == null) return;
            interiorCullInsideFrames++;
            if (interiorCull.OwnTested <= 0) return;
            int drawn = interiorCull.OwnTested - interiorCull.HiddenRooms;
            if (InteriorCullOwnDrawnMin < 0 || drawn < InteriorCullOwnDrawnMin)
            {
                InteriorCullOwnDrawnMin = drawn;
                interiorCullOwnMinWhere = $"{interiorCull.Inside.Archetype?.Name}/{interiorCull.RoomName} at {camera.Position.X:0},{camera.Position.Y:0},{camera.Position.Z:0}";
                Console.WriteLine($"INTCULLOWN own props drawn {drawn} of {interiorCull.OwnTested} (rooms seen {interiorCull.RoomsVisible}) in {interiorCullOwnMinWhere}");
            }
        }

        private void CullerRoomOverride_L1(ref CodeWalker.GameFiles.MCMloRoomDef room, ref CodeWalker.GameFiles.MloInstanceData inst, ref int roomIndex)
        {
            if (interiorCull == null || !interiorCull.Enabled || worldRender?.InteriorCull == null) return;
            var ent = interiorCull.Inside; var arch = interiorCull.InsideArch;
            int r = interiorCull.Room;
            if (ent?.MloInstance == null || arch?.rooms == null || r <= 0 || r >= arch.rooms.Length || arch.rooms[r] == null) return;
            room = arch.rooms[r]; inst = ent.MloInstance; roomIndex = r;
        }

        private string InteriorCullSummary_L1() =>
            interiorCull == null ? "INTCULLSUM off" :
            $"INTCULLSUM insideFrames {interiorCullInsideFrames} ownDrawnMin {InteriorCullOwnDrawnMin} ({interiorCullOwnMinWhere}) roomSwitches {interiorCull.RoomSwitches} insideFlips {interiorCull.InsideFlips} refused {interiorCull.InteriorsRefused}";

        private static readonly string switchSpaceSpec = Environment.GetEnvironmentVariable("RLE_SWITCHSPACE");
        private int switchSpaceTick;
        private bool switchSpaceDone;

        partial void OnWorldTick_L1()
        {
            if (switchSpaceDone || string.IsNullOrEmpty(switchSpaceSpec) || screenshotPath == null) return;
            if (DebugMlo != null && !debugMloDone) return;
            if (!worldBuilt) return;
            var f = switchSpaceSpec.Split(',');
            static float N(string[] a, int i, float d) =>
                i < a.Length && float.TryParse(a[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : d;
            int at = (int)N(f, 1, 30);
            if (++switchSpaceTick < at) { screenshotFrames = Math.Max(screenshotFrames, 2); return; }
            switchSpaceDone = true;
            var space = LightPanel.Space.World;
            if (f.Length > 0 && SpaceNames.TryParse(f[0], out var sp)) space = sp;
            Console.WriteLine($"SWITCHSPACE {panel.Workspace} -> {space} at tick {switchSpaceTick} (scene meshes {scene.Files.Count}, mlo {(scene.MloInfo?.MloName ?? "none")}, interiors {scene.MloInfo?.Interiors?.Count ?? 0})");
            panel.SwitchWorkspace(space);
            if (space == LightPanel.Space.World)
            {
                if (f.Length > 4)
                {
                    worldStart = new SharpDX.Vector3(N(f, 2, -70), N(f, 3, -1103), N(f, 4, 120));
                    worldAimYaw = N(f, 5, 1.4f);
                    worldAimPitch = N(f, 6, 0.45f);
                }
                worldWarmup = 0;
                screenshotFrames = Math.Max(screenshotFrames, 2);
                Console.WriteLine($"SWITCHSPACE world camera {camera.Position} target {camera.Target} (start {(worldStart.HasValue ? worldStart.Value.ToString() : "as is")})");
            }
        }
    }
}

