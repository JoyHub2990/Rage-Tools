using System;
using System.Collections.Generic;
using System.Linq;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private static string NavPolyFingerprint_V15(YnvPoly p)
        {
            var v = p?.Vertices;
            if (v == null || v.Length == 0) return "-";
            var c = Vector3.Zero;
            foreach (var x in v) c += x;
            c /= v.Length;
            return $"{v.Length}@{c.X:0.00},{c.Y:0.00},{c.Z:0.00}";
        }

        private RpfFileEntry NavPickRealYnv_V15()
        {
            var rpfs = gameFiles?.Cache?.AllRpfs;
            if (rpfs == null) return null;
            RpfFileEntry best = null;
            long bestSize = 0;
            int seen = 0;
            foreach (var r in rpfs)
            {
                foreach (var e in r?.AllEntries ?? new List<RpfEntry>())
                {
                    if (!(e is RpfFileEntry fe) || !fe.NameLower.EndsWith(".ynv")) continue;
                    if (!fe.NameLower.StartsWith("navmesh")) continue;
                    if (fe.GetFileSize() > bestSize) { bestSize = fe.GetFileSize(); best = fe; }
                    if (++seen >= 400) return best;
                }
            }
            return best;
        }

        private YnvFile NavLoadRealYnv_V15(RpfFileEntry entry)
        {
            var data = entry?.File?.ExtractFile(entry);
            if (data == null) return null;
            return RpfFile.GetFile<YnvFile>(entry, data);
        }

        private void NavSeamRealFileTest_V15(Action<string, bool, string> check)
        {
            if (gameFiles?.Cache?.AllRpfs == null)
            {
                Console.WriteLine("  v15: (real-file round trip skipped - the archives are not open)");
                return;
            }

            RpfFileEntry entry;
            YnvFile ynv;
            try
            {
                entry = NavPickRealYnv_V15();
                ynv = NavLoadRealYnv_V15(entry);
            }
            catch (Exception ex)
            {
                Console.WriteLine("  v15: (real-file round trip skipped - " + ex.Message + ")");
                return;
            }
            if (ynv?.Polys == null || ynv.Polys.Count < 20)
            {
                Console.WriteLine("  v15: (real-file round trip skipped - no navmesh with enough polys found)");
                return;
            }

            string name = entry.Name;
            int count = ynv.Polys.Count;

            var before = new string[count];
            for (int i = 0; i < count; i++) before[i] = NavPolyFingerprint_V15(ynv.Polys[i]);

            var ed = new Editor.NavMeshEditor();
            var doc = ed.Add(ynv, null, "v15 real");
            doc.PolyCountOnLoad_V15 = count;

            var victims = new List<YnvPoly>();
            for (int i = count / 4; i < count && victims.Count < 5; i += Math.Max(1, count / 20))
                victims.Add(ynv.Polys[i]);
            var victimIdx = victims.Select(v => ynv.Polys.IndexOf(v)).ToList();

            ed.DisablePolys_V15(null, doc, victims);
            Editor.NavMeshEditor.Reindex(ynv);
            Editor.NavMeshEditor.SyncVertexLists(ynv);
            ynv.UpdateContentFlags(false);
            byte[] saved = ynv.Save();

            var back = new YnvFile();
            back.Load(saved);
            if (back.Polys == null) { check("v15 (real file): the saved navmesh parses back", false, "no polys"); return; }

            check($"v15 (real file, {name}): it saved and parsed back",
                  back.Polys.Count > 0, $"{saved.Length:N0} bytes, {back.Polys.Count:N0} polys");
            check("v15 (real file): disabling changed the poly COUNT by nothing",
                  back.Polys.Count == count, $"{count:N0} -> {back.Polys.Count:N0}");

            int moved = 0, firstMoved = -1;
            for (int i = 0; i < Math.Min(count, back.Polys.Count); i++)
                if (NavPolyFingerprint_V15(back.Polys[i]) != before[i])
                { moved++; if (firstMoved < 0) firstMoved = i; }
            check("v15 (real file): ...and EVERY polygon came back at the index it went in at",
                  moved == 0, moved == 0 ? $"all {count:N0} indices identical" : $"{moved:N0} moved, first at {firstMoved}");

            int stillDisabled = 0;
            foreach (int idx in victimIdx)
            {
                if (idx < 0 || idx >= back.Polys.Count) continue;
                var rp = back.Polys[idx];
                bool severed = (rp.Edges ?? Array.Empty<YnvEdge>())
                    .All(e => e == null || e.PolyID1 == Editor.NavMeshEditor.NoPoly_V15);
                if (Editor.NavMeshEditor.IsDisabled_V15(rp) && severed) stillDisabled++;
            }
            check("v15 (real file): ...and they are still out of the graph after the round trip",
                  stillDisabled == victims.Count, $"{stillDisabled}/{victims.Count} still severed and marked");

            var victimSet = new HashSet<uint>(victimIdx.Select(i => (uint)i));
            int inbound = 0;
            for (int i = 0; i < back.Polys.Count; i++)
            {
                if (victimSet.Contains((uint)i)) continue;
                foreach (var e in back.Polys[i].Edges ?? Array.Empty<YnvEdge>())
                {
                    if (e == null) continue;
                    if (victimSet.Contains(e.PolyID1) || victimSet.Contains(e.PolyID2)) inbound++;
                }
            }
            check("v15 (real file): ...and no other polygon still routes into them",
                  inbound == 0, inbound == 0 ? "no inbound edges" : $"{inbound} edge(s) still name a disabled poly");

            ed.Touch(doc, structural: true);
            int stillOut = 0;
            foreach (int idx in victimIdx)
                if ((ynv.Polys[idx].Edges ?? Array.Empty<YnvEdge>())
                        .All(e => e == null || e.PolyID1 == Editor.NavMeshEditor.NoPoly_V15)) stillOut++;
            check("v15 (real file): ...and a LATER structural edit does not link them back in",
                  stillOut == victims.Count, $"{stillOut}/{victims.Count} still severed after a rebuild");

            var reopened = new Editor.NavMeshEditor();
            var docR = reopened.Add(back, null, "v15 reopen");
            check("v15 (real file): ...and re-opening the saved file recognises them again",
                  docR.Disabled_V15.Count >= victims.Count,
                  $"{docR.Disabled_V15.Count} poly(s) seen as disabled on open, of {victims.Count} disabled");

            var ynv2 = NavLoadRealYnv_V15(entry);
            if (ynv2?.Polys == null) return;
            var ed2 = new Editor.NavMeshEditor();
            var doc2 = ed2.Add(ynv2, null, "v15 real delete");
            var kill = new List<YnvPoly>();
            for (int i = count / 4; i < ynv2.Polys.Count && kill.Count < 5; i += Math.Max(1, count / 20))
                kill.Add(ynv2.Polys[i]);
            ed2.DeletePolys(null, doc2, kill);
            Editor.NavMeshEditor.Reindex(ynv2);
            Editor.NavMeshEditor.SyncVertexLists(ynv2);
            ynv2.UpdateContentFlags(false);
            var back2 = new YnvFile();
            back2.Load(ynv2.Save());

            int changed = 0;
            for (int i = 0; i < Math.Min(count, back2.Polys?.Count ?? 0); i++)
                if (NavPolyFingerprint_V15(back2.Polys[i]) != before[i]) changed++;
            check("v15 (real file): a hard delete gives a DIFFERENT polygon to thousands of indices",
                  changed > 0,
                  $"{changed:N0} of {count:N0} indices name another polygon now (first delete at {victimIdx[0]})");

            int outward = 0;
            uint areaId = (uint)ynv2.AreaID;
            foreach (var p2 in ynv2.Polys)
                foreach (var e in p2?.Edges ?? Array.Empty<YnvEdge>())
                {
                    if (e == null) continue;
                    if (e.PolyID1 != Editor.NavMeshEditor.NoPoly_V15 && e.AreaID1 != areaId) outward++;
                    if (e.PolyID2 != Editor.NavMeshEditor.NoPoly_V15 && e.AreaID2 != areaId) outward++;
                }
            Console.WriteLine($"  v15 (real file) {name}: {count:N0} polys in area {areaId}, {outward:N0} edge slot(s) pointing OUT of this cell; " +
                              $"deleting {kill.Count} polys gave {changed:N0} indices a different polygon - every reference to one of those from a NEIGHBOURING .ynv now names the wrong one.");
        }
    }
}

