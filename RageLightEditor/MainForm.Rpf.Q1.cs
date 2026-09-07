using System;
using System.IO;
using CodeWalker.GameFiles;
using ImGuiNET;
using RageLightEditor.Editor;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private bool RpfExplorerOnly_Q1 => panel != null && panel.ArchiveMode;

        private int rpfFlatFrames_Q1;
        private int rpfFlatYmapsAtEntry_Q1 = -1, rpfFlatArchesAtEntry_Q1;
        private int rpfFlatYmapsMax_Q1, rpfFlatArchesMax_Q1, rpfFlatBuiltWhileFlat_Q1;
        private bool rpfFlatWasIn_Q1;

        private Color4 RpfFlatClear_Q1()
        {
            var bg = new System.Numerics.Vector4(0.09f, 0.10f, 0.11f, 1f);
            try
            {
                var c = ImGui.GetStyle().Colors[(int)ImGuiCol.WindowBg];
                if (c.W > 0.01f) bg = c;
            }
            catch { }
            return new Color4(
                (float)Math.Pow(Math.Clamp(bg.X, 0f, 1f), 2.2),
                (float)Math.Pow(Math.Clamp(bg.Y, 0f, 1f), 2.2),
                (float)Math.Pow(Math.Clamp(bg.Z, 0f, 1f), 2.2), 1.0f);
        }

        private void ClearForRpfExplorer_Q1()
        {
            TrackRpfFlatCost_Q1();
            if (!RpfExplorerOnly_Q1 || deviceResources == null) return;
            deviceResources.BeginFrame(RpfFlatClear_Q1());
        }

        private void TrackRpfFlatCost_Q1()
        {
            bool inRpf = RpfExplorerOnly_Q1;
            if (inRpf && !rpfFlatWasIn_Q1)
            {
                rpfFlatYmapsAtEntry_Q1 = World.YmapsResident;
                rpfFlatArchesAtEntry_Q1 = worldRender.ArchetypesLoaded;
                rpfFlatYmapsMax_Q1 = World.YmapsResident;
                rpfFlatArchesMax_Q1 = worldRender.ArchetypesLoaded;
                rpfFlatBuiltWhileFlat_Q1 = 0;
                rpfFlatFrames_Q1 = 0;
            }
            rpfFlatWasIn_Q1 = inRpf;
            if (!inRpf) return;

            rpfFlatFrames_Q1++;
            rpfFlatYmapsMax_Q1 = Math.Max(rpfFlatYmapsMax_Q1, World.YmapsResident);
            rpfFlatArchesMax_Q1 = Math.Max(rpfFlatArchesMax_Q1, worldRender.ArchetypesLoaded);
            rpfFlatBuiltWhileFlat_Q1 += worldRender.BuiltThisFrame;

            if (screenshotPath != null && (rpfFlatFrames_Q1 == 1 || rpfFlatFrames_Q1 % 60 == 0))
                Console.WriteLine(RpfFlatLine_Q1());
        }

        private string RpfFlatLine_Q1() =>
            $"RPFFLAT workspace={panel?.Workspace} explorerOnly={RpfExplorerOnly_Q1} frames={rpfFlatFrames_Q1} " +
            $"ymapsResident={rpfFlatYmapsAtEntry_Q1}->{World.YmapsResident} (peak {rpfFlatYmapsMax_Q1}) " +
            $"archetypes={rpfFlatArchesAtEntry_Q1}->{worldRender.ArchetypesLoaded} (peak {rpfFlatArchesMax_Q1}) " +
            $"modelsBuilt={rpfFlatBuiltWhileFlat_Q1} worldMeshesDrawn={worldRender.MeshesDrawn} " +
            $"lightSceneProps={(lightScene?.Files.Count ?? 0)} mloProps={(mloScene?.Files.Count ?? 0)} " +
            $"sceneDrawn=0 sky=off grid=off gizmo=off";

        partial void OpenMetaFileInExplorer_Q1(string path, ref bool handled)
        {
            if (string.IsNullOrEmpty(path)) return;
            var ext = (Path.GetExtension(path) ?? "").ToLowerInvariant();
            if (!IsMetaExtension_Q1(ext)) return;
            handled = true;

            try
            {
                var name = Path.GetFileName(path);
                var data = File.ReadAllBytes(path);
                if (data.Length == 0)
                {
                    panel.RpfStatus = name + " is empty";
                    return;
                }

                var xml = LooksTextual_Q1(data)
                    ? new System.Text.UTF8Encoding(false).GetString(data).TrimStart('﻿')
                    : MetaXmlOfDiskFile_Q1(ext, name, data, out _);
                if (string.IsNullOrEmpty(xml))
                {
                    panel.RpfStatus = name + ": nothing here converts this file to text - extract it instead";
                    Console.WriteLine($"RPFMETA refused {path}: no converter");
                    return;
                }

                panel.ShowRpfText(name + (LooksTextual_Q1(data) ? "" : ".xml"), xml);
                panel.SetRpfViewSource_Q1(null, path);
                panel.RpfStatus = $"{name} shown as text ({xml.Length / 1024:N0} KB) - nothing was loaded";
                Console.WriteLine($"RPFMETA opened {path} as text/XML ({xml.Length} chars) - nothing loaded");
                Console.WriteLine(ModelViewIsolationLine_P1());
            }
            catch (Exception ex)
            {
                panel.RpfStatus = "could not open " + Path.GetFileName(path) + ": " + ex.Message;
                Console.WriteLine($"RPFMETA threw on {path}: {ex.Message}");
            }
        }

        public static bool IsMetaExtension_Q1(string extWithDot)
        {
            switch ((extWithDot ?? "").ToLowerInvariant())
            {
                case ".ytyp": case ".ymap": case ".ymt": return true;
                default: return false;
            }
        }

        private string MetaXmlOfDiskFile_Q1(string ext, string name, byte[] data, out string why)
        {
            why = null;
            try
            {
                switch (ext)
                {
                    case ".ytyp":
                    {
                        var f = new YtypFile();
                        f.Load(data);
                        if (f.RpfFileEntry != null) { f.RpfFileEntry.Name = name; f.Name = name; }
                        return MetaXml.GetXml(f, out _);
                    }
                    case ".ymap":
                    {
                        var f = new YmapFile();
                        f.Load(data);
                        if (f.RpfFileEntry != null) { f.RpfFileEntry.Name = name; f.Name = name; }
                        return MetaXml.GetXml(f, out _);
                    }
                    case ".ymt":
                    {
                        var f = new YmtFile();
                        f.Load(data);
                        if (f.RpfFileEntry != null) f.RpfFileEntry.Name = name;
                        return MetaXml.GetXml(f, out _);
                    }
                    default:
                        why = "no XML converter for " + ext;
                        return null;
                }
            }
            catch (Exception ex) { why = ex.Message; return null; }
        }

        private static bool LooksTextual_Q1(byte[] data)
        {
            int n = Math.Min(data.Length, 1024);
            for (int i = 0; i < n; i++) if (data[i] == 0) return false;
            return true;
        }

        private void ServiceRpfHandoff_Q1()
        {
            var p = panel;
            if (p == null) return;

            if (p.RequestRpfToLight_Q1 != null)
            {
                var src = p.RequestRpfToLight_Q1;
                p.RequestRpfToLight_Q1 = null;
                OpenMetaInWorkspace_Q1(src, LightPanel.Space.Light);
            }
            if (p.RequestRpfToMlo_Q1 != null)
            {
                var src = p.RequestRpfToMlo_Q1;
                p.RequestRpfToMlo_Q1 = null;
                OpenMetaInWorkspace_Q1(src, LightPanel.Space.Mlo);
            }
        }

        private void OpenMetaInWorkspace_Q1(LightPanel.RpfViewSource_Q1 src, LightPanel.Space space)
        {
            if (src == null) return;
            string tmp = src.DiskPath;
            try
            {
                Cursor = System.Windows.Forms.Cursors.WaitCursor;
                if (tmp == null && src.Entry != null)
                {
                    var data = ArchiveBrowser.ExtractForDisk(src.Entry);
                    if (data == null || data.Length == 0)
                    {
                        panel.RpfStatus = src.Name + " came out of the archive empty";
                        return;
                    }
                    var dir = Path.Combine(Path.GetTempPath(), "rle_archive");
                    Directory.CreateDirectory(dir);
                    tmp = Path.Combine(dir, src.Entry.Name);
                    File.WriteAllBytes(tmp, data);
                }
                if (tmp == null || !File.Exists(tmp)) { panel.RpfStatus = src.Name + " is gone"; return; }

                panel.SwitchWorkspace(space);
                LoadFile(tmp);

                string where = space == LightPanel.Space.Mlo ? "the MLO Creator" : "the Lights workspace";
                panel.RpfStatus = $"{src.Name} opened in {where}";
                panel.MloStatus = panel.RpfStatus;
                Console.WriteLine($"RPFHANDOFF {src.Name} -> {space} (button; browsing never does this)");
                Console.WriteLine(ModelViewIsolationLine_P1());
            }
            catch (Exception ex)
            {
                panel.RpfStatus = "could not open " + src.Name + ": " + ex.Message;
            }
            finally { Cursor = System.Windows.Forms.Cursors.Default; }
        }
    }
}

