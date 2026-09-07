using System;
using System.Diagnostics;
using System.Threading;
using System.Collections.Generic;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public static class LibScan
    {
        public static int FindShader(string gtaFolder, string needle, int limit)
        {
            var game = new GameFileManager();
            Console.WriteLine($"Loading game files from {gtaFolder}...");
            game.BeginInit(gtaFolder);
            while (game.Initialising) Thread.Sleep(500);
            if (!game.Ready) { Console.WriteLine("game files not ready: " + game.Error); return 1; }

            var rpfman = game.Cache?.RpfMan;
            if (rpfman?.EntryDict == null) return 1;

            var candidates = new List<RpfFileEntry>();
            foreach (var kv in rpfman.EntryDict)
            {
                if (kv.Value is RpfFileEntry fe && fe.Name != null &&
                    fe.Name.EndsWith(".ydr", StringComparison.OrdinalIgnoreCase))
                    candidates.Add(fe);
            }
            Console.WriteLine($"scanning {candidates.Count} .ydr for shader '{needle}'...");

            int found = 0, scanned = 0;
            var sw = Stopwatch.StartNew();
            foreach (var fe in candidates)
            {
                if (found >= limit) break;
                scanned++;
                try
                {
                    var ydr = rpfman.GetFile<YdrFile>(fe);
                    var sg = ydr?.Drawable?.ShaderGroup?.Shaders?.data_items;
                    if (sg == null) continue;
                    foreach (var sh in sg)
                    {
                        var name = sh?.Name.ToString() ?? "";
                        if (name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        Console.WriteLine($"  {fe.Name}  shader='{name}' sps='{sh.FileName}'");
                        var prms = sh.ParametersList?.Parameters;
                        var hashes = sh.ParametersList?.Hashes;
                        if (prms == null || hashes == null) continue;
                        for (int i = 0; i < Math.Min(prms.Length, hashes.Length); i++)
                        {
                            var pn = (ShaderParamNames)(uint)hashes[i];
                            var d = prms[i].Data;
                            string val = d is TextureBase tb ? $"tex '{tb.Name}'"
                                       : d is SharpDX.Vector4 v ? $"({v.X:0.###},{v.Y:0.###},{v.Z:0.###},{v.W:0.###})"
                                       : d is SharpDX.Vector4[] va ? $"array[{va.Length}]"
                                       : d?.GetType().Name ?? "null";
                            if (pn.ToString().IndexOf("etail", StringComparison.Ordinal) >= 0)
                                Console.WriteLine($"      {pn} = {val}");
                        }
                        found++;
                        break;
                    }
                }
                catch { }
            }
            Console.WriteLine($"FINDSHADER: {found} match(es) in {scanned} scanned, {sw.Elapsed.TotalSeconds:0.0}s");
            return 0;
        }

        public static int Run(string gtaFolder, string[] localFolders, int limit)
        {
            try
            {
                var game = new GameFileManager();
                if (!string.IsNullOrEmpty(gtaFolder))
                {
                    Console.WriteLine($"Loading game files from {gtaFolder}...");
                    var sw0 = Stopwatch.StartNew();
                    game.BeginInit(gtaFolder);
                    string last = "";
                    while (game.Initialising)
                    {
                        if (game.Status != last) { last = game.Status; Console.WriteLine("  " + last); }
                        Thread.Sleep(500);
                    }
                    Console.WriteLine($"  game files ready={game.Ready} in {sw0.Elapsed.TotalSeconds:0.0}s" +
                        (string.IsNullOrEmpty(game.Error) ? "" : $" error={game.Error}"));
                    if (!game.Ready) return 1;
                }

                var lib = new LightPropLibrary { ArchiveLimit = limit };
                var sw = Stopwatch.StartNew();
                lib.BeginScan(game, localFolders, includeArchives: game.Ready);
                int lastScanned = -1;
                while (lib.Scanning)
                {
                    if (lib.Scanned != lastScanned)
                    {
                        lastScanned = lib.Scanned;
                        Console.WriteLine($"  {lib.Scanned}/{lib.Total} scanned, {lib.Count} with lights " +
                            $"({sw.Elapsed.TotalSeconds:0.0}s)");
                    }
                    Thread.Sleep(2000);
                }

                double secs = sw.Elapsed.TotalSeconds;
                Console.WriteLine($"LIBSCAN done: {lib.Count} light props from {lib.Scanned} models in {secs:0.0}s");
                if (lib.Scanned > 0)
                {
                    double per = secs / lib.Scanned;
                    Console.WriteLine($"  {per * 1000.0:0.00} ms/model");
                    if (limit > 0) Console.WriteLine($"  (limited to {limit}; a full pass would be extrapolated from this rate)");
                }
                Console.WriteLine("  " + lib.Status);
                int shown = 0;
                foreach (var e in lib.Search(""))
                {
                    Console.WriteLine($"    {e.Name}  [{e.Types}]  {(e.FromArchive ? "game" : "local")}");
                    if (++shown >= 40) { Console.WriteLine("    ..."); break; }
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("LIBSCAN FAILED: " + ex);
                return 1;
            }
        }
    }
}

