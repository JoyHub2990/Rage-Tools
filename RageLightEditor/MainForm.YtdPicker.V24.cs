using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private void InstallYtdPicker_V24()
        {
            ModelView.PickerList_V24 = ListPickerNode_V24;
            ModelView.PickerUp_V24 = PickerUp_V24;
            ModelView.PickerLabel_V24 = n => n is RpfDirectoryEntry d ? (d.Path ?? d.Name ?? "") : (n as string ?? "");
        }

        private RpfDirectoryEntry ArchiveRootOf_V24(RpfFileEntry rpfEntry)
        {
            var man = gameFiles?.Cache?.RpfMan;
            if (man?.RpfDict == null || rpfEntry == null) return null;
            if (man.RpfDict.TryGetValue(rpfEntry.Path, out var rpf) && rpf?.Root != null) return rpf.Root;
            var children = rpfEntry.File?.Children;
            if (children != null)
                foreach (var ch in children)
                    if (ch?.ParentFileEntry == rpfEntry && ch.Root != null) return ch.Root;
            return null;
        }

        private List<ModelViewer.PickItem> ListPickerNode_V24(object node)
        {
            var items = new List<ModelViewer.PickItem>();
            if (node is RpfDirectoryEntry d)
            {
                foreach (var sub in d.Directories ?? new List<RpfDirectoryEntry>())
                    if (sub != null) items.Add(new ModelViewer.PickItem { Name = sub.Name, IsDir = true, Node = sub });
                foreach (var f in d.Files ?? new List<RpfFileEntry>())
                {
                    var n = f?.NameLower ?? f?.Name?.ToLowerInvariant();
                    if (n == null) continue;
                    if (n.EndsWith(".rpf"))
                    {
                        var root = ArchiveRootOf_V24(f);
                        if (root != null) items.Add(new ModelViewer.PickItem { Name = f.Name, IsDir = true, Node = root });
                    }
                    else if (n.EndsWith(".ytd"))
                        items.Add(new ModelViewer.PickItem { Name = f.Name, Entry = f });
                }
            }
            else if (node is string dir && Directory.Exists(dir))
            {
                try
                {
                    foreach (var sub in Directory.GetDirectories(dir))
                        items.Add(new ModelViewer.PickItem { Name = Path.GetFileName(sub), IsDir = true, Node = sub });
                    var man = gameFiles?.Cache?.RpfMan;
                    var root = gameFiles?.Folder?.TrimEnd('\\', '/');
                    foreach (var f in Directory.GetFiles(dir))
                    {
                        var ext = Path.GetExtension(f).ToLowerInvariant();
                        if (ext == ".rpf" && man?.RpfDict != null && root != null && f.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        {
                            var rel = f.Substring(root.Length).TrimStart('\\', '/');
                            if (man.RpfDict.TryGetValue(rel, out var rpf) && rpf?.Root != null)
                                items.Add(new ModelViewer.PickItem { Name = Path.GetFileName(f), IsDir = true, Node = rpf.Root });
                        }
                        else if (ext == ".ytd")
                            items.Add(new ModelViewer.PickItem { Name = Path.GetFileName(f), DiskPath = f });
                    }
                }
                catch { }
            }
            return items.OrderBy(i => i.IsDir ? 0 : 1).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private object PickerUp_V24(object node)
        {
            if (node is RpfDirectoryEntry d)
            {
                if (d.Parent != null) return d.Parent;
                var rpf = d.File;
                if (rpf?.ParentFileEntry?.Parent != null) return rpf.ParentFileEntry.Parent;
                var root = gameFiles?.Folder?.TrimEnd('\\', '/');
                if (root != null && rpf?.Path != null)
                {
                    var onDisk = Path.Combine(root, rpf.Path);
                    var dir = Path.GetDirectoryName(onDisk);
                    if (dir != null && Directory.Exists(dir)) return dir;
                }
                return null;
            }
            if (node is string s)
            {
                var parent = Path.GetDirectoryName(s.TrimEnd('\\', '/'));
                return parent != null && Directory.Exists(parent) ? parent : null;
            }
            return null;
        }

        private bool pickShotDone_V24;

        private void ServiceYtdPicker_V24()
        {
            var mv = ModelView;
            var pv = modelViewPreview;
            if (mv == null) return;
            if (!pickShotDone_V24 && pv?.Model != null && Environment.GetEnvironmentVariable("RLE_MVYTDPICK") == "1")
            {
                pickShotDone_V24 = true;
                mv.OpenYtdPicker_V24();
                screenshotFrames = Math.Max(screenshotFrames, 10);
            }
            if (mv.RequestYtdEntry_V24 != null)
            {
                var e = mv.RequestYtdEntry_V24;
                mv.RequestYtdEntry_V24 = null;
                if (pv?.Model != null)
                {
                    try
                    {
                        var ytd = gameFiles.Cache.RpfMan.GetFile<YtdFile>(e);
                        if (ytd?.TextureDict == null) throw new Exception("the file would not read");
                        AttachYtdToPreview_V22(ytd, "gta:" + e.Path);
                    }
                    catch (Exception ex) { mv.Status = $"Could not load {e.Name}: {ex.Message}"; }
                }
            }
            if (!string.IsNullOrEmpty(mv.RequestYtdDisk_V24))
            {
                var path = mv.RequestYtdDisk_V24;
                mv.RequestYtdDisk_V24 = null;
                if (pv?.Model != null)
                {
                    try
                    {
                        var ytd = new YtdFile();
                        ytd.Load(File.ReadAllBytes(path));
                        AttachYtdToPreview_V22(ytd, path);
                    }
                    catch (Exception ex) { mv.Status = $"Could not read {Path.GetFileName(path)}: {ex.Message}"; }
                }
            }
        }

        private void SeqTest_YtdPicker_V24(Action<string, bool, string> check)
        {
            var c = gameFiles?.Cache;
            if (c?.YdrDict == null || !gameFiles.Ready) { Console.WriteLine("  v24 picker: (skipped - no game folder)"); return; }
            if (!c.YdrDict.TryGetValue(JenkHash.GenHash("w_ar_carbinerifle"), out var fe) || fe?.Parent == null)
            { check("v24 picker: the carbine and its folder", false, "not found"); return; }

            var items = ListPickerNode_V24(fe.Parent);
            var own = items.FirstOrDefault(i => !i.IsDir && string.Equals(i.Name, "w_ar_carbinerifle.ytd", StringComparison.OrdinalIgnoreCase));
            check("v24 picker: the model's folder lists its own .ytd", own?.Entry != null,
                  $"{items.Count(i => !i.IsDir)} .ytd file(s), {items.Count(i => i.IsDir)} folder(s) in {fe.Parent.Path}");
            check("v24 picker: ...and no file that is not a .ytd", items.All(i => i.IsDir || i.Name.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase)), "only .ytd");

            var up = PickerUp_V24(fe.Parent);
            var upLabel = up is RpfDirectoryEntry ud ? ud.Path : up as string;
            check("v24 picker: Up from an archive's top goes to the folder holding it", up != null && !string.IsNullOrEmpty(upLabel),
                  upLabel ?? "(nowhere)");
            object n = fe.Parent; int steps = 0; string last = null;
            while (n != null && steps++ < 12) { last = n is RpfDirectoryEntry dd ? dd.Path : n as string; n = PickerUp_V24(n); }
            check("v24 picker: ...all the way up to a disk folder", last != null && Directory.Exists(last), last ?? "(none)");

            var pv = new AssetPreview(gameFiles, modelRenderer, textureLoader);
            try
            {
                pv.Open(fe);
                var ytd = c.RpfMan.GetFile<YtdFile>(own.Entry);
                pv.AttachYtd(ytd, "gta:" + own.Entry.Path);
                var mats = pv.InspectMaterials();
                var diffuse = mats.SelectMany(m => m.Textures).FirstOrDefault(t => t.SlotName.IndexOf("Diffuse", StringComparison.OrdinalIgnoreCase) >= 0);
                check("v24 picker: a dictionary attached from the archives shows its textures as found in the Textures tab",
                      diffuse != null && diffuse.Resolved && diffuse.Source == MatTexSource.LoadedYtd,
                      diffuse == null ? "no diffuse slot" : $"{diffuse.TextureName}: {(diffuse.Resolved ? "from " + diffuse.Source : "unresolved")}");
            }
            finally { pv.Dispose(); }
        }

        private string YtdDialogStart_V24()
        {
            var p = ModelView?.DiskPath;
            if (!string.IsNullOrEmpty(p)) { var d = Path.GetDirectoryName(p); if (Directory.Exists(d)) return d; }
            return gameFiles?.Folder;
        }
    }
}

