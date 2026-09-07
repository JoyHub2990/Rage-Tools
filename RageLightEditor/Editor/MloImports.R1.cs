using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor.Editor
{
    public sealed class MloImportRegistry
    {
        public sealed class Entry
        {
            public string Path = "";
            public string Name => string.IsNullOrEmpty(Path) ? "(unnamed)" : System.IO.Path.GetFileName(Path);
            public MloImportResult Result;
            public RenderModel Model;
            public readonly List<MloProp> Props = new List<MloProp>();
            public int MeshCount => Model?.Meshes?.Count ?? 0;
            public int EntityCount => Result?.Placed ?? 0;
            public IEnumerable<string> ArchetypeNames =>
                Result?.Ytyps?.SelectMany(y => y.Archetypes ?? new List<CodeWalker.GameFiles.Archetype>())
                    .Where(a => a != null).Select(a => a.Name ?? a.Hash.ToString()) ?? Enumerable.Empty<string>();
        }

        private readonly List<Entry> entries = new List<Entry>();
        public IReadOnlyList<Entry> Entries => entries;
        public int Count => entries.Count;

        public int Version { get; private set; }

        public void Clear() { entries.Clear(); Version++; }

        public Entry Note(string path, MloImportResult result, RenderModel model, IEnumerable<MloProp> props)
        {
            var e = entries.FirstOrDefault(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase));
            if (e == null) { e = new Entry { Path = path ?? "" }; entries.Add(e); }
            e.Result = result; e.Model = model;
            e.Props.Clear();
            if (props != null) foreach (var p in props) if (p != null && !e.Props.Contains(p)) e.Props.Add(p);
            Version++;
            return e;
        }

        public Entry Find(string path) =>
            entries.FirstOrDefault(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase));

        public bool Remove(Entry e)
        {
            bool ok = entries.Remove(e);
            if (ok) Version++;
            return ok;
        }

        public bool StillUsed(MloProp p, Entry skip)
        {
            foreach (var e in entries)
            {
                if (ReferenceEquals(e, skip)) continue;
                if (e.Props.Contains(p)) return true;
            }
            return false;
        }

        public MloImportResult MergeRemaining()
        {
            if (entries.Count == 0) return null;
            var outp = new MloImportResult();
            bool anyFocus = false;
            foreach (var e in entries)
            {
                var r = e.Result;
                if (r == null) continue;
                outp.Ytyps.AddRange(r.Ytyps);
                outp.Entities.AddRange(r.Entities);
                foreach (var p in r.Props) if (!outp.Props.Contains(p)) outp.Props.Add(p);
                outp.Interiors.AddRange(r.Interiors);
                outp.EntityCount += r.EntityCount; outp.Placed += r.Placed; outp.Missing += r.Missing;
                outp.UniqueProps += r.UniqueProps; outp.LocalFiles += r.LocalFiles; outp.LocalArchetypes += r.LocalArchetypes;
                outp.LightCount += r.LightCount; outp.Oversized += r.Oversized; outp.Proxies += r.Proxies;
                outp.LodSkipped += r.LodSkipped; outp.VegetationSkipped += r.VegetationSkipped;
                outp.MloInstances += r.MloInstances; outp.ShellMeshes += r.ShellMeshes;
                outp.UntexturedMeshes += r.UntexturedMeshes;
                outp.Seconds += r.Seconds;
                for (int i = 0; i < r.RoomTimecycleHashes.Count; i++)
                {
                    outp.RoomTimecycleHashes.Add(r.RoomTimecycleHashes[i]);
                    outp.RoomTimecycles.Add(i < r.RoomTimecycles.Count ? r.RoomTimecycles[i] : null);
                }
                foreach (var n in r.MissingNames) if (outp.MissingNames.Count < 40 && !outp.MissingNames.Contains(n)) outp.MissingNames.Add(n);
                foreach (var n in r.MloInstanceNames) if (!outp.MloInstanceNames.Contains(n)) outp.MloInstanceNames.Add(n);
                foreach (var n in r.FailedNames) if (!outp.FailedNames.Contains(n)) outp.FailedNames.Add(n);
                if (r.HasFocus)
                {
                    outp.FocusBounds = anyFocus ? BoundingBox.Merge(outp.FocusBounds, r.FocusBounds) : r.FocusBounds;
                    outp.HasFocus = true; anyFocus = true;
                }
            }
            outp.MloName = entries.Count == 1
                ? (entries[0].Result?.MloName ?? Path.GetFileNameWithoutExtension(entries[0].Path))
                : $"{entries.Count} interiors";
            return outp;
        }

        public string Summary(Entry e)
        {
            if (e == null) return "";
            int props = e.Props.Count;
            return $"{e.MeshCount} mesh{(e.MeshCount == 1 ? "" : "es")}, {props} prop{(props == 1 ? "" : "s")}, {e.EntityCount} placed";
        }
    }
}

