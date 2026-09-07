using System;
using System.Collections.Generic;
using CodeWalker.GameFiles;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private bool archFindDone_V64;

        partial void OnWorldTick_ArchFind_V64()
        {
            if (archFindDone_V64) return;
            var want = Environment.GetEnvironmentVariable("RLE_ARCHFIND");
            if (string.IsNullOrWhiteSpace(want)) return;
            var c = gameFiles?.Cache;
            if (c == null || !gameFiles.Ready) return;
            archFindDone_V64 = true;

            var names = new List<(string Name, uint Hash)>();
            foreach (var raw in want.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var nm = raw.Trim().ToLowerInvariant();
                JenkIndex.Ensure(nm);
                names.Add((nm, JenkHash.GenHash(nm)));
            }
            foreach (var (nm, h) in names)
                Console.WriteLine($"ARCHFIND target {nm} = {h:X8}");

            int ytyps = 0, defs = 0;
            if (c.YtypDict != null)
            {
                foreach (var ytyp in c.YtypDict.Values)
                {
                    if (ytyp?.AllArchetypes == null) continue;
                    ytyps++;
                    var fe = ytyp.RpfFileEntry;
                    foreach (var a in ytyp.AllArchetypes)
                    {
                        foreach (var (nm, hh) in names)
                        {
                            if (a.Hash != hh) continue;
                            defs++;
                            Console.WriteLine($"ARCHFIND def {nm} in {fe.Path}  type {a.GetType().Name}  drawable {a.DrawableDict} bbmin {a.BBMin} bbmax {a.BBMax} lodDist {a.LodDist}");
                        }
                    }
                }
            }
            Console.WriteLine($"ARCHFIND scanned {ytyps} ytyps, {defs} definitions");

            int ymaps = 0, hits = 0;
            if (c.YmapDict != null)
            {
                foreach (var fe in c.YmapDict.Values)
                {
                    YmapFile ymap = null;
                    try { ymap = c.RpfMan.GetFile<YmapFile>(fe); } catch { }
                    if (ymap == null) continue;
                    ymaps++;
                    var ents = ymap.AllEntities;
                    if (ents == null) continue;
                    for (int i = 0; i < ents.Length; i++)
                    {
                        var d = ents[i]._CEntityDef;
                        foreach (var (nm, hh) in names)
                        {
                            if (d.archetypeName.Hash != hh) continue;
                            hits++;
                            Console.WriteLine($"ARCHFIND ent {nm} in {fe.Path}  ent {i}  pos {d.position.X:0.##},{d.position.Y:0.##},{d.position.Z:0.##}  lodLevel {d.lodLevel}  lodDist {d.lodDist}  childLodDist {d.childLodDist}  flags {d.flags:X}  parent {d.parentIndex}  children {d.numChildren}  ymapFlags {ymap.CMapData.flags:X} content {ymap.CMapData.contentFlags:X} ymapParent {ymap.CMapData.parent}");
                        }
                    }
                }
            }
            Console.WriteLine($"ARCHFIND scanned {ymaps} ymaps, {hits} placements");
        }
    }
}
