using System.Collections.Generic;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private readonly HashSet<LoadedFile> fivemTouched_U18 = new HashSet<LoadedFile>();

        private int TouchFiveMFiles_U18()
        {
            int placed = 0;
            foreach (var f in scene.Files) if (f.HasPlacement) placed++;
            for (int i = 0; i < scene.Lights.Count; i++)
            {
                if (!scene.IsSelected(i)) continue;
                var owner = scene.OwnerFile(scene.Lights[i]);
                if (owner != null) fivemTouched_U18.Add(owner);
            }
            if (fivemTouched_U18.Count > 64) fivemTouched_U18.RemoveWhere(f => !scene.Files.Contains(f));
            return placed;
        }

        public static bool SendPlacedFile_U18(int placedFiles, bool hasPlacement, bool touched) => placedFiles < 2 || !hasPlacement || touched;
    }
}
