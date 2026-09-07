using System;
using System.Collections.Generic;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Editor
{
    public partial class WorldStreamer
    {
        public bool HideBaseUnderProject
        {
            get => hideBaseUnderProject;
            set { if (value != hideBaseUnderProject) { hideBaseUnderProject = value; contentStamp++; } }
        }
        private bool hideBaseUnderProject = true;
        private int contentStamp, contentStampCount = -2;
        private Dictionary<uint, YmapFile> contentStampFor;

        public int ContentVersion
        {
            get
            {
                var over = ProjectOverrides;
                int n = over?.Count ?? -1;
                if (!ReferenceEquals(contentStampFor, over) || n != contentStampCount)
                { contentStampFor = over; contentStampCount = n; contentStamp++; }
                return ResidentVersion * 1024 + (contentStamp & 1023);
            }
        }

        public int BaseContentYmapsHidden;
        public int ProjectFootprintBoxes;
        public int GrassBatchesUnderProject;
        public int LodLightsUnderProject;

        public IEnumerable<YmapFile> ContentYmaps
        {
            get
            {
                var over = ProjectOverrides;
                if (over == null || !HideBaseUnderProject)
                {
                    BaseContentYmapsHidden = 0;
                    foreach (var n in nodes.Values) if (n.Ymap != null && n.Prepared) yield return n.Ymap;
                    yield break;
                }
                var taken = TakenContentNames(over);
                int hidden = 0;
                foreach (var n in nodes.Values)
                {
                    if (n.Ymap == null || !n.Prepared) continue;
                    if (taken.Contains(n.Hash)) { hidden++; continue; }
                    yield return n.Ymap;
                }
                BaseContentYmapsHidden = hidden;
                foreach (var kv in over) if (kv.Value != null && kv.Value.Loaded) yield return kv.Value;
            }
        }

        private HashSet<uint> TakenContentNames(Dictionary<uint, YmapFile> over)
        {
            if (takenCache != null && ReferenceEquals(takenFor, over) && takenNodeCount == nodes.Count &&
                takenOverCount == over.Count) return takenCache;
            var set = new HashSet<uint>();
            foreach (var kv in over) set.Add(kv.Key);
            foreach (var kv in over)
            {
                var y = kv.Value;
                var name = ShortName(y);
                if (string.IsNullOrEmpty(name)) continue;
                foreach (var n in nodes.Values)
                {
                    if (string.IsNullOrEmpty(n.Name) || set.Contains(n.Hash)) continue;
                    if (IsContentPartner(name, n.Name)) set.Add(n.Hash);
                }
            }
            takenCache = set; takenFor = over; takenNodeCount = nodes.Count; takenOverCount = over.Count;
            return set;
        }
        private HashSet<uint> takenCache;
        private Dictionary<uint, YmapFile> takenFor;
        private int takenNodeCount = -1, takenOverCount = -1;

        internal static string ShortName(YmapFile y)
        {
            var n = y?.RpfFileEntry?.Name ?? y?.Name;
            if (string.IsNullOrEmpty(n)) return null;
            int dot = n.LastIndexOf('.');
            return (dot > 0 ? n.Substring(0, dot) : n).ToLowerInvariant();
        }

        internal static bool IsContentPartner(string baseName, string other)
        {
            if (string.IsNullOrEmpty(baseName) || string.IsNullOrEmpty(other)) return false;
            other = other.ToLowerInvariant();
            int dot = other.LastIndexOf('.');
            if (dot > 0) other = other.Substring(0, dot);
            if (other == baseName) return true;
            if (other.Length > baseName.Length + 1 && other.StartsWith(baseName, StringComparison.Ordinal) && other[baseName.Length] == '_')
            {
                var tail = other.Substring(baseName.Length + 1);
                if (StartsWithPart(tail, "grass") || StartsWithPart(tail, "lodlights") ||
                    StartsWithPart(tail, "distantlights") || StartsWithPart(tail, "distlodlights") ||
                    StartsWithPart(tail, "strm") || StartsWithPart(tail, "long") || StartsWithPart(tail, "critical"))
                    return true;
            }
            string bt = LodTile(baseName), ot = LodTile(other);
            if (bt != null && bt == ot) return true;
            return false;
        }

        private static string LodTile(string name)
        {
            int i = name.IndexOf("lodlights_", StringComparison.Ordinal);
            if (i < 0) return null;
            var tile = name.Substring(i + "lodlights_".Length);
            return tile.Length > 0 ? tile : null;
        }

        private static bool StartsWithPart(string tail, string word)
        {
            if (!tail.StartsWith(word, StringComparison.Ordinal)) return false;
            if (tail.Length == word.Length) return true;
            char c = tail[word.Length];
            return c == '_' || (c >= '0' && c <= '9');
        }

        private struct Footprint { public float MinX, MinY, MaxX, MaxY, MinZ, MaxZ; }
        private readonly List<Footprint> footprints = new List<Footprint>();
        private Dictionary<uint, YmapFile> footprintsFor;
        private int footprintsVersion = -1;

        public const float FootprintBelow = 25.0f, FootprintAbove = 60.0f;
        public const float FootprintMaxSpan = 600.0f;

        private void SyncFootprints()
        {
            var over = ProjectOverrides;
            int v = ContentVersion;
            if (ReferenceEquals(footprintsFor, over) && footprintsVersion == v) return;
            footprintsFor = over; footprintsVersion = v;
            footprints.Clear();
            if (over != null)
                foreach (var kv in over)
                {
                    var y = kv.Value;
                    if (y == null || !y.Loaded) continue;
                    if (nodes.ContainsKey(kv.Key)) continue;
                    if (TryFootprint(y, out var f)) footprints.Add(f);
                }
            ProjectFootprintBoxes = footprints.Count;
        }

        private static bool TryFootprint(YmapFile y, out Footprint f)
        {
            f = default;
            var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
            bool any = false;
            foreach (var e in y.AllEntities ?? Array.Empty<YmapEntityDef>())
            {
                if (e == null) continue;
                float r = Math.Max(e.BSRadius, 0.5f);
                var p = e.Position;
                min = Vector3.Min(min, p - new Vector3(r));
                max = Vector3.Max(max, p + new Vector3(r));
                any = true;
            }
            if (!any) return false;
            if (max.X - min.X > FootprintMaxSpan || max.Y - min.Y > FootprintMaxSpan) return false;
            f = new Footprint
            {
                MinX = min.X, MinY = min.Y, MaxX = max.X, MaxY = max.Y,
                MinZ = min.Z - FootprintBelow, MaxZ = max.Z + FootprintAbove,
            };
            return true;
        }

        public bool UnderProject(Vector3 p)
        {
            if (!HideBaseUnderProject || ProjectOverrides == null) return false;
            SyncFootprints();
            for (int i = 0; i < footprints.Count; i++)
            {
                var f = footprints[i];
                if (p.X >= f.MinX && p.X <= f.MaxX && p.Y >= f.MinY && p.Y <= f.MaxY && p.Z >= f.MinZ && p.Z <= f.MaxZ)
                    return true;
            }
            return false;
        }

        public bool GrassBatchHidden_P3(YmapFile owner, Vector3 pos)
        {
            if (!HideBaseUnderProject || ProjectOverrides == null) return false;
            if (owner != null && ProjectOverrides.TryGetValue(owner.RpfFileEntry?.ShortNameHash ?? 0, out var p) && ReferenceEquals(p, owner)) return false;
            if (!UnderProject(pos)) return false;
            GrassBatchesUnderProject++;
            return true;
        }

        public bool LodLightHidden_P3(Vector3 pos)
        {
            if (!HideBaseUnderProject || ProjectOverrides == null) return false;
            if (!UnderProject(pos)) return false;
            LodLightsUnderProject++;
            return true;
        }

        public static void ContentPartnerTest_P3(Action<string, bool, string> check)
        {
            check("p3 partner: a tile's grass file belongs to the tile",
                  IsContentPartner("cs4_09", "cs4_09_grass_0.ymap"), "cs4_09 -> cs4_09_grass_0");
            check("p3 partner: the streaming and long-range halves belong to it too",
                  IsContentPartner("cs4_09", "cs4_09_strm_0") && IsContentPartner("cs4_09", "cs4_09_long_0") &&
                  IsContentPartner("lr_cs4_roads", "lr_cs4_roads_critical_2"), "strm / long / critical");
            check("p3 partner: a neighbouring tile is NOT a partner",
                  !IsContentPartner("cs4_09", "cs4_090") && !IsContentPartner("cs4_09", "cs4_09b_grass_0") &&
                  !IsContentPartner("cs4_09", "cs4_10_grass_0"), "cs4_090 / cs4_09b / cs4_10 all left alone");
            check("p3 partner: a tile does not swallow an unrelated suffix",
                  !IsContentPartner("cs4_09", "cs4_09_props"), "cs4_09_props stays");
            check("p3 partner: the lodlights tile pairs with its distant half and its DLC overlays",
                  IsContentPartner("lodlights_medium028", "distlodlights_medium028") &&
                  IsContentPartner("lodlights_medium028", "apa_lodlights_medium028") &&
                  IsContentPartner("lodlights_medium028", "hei_distlodlights_medium028.ymap"), "medium028 set");
            check("p3 partner: another lodlights tile is untouched",
                  !IsContentPartner("lodlights_medium028", "lodlights_medium029") &&
                  !IsContentPartner("lodlights_medium028", "distlodlights_large000"), "medium029 / large000 stay");
            check("p3 partner: the same name always matches",
                  IsContentPartner("cs4_occl_03", "cs4_occl_03.ymap"), "identity");
        }
    }
}

