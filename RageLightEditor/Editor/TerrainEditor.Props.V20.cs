using System;
using System.Collections.Generic;
using System.Linq;
using SharpDX;

namespace RageLightEditor.Editor
{
    public partial class TerrainEditor
    {
        public class Prop_V20
        {
            public string Name = "";
            public string SourcePath = "";
            public bool Visible = true;
            public bool Dirty;
            public string LastExport = "";
        }

        public readonly List<Prop_V20> Props_V20 = new List<Prop_V20>();

        public int ActiveProp_V20;

        public Prop_V20 ActivePropOrNull_V20 =>
            ActiveProp_V20 >= 0 && ActiveProp_V20 < Props_V20.Count ? Props_V20[ActiveProp_V20] : null;

        public IEnumerable<Part> PartsOf_V20(int prop) =>
            Parts.Where(p => p != null && p.Group_V20 == prop);

        public IEnumerable<Part> VisibleParts_V20() =>
            Parts.Where(p => p != null && IsVisible_V20(p.Group_V20));

        public bool IsVisible_V20(int prop) =>
            prop < 0 || prop >= Props_V20.Count || Props_V20[prop].Visible;

        public int BeginImport_V20(string name, string sourcePath, bool replace)
        {
            if (replace)
            {
                Parts.Clear();
                Props_V20.Clear();
            }
            Props_V20.Add(new Prop_V20
            {
                Name = string.IsNullOrWhiteSpace(name) ? ("prop " + (Props_V20.Count + 1)) : name,
                SourcePath = sourcePath ?? "",
                Visible = true,
            });
            ActiveProp_V20 = Props_V20.Count - 1;
            return ActiveProp_V20;
        }

        public bool RemoveProp_V20(int prop)
        {
            if (prop < 0 || prop >= Props_V20.Count) return false;
            for (int i = Parts.Count - 1; i >= 0; i--)
            {
                if (Parts[i].Group_V20 == prop) { Parts[i].Mesh?.Dispose(); Parts.RemoveAt(i); }
                else if (Parts[i].Group_V20 > prop) Parts[i].Group_V20--;
            }
            Props_V20.RemoveAt(prop);
            if (ActiveProp_V20 >= Props_V20.Count) ActiveProp_V20 = Props_V20.Count - 1;
            if (ActiveProp_V20 < 0) ActiveProp_V20 = 0;
            RecomputeBounds_V20();
            Dirty = true;
            return true;
        }

        public void RecomputeBounds_V20()
        {
            if (Parts.Count == 0) { Bounds = new BoundingBox(Vector3.Zero, Vector3.Zero); return; }
            var mn = new Vector3(float.MaxValue);
            var mx = new Vector3(float.MinValue);
            foreach (var p in Parts)
            {
                if (p?.Verts == null) continue;
                foreach (var v in p.Verts)
                {
                    mn = Vector3.Min(mn, v.Position);
                    mx = Vector3.Max(mx, v.Position);
                }
            }
            if (mn.X > mx.X) { mn = Vector3.Zero; mx = Vector3.Zero; }
            Bounds = new BoundingBox(mn, mx);
        }

        public void TouchProp_V20(int prop)
        {
            Dirty = true;
            if (prop >= 0 && prop < Props_V20.Count) Props_V20[prop].Dirty = true;
        }

        public int DirtyPropCount_V20 => Props_V20.Count(p => p != null && p.Dirty);

        public void EnsureGroups_V20(string fallbackName)
        {
            if (Parts.Count == 0) { Props_V20.Clear(); ActiveProp_V20 = 0; return; }
            if (Props_V20.Count == 0)
            {
                Props_V20.Add(new Prop_V20
                {
                    Name = string.IsNullOrWhiteSpace(fallbackName) ? "terrain" : fallbackName,
                    SourcePath = SourcePath ?? "",
                });
                foreach (var p in Parts) p.Group_V20 = 0;
                ActiveProp_V20 = 0;
                return;
            }
            for (int i = 0; i < Parts.Count; i++)
                if (Parts[i].Group_V20 < 0 || Parts[i].Group_V20 >= Props_V20.Count)
                    Parts[i].Group_V20 = Props_V20.Count - 1;
            if (ActiveProp_V20 < 0 || ActiveProp_V20 >= Props_V20.Count) ActiveProp_V20 = 0;
        }

        public bool RequestImportAdd_V20;
        public int RequestExportProp_V20 = -1;
        public bool RequestExportAll_V20;
        public int RequestRemoveProp_V20 = -1;
        public bool RequestRebuildVisible_V20;
    }
}

