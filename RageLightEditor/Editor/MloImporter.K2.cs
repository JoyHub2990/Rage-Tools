using System;
using System.Collections.Generic;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Editor
{
    public class MloPlacedInterior
    {
        public MloArchetype Arch;
        public Vector3 Position;
        public Quaternion Orientation;
        public string Name => Arch?.Name ?? Arch?.Hash.ToString() ?? "";

        public Vector3 ToLocal(Vector3 world) => Vector3.Transform(world - Position, Quaternion.Invert(Orientation));
        public Vector3 ToWorld(Vector3 local) => Position + Vector3.Transform(local, Orientation);
    }

    public partial class MloImportResult
    {
        public readonly List<MloPlacedInterior> Interiors = new List<MloPlacedInterior>();

        internal void NoteInterior_K2(MloArchetype mlo, Vector3 pos, Quaternion rot)
        {
            if (mlo == null) return;
            foreach (var i in Interiors) if (ReferenceEquals(i.Arch, mlo) && (i.Position - pos).LengthSquared() < 1e-6f) return;
            Interiors.Add(new MloPlacedInterior { Arch = mlo, Position = pos, Orientation = rot });
        }
    }
}

