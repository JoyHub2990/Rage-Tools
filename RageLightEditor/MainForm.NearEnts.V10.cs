using System;
using System.Collections.Generic;
using System.Linq;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private bool nearEntsDone_V10;

        private void ServiceNearEnts_V10()
        {
            if (nearEntsDone_V10) return;
            var s = Environment.GetEnvironmentVariable("RLE_NEARENTS");
            if (string.IsNullOrWhiteSpace(s)) { nearEntsDone_V10 = true; return; }
            if (worldMloDumpTick < 430) return;
            nearEntsDone_V10 = true;

            var p = s.Split(',');
            float N(int i, float d) => i < p.Length && float.TryParse(p[i],
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : d;
            var at = new Vector3(N(0, camera?.Position.X ?? 0), N(1, camera?.Position.Y ?? 0), camera?.Position.Z ?? 0);
            float radius = N(2, 200.0f);
            Console.WriteLine(NearEntityReport_V10(at, radius));
        }

        public string NearEntityReport_V10(Vector3 at, float radius)
        {
            var sb = new System.Text.StringBuilder();
            var visSet = new HashSet<YmapEntityDef>(World.Visible);
            int total = 0, visible = 0, built = 0;

            var why = new Dictionary<string, (int n, List<(float size, string name)> names)>();
            void Note(string reason, string name, float size = 0f)
            {
                if (!why.TryGetValue(reason, out var e)) e = (0, new List<(float, string)>());
                e.n++;
                e.names.Add((size, name));
                e.names.Sort((a, b) => b.size.CompareTo(a.size));
                if (e.names.Count > 6) e.names.RemoveRange(6, e.names.Count - 6);
                why[reason] = e;
            }

            foreach (var y in World.ResidentYmaps)
            {
                var ents = y.AllEntities;
                if (ents == null) continue;
                foreach (var e in ents)
                {
                    if (e == null) continue;
                    var d2 = new Vector2(e.Position.X - at.X, e.Position.Y - at.Y).Length();
                    if (d2 > radius) continue;
                    total++;
                    var name = e.Archetype?.Name ?? e._CEntityDef.archetypeName.ToString();
                    bool vis = visSet.Contains(e);
                    if (vis) visible++;
                    bool isBuilt = e.Archetype != null && worldRender.PeekModel(e.Archetype.Hash) != null;
                    if (isBuilt) built++;
                    if (vis && isBuilt) continue;

                    if (e.Archetype == null) { Note("no archetype (not in any ytyp)", name); continue; }
                    if (worldRender.IsFailed(e.Archetype.Hash)) { Note("drawable FAILED to load", name); continue; }
                    if (World.IsVariantHiddenEnt(e)) { Note("hidden by a ymap variant", name); continue; }
                    if (!vis)
                    {
                        float dist = (e.Position - at).Length();
                        bool childDrawn = e.LodManagerChildren != null && e.LodManagerChildren.Any(c => c != null && visSet.Contains(c));
                        if (childDrawn) { Note("a LOD whose children are drawn (correct)", name); continue; }
                        if (e.LodDist > 0 && dist > e.LodDist * World.LodScale)
                        { Note($"beyond its lodDist (d > lodDist x {World.LodScale:0.##})", $"{name} d{dist:0}>{e.LodDist:0}"); continue; }
                        Note("NOT VISIBLE and nothing stands in for it",
                             $"{name} r{e.BSRadius:0} d{dist:0} lod{e.LodDist:0} {e._CEntityDef.lodLevel} parent={(e.Parent == null ? "none" : (e.Parent.Archetype?.Name ?? "?") + (visSet.Contains(e.Parent) ? ":drawn" : ":NOT drawn"))}",
                             e.BSRadius);
                        continue;
                    }
                    Note("visible but not built yet (still loading)", name);
                }
            }

            sb.AppendLine($"NEARENTS at {at.X:0},{at.Y:0} r {radius:0} m: {total} resident entit(ies), {visible} visible, {built} built");
            foreach (var kv in why.OrderByDescending(k => k.Value.n))
                sb.AppendLine($"  {kv.Value.n,5}  {kv.Key}  e.g. {string.Join(" | ", kv.Value.names.Select(t => t.name))}");
            if (why.Count == 0) sb.AppendLine("  everything resident here is visible and built");
            return sb.ToString().TrimEnd();
        }
    }
}

