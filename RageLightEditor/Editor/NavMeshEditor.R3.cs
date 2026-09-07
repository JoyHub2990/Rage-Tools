using System;
using System.Collections.Generic;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Editor
{
    public partial class NavMeshEditor
    {
        public sealed class NavCell
        {
            public string Name = "";
            public int FileX, FileY;
            public int GridX, GridY;
            public Vector3 Min, Max, Centre;
            public RpfFileEntry Entry;
            public string ArchivePath = "";
            public int PolyCount = -1;
            public string NameLower = "";

            public override string ToString() => Name;
        }

        public readonly List<NavCell> Cells = new List<NavCell>();

        public bool CellsReady;

        public string CellsStatus = "";

        public string CellFilter = "";

        public bool CellsNearestFirst = true;

        public bool CellsOpenOnly;

        public Vector3 CellCameraPos;

        public NavCell SelectedCell;

        public NavCell RequestOpenCell;
        public NavCell RequestGoToCell;
        public bool RequestRescanCells;

        public float CellDistance(NavCell c)
        {
            if (c == null) return float.MaxValue;
            float dx = Math.Max(Math.Max(c.Min.X - CellCameraPos.X, 0.0f), CellCameraPos.X - c.Max.X);
            float dy = Math.Max(Math.Max(c.Min.Y - CellCameraPos.Y, 0.0f), CellCameraPos.Y - c.Max.Y);
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        public bool CellIsOpen(NavCell c)
        {
            if (c == null) return false;
            foreach (var d in Docs)
                if (string.Equals(d.Name, c.Name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public NavDoc DocOfCell(NavCell c)
        {
            if (c == null) return null;
            foreach (var d in Docs)
                if (string.Equals(d.Name, c.Name, StringComparison.OrdinalIgnoreCase)) return d;
            return null;
        }

        private readonly List<NavCell> cellRows = new List<NavCell>();
        private int cellRowsTotal, cellRowsMax = -1, cellRowsCount = -1, cellRowsDocs = -1;
        private string cellRowsFilter = "\0";
        private bool cellRowsNear, cellRowsOpenOnly;
        private NavCell cellRowsFirst;
        private Vector3 cellRowsPos = new Vector3(float.MaxValue);

        public IEnumerable<NavCell> FilteredCells(int max, out int total)
        {
            bool fresh = cellRowsMax == max &&
                         cellRowsFilter == (CellFilter ?? "") &&
                         cellRowsNear == CellsNearestFirst &&
                         cellRowsOpenOnly == CellsOpenOnly &&
                         cellRowsCount == Cells.Count &&
                         cellRowsDocs == Docs.Count &&
                         ReferenceEquals(cellRowsFirst, Cells.Count > 0 ? Cells[0] : null) &&
                         (cellRowsPos - CellCameraPos).LengthSquared() < 100.0f;
            if (fresh) { total = cellRowsTotal; return cellRows; }

            cellRowsMax = max;
            cellRowsFilter = CellFilter ?? "";
            cellRowsNear = CellsNearestFirst;
            cellRowsOpenOnly = CellsOpenOnly;
            cellRowsCount = Cells.Count;
            cellRowsDocs = Docs.Count;
            cellRowsFirst = Cells.Count > 0 ? Cells[0] : null;
            cellRowsPos = CellCameraPos;

            var list = cellRows;
            list.Clear();
            total = 0;
            string q = (CellFilter ?? "").Trim().ToLowerInvariant();

            bool coord = false, world = false;
            float cx = 0, cy = 0;
            var parts = q.Split(',');
            if (parts.Length == 2 &&
                float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out cx) &&
                float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out cy))
            {
                coord = true;
                world = Math.Abs(cx) > 400.0f || Math.Abs(cy) > 400.0f;
            }

            foreach (var c in Cells)
            {
                if (CellsOpenOnly && !CellIsOpen(c)) continue;
                if (q.Length > 0)
                {
                    if (coord)
                    {
                        if (world)
                        {
                            if (cx < c.Min.X || cx > c.Max.X || cy < c.Min.Y || cy > c.Max.Y) continue;
                        }
                        else if (c.FileX != (int)cx || c.FileY != (int)cy) continue;
                    }
                    else if (c.NameLower.IndexOf(q, StringComparison.Ordinal) < 0) continue;
                }
                total++;
                list.Add(c);
            }

            if (CellsNearestFirst)
                list.Sort((a, b) => CellDistance(a).CompareTo(CellDistance(b)));

            if (list.Count > max) list.RemoveRange(max, list.Count - max);
            cellRowsTotal = total;
            return list;
        }
    }
}

