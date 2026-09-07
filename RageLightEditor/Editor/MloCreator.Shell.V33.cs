using System;
using System.Linq;

namespace RageLightEditor.Editor
{
    public partial class MloCreatorSession
    {
        public bool? ShellInLimbo_V33;

        public bool ShellGoesInLimbo_V33 => ShellInLimbo_V33 ?? true;

        public string ShellEntityName_V33
        {
            get
            {
                var n = (ShellName ?? "").Trim().ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(n)) return n;
                var p = ShellFile?.Path;
                return string.IsNullOrWhiteSpace(p) ? (Name ?? "").Trim().ToLowerInvariant()
                                                    : System.IO.Path.GetFileNameWithoutExtension(p).ToLowerInvariant();
            }
        }

        public void SyncShellEntity_V33()
        {
            var existing = Entities.FirstOrDefault(e => e != null && e.IsShell_V33);
            if (!ShellGoesInLimbo_V33 || ShellFile == null)
            {
                if (existing != null)
                {
                    int at = Entities.IndexOf(existing);
                    Entities.Remove(existing);
                    ShiftPortalAttachments_V67(at, -1);
                }
                return;
            }
            var nm = ShellEntityName_V33;
            if (string.IsNullOrWhiteSpace(nm)) return;

            if (existing != null) { existing.ArchetypeName = nm; existing.RoomOverride = 0; existing.Include = true; return; }
            ShiftPortalAttachments_V67(0, +1);
            Entities.Insert(0, new MloCreatorEntity
            {
                ArchetypeName = nm,
                Position = SharpDX.Vector3.Zero,
                Rotation = SharpDX.Quaternion.Identity,
                Scale = SharpDX.Vector3.One,
                LodDist = Math.Max(LodDist, 200.0f),
                Include = true,
                RoomOverride = 0,
                IsShell_V33 = true,
                SourceFile = ShellFile,
            });
        }

        private void ShiftPortalAttachments_V67(int fromIndex, int delta)
        {
            foreach (var p in Portals)
            {
                if (p?.Attached == null) continue;
                for (int i = p.Attached.Count - 1; i >= 0; i--)
                {
                    if (p.Attached[i] < fromIndex) continue;
                    if (delta < 0 && p.Attached[i] == fromIndex) { p.Attached.RemoveAt(i); continue; }
                    p.Attached[i] += delta;
                }
            }
        }

        public bool ShellDrawnTwice_V34 => ShellGoesInLimbo_V33 && !AssetLess && ShellFile != null;

        public string ShellInLimboNote_V33 =>
            ShellGoesInLimbo_V33
                ? (AssetLess
                    ? "The interior has no geometry of its own, so the shell must be an entity - this is right."
                    : "The archetype ALSO names its own drawable, so the shell would be drawn twice. "
                      + "Set the interior to assetless (the button beside this) or untick the box.")
                : (AssetLess
                    ? "An assetless interior draws nothing but its entities - with the shell left out it is an empty box."
                    : "The archetype names its own drawable, which IS the shell - so it does not need to be an entity as well.");
    }
}

