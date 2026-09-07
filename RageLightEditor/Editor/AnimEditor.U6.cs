using System;
using System.Collections.Generic;
using System.Linq;
using SharpDX;

namespace RageLightEditor.Editor
{
    public class AnimEditor
    {

        public class MatRow
        {
            public int Index;
            public string Name = "";
            public string Sps = "";
            public bool AnimUv;
            public bool HasParams;
            public int MeshCount;
            public string Label => $"[{Index}] {Name}";
        }

        public readonly List<MatRow> Materials = new List<MatRow>();
        public string ModelName = "";
        public string ModelPath = "";
        public bool HasModel;

        public int AnimUvCount => Materials.Count(m => m.AnimUv);

        public UvAnimClip Clip = new UvAnimClip();
        public int SelectedMaterial = -1;
        public int TimelineChannel;

        public UvAnimTrack SelectedTrack
        {
            get
            {
                if (SelectedMaterial < 0 || SelectedMaterial >= Materials.Count) return null;
                return Clip?.Find(Materials[SelectedMaterial].Index);
            }
        }

        public MatRow SelectedRow =>
            SelectedMaterial >= 0 && SelectedMaterial < Materials.Count ? Materials[SelectedMaterial] : null;

        public bool Playing;
        public float Time;
        public float TimeScale = 1.0f;
        public bool PreviewEnabled = true;

        public int FrameOf(float t) => (int)Math.Round(t * Math.Max(Clip?.Fps ?? 30, 1));

        public void Advance(float dt)
        {
            if (!Playing || Clip == null) return;
            Time += dt * Math.Max(TimeScale, 0.01f);
            if (Time >= Clip.Duration)
            {
                if (Clip.Loop) Time = Clip.Wrap(Time);
                else { Time = Clip.Duration; Playing = false; }
            }
        }

        public void SeekTo(float t)
        {
            if (Clip == null) { Time = 0; return; }
            Time = Math.Max(0f, Math.Min(t, Clip.Duration));
        }

        public void SeekFrame(int frame)
        {
            int fps = Math.Max(Clip?.Fps ?? 30, 1);
            SeekTo(frame / (float)fps);
        }

        public bool RequestOpenFile;
        public bool RequestTakeOpenModel;
        public string RequestOpenArchive;
        public bool RequestClose;
        public bool RequestFrame;
        public bool RequestExport;
        public bool RequestSaveProject;
        public bool RequestOpenProject;
        public int RequestAddTrack = -1;
        public int RequestRemoveTrack = -1;
        public int RequestPreset = -1;
        public bool RequestBake;

        public string Status = "";
        public string ExportStatus = "";
        public string LastExportPath = "";

        public Vector4 LiveUv0 = new Vector4(1, 0, 0, 0);
        public Vector4 LiveUv1 = new Vector4(0, 1, 0, 0);
        public int LiveMeshes;

        public void Clear()
        {
            Materials.Clear();
            ModelName = ModelPath = "";
            HasModel = false;
            SelectedMaterial = -1;
            Clip = new UvAnimClip();
            Time = 0; Playing = false;
            LiveMeshes = 0;
            LiveUv0 = new Vector4(1, 0, 0, 0);
            LiveUv1 = new Vector4(0, 1, 0, 0);
        }

        public void SyncTracks()
        {
            if (Clip?.Tracks == null) return;
            foreach (var t in Clip.Tracks)
            {
                if (t == null) continue;
                var byName = Materials.FirstOrDefault(m => string.Equals(m.Name, t.MaterialName, StringComparison.OrdinalIgnoreCase));
                if (byName != null) { t.MaterialIndex = byName.Index; continue; }
                var byIndex = Materials.FirstOrDefault(m => m.Index == t.MaterialIndex);
                if (byIndex != null && string.IsNullOrEmpty(t.MaterialName)) t.MaterialName = byIndex.Name;
            }
        }

        public bool IsAnimated(MatRow m) => m != null && Clip?.Find(m.Index) != null;

        public bool TryEvaluate(int materialIndex, out Vector4 uv0, out Vector4 uv1)
        {
            uv0 = new Vector4(1, 0, 0, 0);
            uv1 = new Vector4(0, 1, 0, 0);
            var t = Clip?.Find(materialIndex);
            if (t == null || !t.Enabled) return false;
            t.Evaluate(Clip.Wrap(Time), out uv0, out uv1);
            return true;
        }

        public string Summary()
        {
            if (!HasModel) return "No model open. Open a .ydr, or take the one open in Lights.";
            if (Materials.Count == 0) return $"{ModelName}: no materials found in it.";
            if (AnimUvCount == 0)
                return $"{ModelName}: {Materials.Count} material(s), NONE of whose presets declare " +
                       "USE_ANIMATED_UVS - the game will not animate any of them. Switch one to a " +
                       "preset that does (Materials workspace) and come back.";
            int tracks = Clip?.Tracks?.Count(t => t != null && t.Enabled) ?? 0;
            if (tracks == 0)
                return $"{ModelName}: {AnimUvCount} of {Materials.Count} material(s) can be animated. " +
                       "Pick one and add a track.";
            return $"{ModelName}: {tracks} track(s), {Clip.Duration:0.##} s at {Clip.Fps} fps, " +
                   $"t = {Time:0.00} s (frame {FrameOf(Time)}/{Clip.FrameCount - 1})" +
                   (Playing ? ", playing" : "") + $", {LiveMeshes} mesh(es) animating";
        }
    }
}

