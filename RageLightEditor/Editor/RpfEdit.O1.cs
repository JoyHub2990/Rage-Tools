using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using CodeWalker.GameFiles;

namespace RageLightEditor.Editor
{
    public static partial class RpfEdit
    {
        public readonly struct Result
        {
            public readonly bool Ok;
            public readonly string Message;
            public Result(bool ok, string message) { Ok = ok; Message = message ?? ""; }
            public static Result Fail(string m) => new Result(false, m);
            public static Result Done(string m) => new Result(true, m);
        }

        private static readonly HashSet<string> backedUp =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static string LastBackup { get; private set; } = "";

        public static int BackupCount => backedUp.Count;

        public static void EnsureBackup(string physicalPath)
        {
            LastBackup = "";
            if (string.IsNullOrEmpty(physicalPath)) return;
            if (!backedUp.Add(physicalPath)) return;
            try
            {
                if (!File.Exists(physicalPath)) return;
                var bak = physicalPath + ".bak";
                if (File.Exists(bak)) return;
                File.Copy(physicalPath, bak);
                LastBackup = bak;
            }
            catch
            {
                LastBackup = "";
            }
        }

        public static void ResetBackups() { backedUp.Clear(); LastBackup = ""; }

        private static void BeginOp() => LastBackup = "";

        public readonly struct Target
        {
            public readonly RpfDirectoryEntry Dir;
            public readonly string FsFolder;
            public Target(RpfDirectoryEntry dir, string fsFolder) { Dir = dir; FsFolder = fsFolder; }
            public bool InArchive => Dir != null;
            public bool Valid => Dir != null || !string.IsNullOrEmpty(FsFolder);
            public string PhysicalArchive => Dir?.File?.GetPhysicalFilePath();
            public string Display => Dir?.Path ?? FsFolder ?? "";
        }

        public static Target TargetOf(RpfExplorer ex) =>
            new Target(ex?.CurrentRpfDir, ex?.CurrentFsFolder);

        public readonly struct Item
        {
            public readonly RpfEntry Entry;
            public readonly RpfFile Archive;
            public readonly string FsPath;
            public readonly bool IsFolder;
            public readonly string Name;

            public Item(RpfEntry entry, RpfFile archive, string fsPath, bool isFolder, string name)
            {
                Entry = entry; Archive = archive; FsPath = fsPath; IsFolder = isFolder;
                Name = name ?? "";
            }
            public bool Valid => Entry != null || !string.IsNullOrEmpty(FsPath);
            public string PhysicalArchive => Entry?.File?.GetPhysicalFilePath();
            public string Display => Entry?.Path ?? FsPath ?? Name;
        }

        public static Item ItemOf(in RpfExplorer.Row r)
        {
            if (r.IsFs)
            {
                var arch = r.EnterDir?.File;
                return new Item(null, arch, r.Path, r.IsFolder && arch == null, r.Name);
            }
            var inner = r.Entry is RpfFileEntry fe ? RpfExplorer.NestedArchive(fe) : null;
            return new Item(r.Entry, inner, null, r.Entry is RpfDirectoryEntry, r.Name);
        }

        public static bool IsFilenameOk(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            foreach (var c in Path.GetInvalidFileNameChars())
                if (name.IndexOf(c) >= 0) return false;
            return true;
        }

        public static bool NeedsEncryptionChange(Target t) =>
            t.InArchive && !RpfFile.IsValidEncryption(t.Dir.File);

        public static string EncryptionOf(Target t) =>
            t.InArchive ? t.Dir.File.Encryption.ToString() : "";

        public static bool MakeEncryptionValid(Target t)
        {
            if (!t.InArchive) return true;
            if (RpfFile.IsValidEncryption(t.Dir.File)) return true;
            EnsureBackup(t.PhysicalArchive);
            return RpfFile.EnsureValidEncryption(t.Dir.File, f => true);
        }

        public static Result NewFolder(bool editMode, RpfExplorer ex, Target t, string name)
        {
            BeginOp();
            if (!editMode) return Result.Fail("edit mode is off - nothing was written");
            if (!t.Valid) return Result.Fail("no folder to create this in");
            if (!IsFilenameOk(name)) return Result.Fail("\"" + name + "\" is not a usable folder name");

            try
            {
                if (t.InArchive)
                {
                    EnsureBackup(t.PhysicalArchive);
                    RpfFile.CreateDirectory(t.Dir, name);
                }
                else
                {
                    var full = Path.Combine(t.FsFolder, name);
                    if (Directory.Exists(full)) return Result.Fail(name + " already exists here");
                    Directory.CreateDirectory(full);
                }
                ex?.RefreshCurrent_O1();
                return Result.Done("created folder " + name + " in " + t.Display + BackupNote());
            }
            catch (Exception e) { return Result.Fail("could not create " + name + ": " + e.Message); }
        }

        public static Result NewArchive(bool editMode, RpfExplorer ex, Target t, string name)
        {
            BeginOp();
            if (!editMode) return Result.Fail("edit mode is off - nothing was written");
            if (!t.Valid) return Result.Fail("no folder to create this in");
            if (!IsFilenameOk(name)) return Result.Fail("\"" + name + "\" is not a usable archive name");
            if (!name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase)) name += ".rpf";

            try
            {
                RpfFile made;
                if (t.InArchive)
                {
                    EnsureBackup(t.PhysicalArchive);
                    made = RpfFile.CreateNew(t.Dir, name, RpfEncryption.OPEN);
                }
                else
                {
                    var full = Path.Combine(t.FsFolder, name);
                    if (File.Exists(full)) return Result.Fail(name + " already exists here");
                    made = RpfFile.CreateNew(t.FsFolder, full, RpfEncryption.OPEN);
                    ex?.RememberArchive_O1(made);
                }
                ex?.RefreshCurrent_O1();
                return Result.Done("created archive " + name + " (OPEN encryption) in " + t.Display +
                                   BackupNote());
            }
            catch (Exception e) { return Result.Fail("could not create " + name + ": " + e.Message); }
        }

        public static Result ImportFiles(bool editMode, RpfExplorer ex, Target t,
                                         IReadOnlyList<string> paths, bool raw)
        {
            BeginOp();
            if (!editMode) return Result.Fail("edit mode is off - nothing was written");
            if (!t.Valid) return Result.Fail("no folder to import into");
            if (paths == null || paths.Count == 0) return Result.Fail("no files chosen");

            int done = 0, failed = 0, converted = 0;
            long bytes = 0;
            string firstError = null;
            XmlImportBegin_S3();
            if (t.InArchive) EnsureBackup(t.PhysicalArchive);

            foreach (var p in paths)
            {
                try
                {
                    if (!File.Exists(p)) { failed++; continue; }
                    var fi = new FileInfo(p);
                    if (fi.Length > 0x3FFFFFFF)
                    {
                        failed++;
                        firstError = firstError ?? (fi.Name + " is over 1 GB");
                        continue;
                    }

                    var name = fi.Name;
                    byte[] data = ReadForImport_S3(p, raw, ref name, ref converted, out var xmlWhy);
                    if (data == null)
                    {
                        failed++;
                        firstError = firstError ?? (fi.Name + ": " + (xmlWhy ?? "could not be read"));
                        continue;
                    }

                    if (t.InArchive) RpfFile.CreateFile(t.Dir, name, data);
                    else File.WriteAllBytes(Path.Combine(t.FsFolder, name), data);

                    done++;
                    bytes += data.Length;
                }
                catch (Exception e)
                {
                    failed++;
                    firstError = firstError ?? e.Message;
                }
            }

            ex?.RefreshCurrent_O1();
            if (done == 0) return Result.Fail("nothing imported" + (firstError != null ? ": " + firstError : ""));
            return Result.Done($"imported {done:N0} file{(done == 1 ? "" : "s")} ({bytes / 1024:N0} KB) into " +
                               t.Display +
                               XmlImportNote_S3() +
                               (failed > 0 ? $" - {failed:N0} failed" + (firstError != null ? " (" + firstError + ")" : "") : "") +
                               BackupNote());
        }

        private static bool TryConvertXml(string path, ref string name, out byte[] data)
        {
            data = null;
            var lower = name.ToLowerInvariant();
            if (!lower.EndsWith(".xml")) return false;
            if (lower.IndexOf('.') == lower.LastIndexOf('.')) return false;

            var mformat = XmlMeta.GetXMLFormat(lower, out int trim);
            if (mformat == MetaFormat.XML) return false;

            var doc = new XmlDocument();
            var text = File.ReadAllText(path);
            if (string.IsNullOrEmpty(text)) return false;
            doc.LoadXml(text);

            var inpath = path.Substring(0, path.Length - trim);
            inpath = Path.Combine(Path.GetDirectoryName(inpath) ?? "", Path.GetFileNameWithoutExtension(inpath));

            data = XmlMeta.GetData(doc, mformat, inpath);
            if (data == null) return false;
            name = name.Substring(0, name.Length - trim);
            return true;
        }

        public readonly struct Clip
        {
            public readonly RpfFileEntry Entry;
            public readonly string FsPath;
            public readonly string Name;
            public Clip(RpfFileEntry e, string fs, string name) { Entry = e; FsPath = fs; Name = name ?? ""; }
            public bool Valid => Entry != null || !string.IsNullOrEmpty(FsPath);
        }

        public static readonly List<Clip> Clipboard = new List<Clip>();

        public static Result Copy(IReadOnlyList<RpfExplorer.Row> rows)
        {
            Clipboard.Clear();
            if (rows == null) return Result.Fail("nothing to copy");
            foreach (var r in rows)
            {
                if (r.IsFs)
                {
                    if (r.IsFolder && r.EnterDir == null) continue;
                    Clipboard.Add(new Clip(null, r.Path, r.Name));
                }
                else if (r.Entry is RpfFileEntry fe) Clipboard.Add(new Clip(fe, null, r.Name));
            }
            if (Clipboard.Count == 0) return Result.Fail("nothing copyable was selected");
            return Result.Done($"copied {Clipboard.Count:N0} item{(Clipboard.Count == 1 ? "" : "s")}");
        }

        public static Result Paste(bool editMode, RpfExplorer ex, Target t, IReadOnlyList<string> osFiles)
        {
            BeginOp();
            if (!editMode) return Result.Fail("edit mode is off - nothing was written");
            if (!t.Valid) return Result.Fail("no folder to paste into");

            if (osFiles != null && osFiles.Count > 0)
                return ImportFiles(true, ex, t, osFiles, false);

            if (Clipboard.Count == 0) return Result.Fail("nothing has been copied");

            int done = 0, failed = 0;
            long bytes = 0;
            string firstError = null;
            if (t.InArchive) EnsureBackup(t.PhysicalArchive);

            foreach (var c in Clipboard)
            {
                try
                {
                    byte[] data = c.Entry != null
                        ? ArchiveBrowser.ExtractForDisk(c.Entry)
                        : File.ReadAllBytes(c.FsPath);
                    if (data == null || data.Length == 0) { failed++; continue; }

                    if (c.Entry != null && t.InArchive && ReferenceEquals(c.Entry.Parent, t.Dir)) continue;
                    if (c.FsPath != null && !t.InArchive &&
                        string.Equals(Path.GetDirectoryName(c.FsPath), t.FsFolder, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var name = RpfExplorer.SafeName(c.Name);
                    if (t.InArchive) RpfFile.CreateFile(t.Dir, name, data);
                    else File.WriteAllBytes(Path.Combine(t.FsFolder, name), data);
                    done++;
                    bytes += data.Length;
                }
                catch (Exception e) { failed++; firstError = firstError ?? e.Message; }
            }

            ex?.RefreshCurrent_O1();
            if (done == 0) return Result.Fail("nothing pasted" + (firstError != null ? ": " + firstError : ""));
            return Result.Done($"pasted {done:N0} file{(done == 1 ? "" : "s")} ({bytes / 1024:N0} KB) into " +
                               t.Display + (failed > 0 ? $" - {failed:N0} failed" : "") + BackupNote());
        }

        public static Result Rename(bool editMode, RpfExplorer ex, Item item, string newName)
        {
            BeginOp();
            if (!editMode) return Result.Fail("edit mode is off - nothing was written");
            if (!item.Valid) return Result.Fail("nothing is selected");
            if (!IsFilenameOk(newName)) return Result.Fail("\"" + newName + "\" is not a usable name");
            if (string.Equals(item.Name, newName, StringComparison.Ordinal)) return Result.Fail("that is the same name");

            try
            {
                if (item.Entry != null)
                {
                    EnsureBackup(item.PhysicalArchive);
                    if (item.Archive != null) RpfFile.RenameArchive(item.Archive, newName);
                    RpfFile.RenameEntry(item.Entry, newName);
                }
                else
                {
                    var dir = Path.GetDirectoryName(item.FsPath);
                    var to = Path.Combine(dir ?? "", newName);
                    if (string.Equals(item.FsPath, to, StringComparison.OrdinalIgnoreCase))
                        return Result.Fail("that is the same name");
                    if (item.Archive != null)
                    {
                        File.Move(item.FsPath, to);
                        ex?.ForgetArchive_O1(item.FsPath);
                        RpfFile.RenameArchive(item.Archive, newName);
                        ex?.RememberArchive_O1(item.Archive);
                    }
                    else if (item.IsFolder) Directory.Move(item.FsPath, to);
                    else File.Move(item.FsPath, to);
                }
                ex?.RefreshCurrent_O1();
                return Result.Done("renamed " + item.Name + " to " + newName + BackupNote());
            }
            catch (Exception e) { return Result.Fail("could not rename " + item.Name + ": " + e.Message); }
        }

        public static bool DeleteNeedsConfirm(Item item, out string what)
        {
            what = "";
            if (item.Archive != null) { what = "archive " + item.Name; return true; }
            if (item.Entry is RpfDirectoryEntry d)
            {
                int n = (d.Directories?.Count ?? 0) + (d.Files?.Count ?? 0);
                if (n > 0) { what = "folder " + item.Name + " and its " + n + " item" + (n == 1 ? "" : "s"); return true; }
            }
            if (item.IsFolder && item.FsPath != null)
            {
                try
                {
                    int n = Directory.GetFileSystemEntries(item.FsPath).Length;
                    if (n > 0) { what = "folder " + item.Name + " and its " + n + " item" + (n == 1 ? "" : "s"); return true; }
                }
                catch { }
            }
            return false;
        }

        public static Result Delete(bool editMode, RpfExplorer ex, Item item)
        {
            BeginOp();
            if (!editMode) return Result.Fail("edit mode is off - nothing was written");
            if (!item.Valid) return Result.Fail("nothing is selected");

            try
            {
                if (item.Entry != null)
                {
                    EnsureBackup(item.PhysicalArchive);
                    RpfFile.DeleteEntry(item.Entry);
                }
                else if (item.Archive != null)
                {
                    ex?.ForgetArchive_O1(item.FsPath);
                    File.Delete(item.FsPath);
                }
                else if (item.IsFolder) Directory.Delete(item.FsPath, true);
                else File.Delete(item.FsPath);

                ex?.RefreshCurrent_O1();
                return Result.Done("deleted " + item.Name + BackupNote());
            }
            catch (Exception e) { return Result.Fail("could not delete " + item.Name + ": " + e.Message); }
        }

        public static Result Defragment(bool editMode, RpfFile rpf, bool recursive)
        {
            BeginOp();
            if (!editMode) return Result.Fail("edit mode is off - nothing was written");
            if (rpf == null) return Result.Fail("that is not an archive");
            if (!RpfFile.IsValidEncryption(rpf, recursive))
                return Result.Fail(rpf.Name + " is " + rpf.Encryption + "-encrypted - convert it to OPEN first");

            try
            {
                var before = rpf.FileSize;
                EnsureBackup(rpf.GetPhysicalFilePath());
                RpfFile.Defragment(rpf, null, recursive);
                var after = rpf.FileSize;
                var saved = before - after;
                return Result.Done($"defragmented {rpf.Name}: {RpfExplorer.SizeText(before)} -> " +
                                   $"{RpfExplorer.SizeText(after)}" +
                                   (saved > 0 ? $" ({RpfExplorer.SizeText(saved)} freed)" : " (nothing to reclaim)") +
                                   BackupNote());
            }
            catch (Exception e) { return Result.Fail("could not defragment " + rpf.Name + ": " + e.Message); }
        }

        public static long DefragmentedSize(RpfFile rpf, bool recursive)
        {
            try { return rpf?.GetDefragmentedFileSize(recursive) ?? 0; }
            catch { return 0; }
        }

        private static string BackupNote() =>
            string.IsNullOrEmpty(LastBackup) ? "" : "  (kept a copy at " + Path.GetFileName(LastBackup) + ")";
    }
}

