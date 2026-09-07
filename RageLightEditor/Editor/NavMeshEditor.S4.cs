using System;
using System.Collections.Generic;
using SharpDX;

namespace RageLightEditor.Editor
{
    public partial class NavMeshEditor
    {
        public bool FollowCamera_S4 = true;

        public int StreamCellCap_S4 = 64;

        public readonly Dictionary<NavDoc, Vector2> StreamedDocs_S4 = new Dictionary<NavDoc, Vector2>();

        public int StreamedCount_S4
        {
            get { int n = 0; foreach (var d in Docs) if (StreamedDocs_S4.ContainsKey(d)) n++; return n; }
        }

        public string StreamStatus_S4 = "";

        public NavDoc AddStreamed_S4(CodeWalker.GameFiles.YnvFile ynv, string source, Vector2 cellCentre)
        {
            if (ynv == null) return null;
            var keepActive = Active;
            var keepPolys = SelectedPolys.ToArray();
            int keepVertex = SelectedVertex;
            var keepPoint = SelectedPoint;
            var keepPortal = SelectedPortal;

            var doc = Add(ynv, null, source);
            if (doc == null) return null;
            StreamedDocs_S4[doc] = cellCentre;

            if (keepActive != null) Active = keepActive;
            SelectedPolys.Clear();
            SelectedPolys.AddRange(keepPolys);
            SelectedVertex = keepVertex;
            SelectedPoint = keepPoint;
            SelectedPortal = keepPortal;
            return doc;
        }

        public bool CloseStreamed_S4(NavDoc doc)
        {
            if (doc == null || !StreamedDocs_S4.ContainsKey(doc)) return false;
            if (doc.Dirty || ReferenceEquals(doc, Active)) return false;
            foreach (var p in SelectedPolys) if (ReferenceEquals(p?.Ynv, doc.Ynv)) return false;
            if (ReferenceEquals(SelectedPoint?.Ynv, doc.Ynv) || ReferenceEquals(SelectedPortal?.Ynv, doc.Ynv)) return false;
            StreamedDocs_S4.Remove(doc);
            Docs.Remove(doc);
            return true;
        }

        public void PruneStreamed_S4()
        {
            if (StreamedDocs_S4.Count == 0) return;
            List<NavDoc> gone = null;
            foreach (var kv in StreamedDocs_S4)
                if (!Docs.Contains(kv.Key)) (gone ??= new List<NavDoc>()).Add(kv.Key);
            if (gone == null) return;
            foreach (var d in gone) StreamedDocs_S4.Remove(d);
        }
    }
}

