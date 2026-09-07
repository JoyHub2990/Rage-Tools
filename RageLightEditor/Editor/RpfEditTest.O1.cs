using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CodeWalker.GameFiles;

namespace RageLightEditor.Editor
{
    public static partial class RpfEditTest
    {
        public static int Run()
        {
            int fails = 0;
            void Check(string name, bool ok, string detail)
            {
                Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}" +
                                  (string.IsNullOrEmpty(detail) ? "" : "   [" + detail + "]"));
            }

            var root = Path.Combine(Path.GetTempPath(), "rle_rpfedit_test");
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
            Directory.CreateDirectory(root);
            Console.WriteLine("RPFEDITTEST folder: " + root);
            RpfEdit.ResetBackups();

            try
            {
                fails += RunArchiveChecks(root, Check);
                fails += RunXmlImportChecks_S3(root, Check);
                fails += RunFileSystemChecks(root, Check);
                fails += RunTreeChecks(root, Check);
                fails += RunGuardChecks(root, Check);
            }
            catch (Exception ex)
            {
                Console.WriteLine("RPFEDITTEST threw: " + ex);
                fails++;
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }

            Console.WriteLine(fails == 0 ? "RPFEDITTEST PASSED" : $"RPFEDITTEST FAILED ({fails})");
            return fails == 0 ? 0 : 1;
        }

        private static int RunArchiveChecks(string root, Action<string, bool, string> check)
        {
            var fails = new Counter(check);

            var arcPath = Path.Combine(root, "wso1_test.rpf");
            var rpf = RpfFile.CreateNew(root, arcPath, RpfEncryption.OPEN);
            fails.Check("new archive exists on disk", File.Exists(arcPath),
                        new FileInfo(arcPath).Length + " bytes");
            fails.Check("new archive is OPEN", rpf.Encryption == RpfEncryption.OPEN, rpf.Encryption.ToString());

            var ex = new RpfExplorer();
            var target = new RpfEdit.Target(rpf.Root, null);

            var res = RpfEdit.NewFolder(true, ex, target, "data");
            fails.Check("create folder", res.Ok && rpf.Root.Directories.Any(d => d.NameLower == "data"), res.Message);

            fails.Check("first write kept a .bak", File.Exists(arcPath + ".bak"), arcPath + ".bak");

            var dataDir = rpf.Root.Directories.First(d => d.NameLower == "data");
            var dataTarget = new RpfEdit.Target(dataDir, null);

            var srcText = Path.Combine(root, "hello.txt");
            var textBody = "the quick brown fox jumps over the lazy dog\r\n";
            File.WriteAllText(srcText, textBody);
            res = RpfEdit.ImportFiles(true, ex, dataTarget, new[] { srcText }, true);
            fails.Check("import a file", res.Ok && dataDir.Files.Any(f => f.NameLower == "hello.txt"), res.Message);

            var payload = Encoding.ASCII.GetBytes(new string('R', 4096));
            var resBytes = MakeResource(payload, 165);
            var srcRes = Path.Combine(root, "thing.ydr");
            File.WriteAllBytes(srcRes, resBytes);
            res = RpfEdit.ImportFiles(true, ex, dataTarget, new[] { srcRes }, true);
            var resEntry = dataDir.Files.FirstOrDefault(f => f.NameLower == "thing.ydr");
            fails.Check("import a resource", res.Ok && resEntry is RpfResourceFileEntry,
                        resEntry?.GetType().Name ?? "missing");

            var backOut = ArchiveBrowser.ExtractForDisk(resEntry);
            bool magicOk = backOut != null && backOut.Length > 16 &&
                           BitConverter.ToUInt32(backOut, 0) == 0x37435352;
            byte[] roundTrip = null;
            try { roundTrip = ResourceBuilder.Decompress(backOut.Skip(16).ToArray()); } catch { }
            fails.Check("resource keeps its RSC7 header", magicOk, magicOk ? "RSC7" : "no magic");
            fails.Check("resource payload survives the round trip",
                        roundTrip != null && roundTrip.Length == payload.Length &&
                        roundTrip.SequenceEqual(payload),
                        (roundTrip?.Length ?? -1) + " of " + payload.Length + " bytes");

            RpfEdit.NewFolder(true, ex, target, "copies");
            var copiesDir = rpf.Root.Directories.First(d => d.NameLower == "copies");
            RpfEdit.Clipboard.Clear();
            RpfEdit.Clipboard.Add(new RpfEdit.Clip(resEntry, null, "thing.ydr"));
            res = RpfEdit.Paste(true, ex, new RpfEdit.Target(copiesDir, null), null);
            var pasted = copiesDir.Files.FirstOrDefault(f => f.NameLower == "thing.ydr");
            fails.Check("paste a resource into another folder",
                        res.Ok && pasted is RpfResourceFileEntry, res.Message);

            var item = new RpfEdit.Item(dataDir.Files.First(f => f.NameLower == "hello.txt"), null, null, false, "hello.txt");
            res = RpfEdit.Rename(true, ex, item, "greeting.txt");
            fails.Check("rename a file",
                        res.Ok && dataDir.Files.Any(f => f.NameLower == "greeting.txt") &&
                        !dataDir.Files.Any(f => f.NameLower == "hello.txt"), res.Message);

            res = RpfEdit.NewArchive(true, ex, dataTarget, "inner");
            var innerEntry = dataDir.Files.FirstOrDefault(f => f.NameLower == "inner.rpf");
            var inner = innerEntry != null ? rpf.FindChildArchive(innerEntry) : null;
            fails.Check("create a nested archive", res.Ok && inner?.Root != null, res.Message);
            if (inner?.Root != null)
            {
                res = RpfEdit.ImportFiles(true, ex, new RpfEdit.Target(inner.Root, null), new[] { srcText }, true);
                fails.Check("import into the nested archive",
                            res.Ok && inner.Root.Files.Any(f => f.NameLower == "hello.txt"), res.Message);
            }

            var delItem = new RpfEdit.Item(copiesDir, null, null, true, "copies");
            res = RpfEdit.Delete(true, ex, delItem);
            fails.Check("delete a folder and its contents",
                        res.Ok && !rpf.Root.Directories.Any(d => d.NameLower == "copies"), res.Message);

            var reopened = new RpfFile(arcPath, "wso1_test.rpf");
            reopened.ScanStructure(null, null);
            fails.Check("re-opened archive scans clean",
                        reopened.LastException == null && reopened.Root != null,
                        reopened.LastException?.Message ?? "ok");

            var rdata = reopened.Root?.Directories?.FirstOrDefault(d => d.NameLower == "data");
            fails.Check("re-opened: the folder is there", rdata != null,
                        string.Join(",", reopened.Root?.Directories?.Select(d => d.Name) ?? Enumerable.Empty<string>()));
            fails.Check("re-opened: the renamed file is there",
                        rdata?.Files?.Any(f => f.NameLower == "greeting.txt") == true,
                        string.Join(",", rdata?.Files?.Select(f => f.Name) ?? Enumerable.Empty<string>()));
            fails.Check("re-opened: the deleted folder is gone",
                        reopened.Root?.Directories?.Any(d => d.NameLower == "copies") != true, "");

            var readBack = rdata?.Files?.FirstOrDefault(f => f.NameLower == "greeting.txt");
            var body = readBack != null ? Encoding.ASCII.GetString(reopened.ExtractFile(readBack) ?? Array.Empty<byte>()) : "";
            fails.Check("re-opened: the file's contents survived", body == textBody,
                        body.Length + " chars");

            var rres = rdata?.Files?.FirstOrDefault(f => f.NameLower == "thing.ydr") as RpfResourceFileEntry;
            fails.Check("re-opened: the resource is still a resource", rres != null,
                        rres != null ? "v" + rres.Version : "missing");
            var rpayload = rres != null ? reopened.ExtractFile(rres) : null;
            fails.Check("re-opened: the resource's payload is intact",
                        rpayload != null && rpayload.Length >= payload.Length &&
                        rpayload.Take(payload.Length).SequenceEqual(payload),
                        (rpayload?.Length ?? -1) + " bytes");

            var rinner = rdata?.Files?.FirstOrDefault(f => f.NameLower == "inner.rpf");
            var rinnerArc = rinner != null ? reopened.FindChildArchive(rinner) : null;
            fails.Check("re-opened: the nested archive still holds its file",
                        rinnerArc?.Root?.Files?.Any(f => f.NameLower == "hello.txt") == true,
                        rinnerArc == null ? "no child archive" : "ok");

            return fails.Count;
        }

        private static int RunFileSystemChecks(string root, Action<string, bool, string> check)
        {
            var fails = new Counter(check);
            var ex = new RpfExplorer();

            var work = Path.Combine(root, "loose");
            Directory.CreateDirectory(work);
            var t = new RpfEdit.Target(null, work);

            var res = RpfEdit.NewFolder(true, ex, t, "sub");
            fails.Check("fs: create a folder", res.Ok && Directory.Exists(Path.Combine(work, "sub")), res.Message);

            res = RpfEdit.NewFolder(true, ex, t, "sub");
            fails.Check("fs: a folder that exists is refused", !res.Ok, res.Message);

            var src = Path.Combine(root, "drop.txt");
            File.WriteAllText(src, "dropped");
            res = RpfEdit.ImportFiles(true, ex, t, new[] { src }, true);
            fails.Check("fs: import a file", res.Ok && File.Exists(Path.Combine(work, "drop.txt")), res.Message);

            var item = new RpfEdit.Item(null, null, Path.Combine(work, "drop.txt"), false, "drop.txt");
            res = RpfEdit.Rename(true, ex, item, "kept.txt");
            fails.Check("fs: rename a file",
                        res.Ok && File.Exists(Path.Combine(work, "kept.txt")) &&
                        !File.Exists(Path.Combine(work, "drop.txt")), res.Message);

            res = RpfEdit.NewArchive(true, ex, t, "made");
            var madePath = Path.Combine(work, "made.rpf");
            fails.Check("fs: create an archive", res.Ok && File.Exists(madePath), res.Message);

            item = new RpfEdit.Item(null, null, Path.Combine(work, "kept.txt"), false, "kept.txt");
            res = RpfEdit.Delete(true, ex, item);
            fails.Check("fs: delete a file", res.Ok && !File.Exists(Path.Combine(work, "kept.txt")), res.Message);

            item = new RpfEdit.Item(null, null, Path.Combine(work, "sub"), true, "sub");
            res = RpfEdit.Delete(true, ex, item);
            fails.Check("fs: delete a folder", res.Ok && !Directory.Exists(Path.Combine(work, "sub")), res.Message);

            return fails.Count;
        }

        private static int RunTreeChecks(string root, Action<string, bool, string> check)
        {
            var fails = new Counter(check);

            var ex = new RpfExplorer();
            ex.BuildFromGameFolder(root, null);
            fails.Check("tree: the root is the folder", ex.Ready && ex.GameRoot != null &&
                        ex.GameRoot.IsFs, ex.GameRoot?.Label ?? "none");

            bool went = ex.GoToPath("wso1_test.rpf\\data");
            ex.EnsureList();
            fails.Check("tree: walk into an archive on disk", went && ex.Current?.Dir != null,
                        ex.CurrentDisplayPath);
            var names = ex.Rows.Select(r => r.Name.ToLowerInvariant()).ToList();
            fails.Check("tree: the archive's folder lists its files",
                        names.Contains("greeting.txt") && names.Contains("thing.ydr"),
                        string.Join(",", names));

            var resRow = ex.Rows.FirstOrDefault(r => r.Name.ToLowerInvariant() == "thing.ydr");
            fails.Check("tree: a resource's Attributes column", resRow.Attr.StartsWith("Resource [V."),
                        resRow.Attr);

            ex.GoToPath("");
            went = ex.GoToPath(root);
            ex.EnsureList();
            var topNames = ex.Rows.Select(r => r.Name.ToLowerInvariant()).ToList();
            fails.Check("tree: the install root lists loose files and archives",
                        went && topNames.Contains("wso1_test.rpf") && topNames.Contains("hello.txt"),
                        string.Join(",", topNames));
            var arcRow = ex.Rows.FirstOrDefault(r => r.Name.ToLowerInvariant() == "wso1_test.rpf");
            fails.Check("tree: an archive on disk is a folder with an encryption attribute",
                        arcRow.IsFolder && arcRow.Attr == "OPEN encryption", arcRow.Attr);

            fails.Check("tree: inside the install is detected", ex.CurrentIsInGameFolder,
                        ex.CurrentPhysicalPath ?? "");
            fails.Check("tree: mods\\ is not flagged as base game files", !ex.CurrentIsInModsFolder, "");

            var other = Path.Combine(root, "loose");
            ex.AddRootFolder(other);
            fails.Check("tree: an opened folder becomes a root",
                        ex.Roots.Any(r => string.Equals(r.FsPath, other, StringComparison.OrdinalIgnoreCase)),
                        string.Join(",", ex.Roots.Select(r => r.Label)));

            return fails.Count;
        }

        private static int RunGuardChecks(string root, Action<string, bool, string> check)
        {
            var fails = new Counter(check);
            var ex = new RpfExplorer();
            var work = Path.Combine(root, "guards");
            Directory.CreateDirectory(work);
            var t = new RpfEdit.Target(null, work);

            var r1 = RpfEdit.NewFolder(false, ex, t, "nope");
            var r2 = RpfEdit.ImportFiles(false, ex, t, new[] { Path.Combine(root, "hello.txt") }, true);
            var r3 = RpfEdit.Delete(false, ex, new RpfEdit.Item(null, null, work, true, "guards"));
            fails.Check("edit mode off refuses every write",
                        !r1.Ok && !r2.Ok && !r3.Ok && Directory.Exists(work) &&
                        !Directory.Exists(Path.Combine(work, "nope")), r1.Message);

            var bad = RpfEdit.NewFolder(true, ex, t, "a/b");
            fails.Check("an invalid name is refused", !bad.Ok && !Directory.Exists(Path.Combine(work, "a")),
                        bad.Message);
            fails.Check("name validation matches the file system",
                        !RpfEdit.IsFilenameOk("") && !RpfEdit.IsFilenameOk("a:b") &&
                        !RpfEdit.IsFilenameOk("a*b") && RpfEdit.IsFilenameOk("dlc.rpf"), "");

            var nowhere = RpfEdit.NewFolder(true, ex, new RpfEdit.Target(null, null), "x");
            fails.Check("no target is refused, not thrown", !nowhere.Ok, nowhere.Message);

            var lone = new RpfEdit.Item(null, null, Path.Combine(root, "hello.txt"), false, "hello.txt");
            fails.Check("a single file needs no confirmation", !RpfEdit.DeleteNeedsConfirm(lone, out _), "");
            var full = new RpfEdit.Item(null, null, root, true, "root");
            fails.Check("a folder with contents needs confirmation",
                        RpfEdit.DeleteNeedsConfirm(full, out var what), what);

            return fails.Count;
        }

        private static byte[] MakeResource(byte[] payload, uint version)
        {
            var comp = ResourceBuilder.Compress(payload);
            var data = new byte[comp.Length + 16];
            Buffer.BlockCopy(BitConverter.GetBytes(0x37435352u), 0, data, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(version), 0, data, 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(0x90000000u), 0, data, 8, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(0u), 0, data, 12, 4);
            Buffer.BlockCopy(comp, 0, data, 16, comp.Length);
            return data;
        }

        private sealed class Counter
        {
            private readonly Action<string, bool, string> report;
            public int Count;
            public Counter(Action<string, bool, string> report) { this.report = report; }
            public void Check(string name, bool ok, string detail)
            {
                report(name, ok, detail);
                if (!ok) Count++;
            }
        }
    }
}

