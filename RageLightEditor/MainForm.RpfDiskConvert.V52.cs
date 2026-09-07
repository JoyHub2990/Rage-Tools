using System;
using System.Collections.Generic;
using System.IO;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        public static bool IsConvertibleXml_V52(string name)
        {
            var lower = (name ?? "").ToLowerInvariant();
            if (!lower.EndsWith(".xml")) return false;
            if (lower.IndexOf('.') == lower.LastIndexOf('.')) return false;
            return XmlMeta.GetXMLFormat(lower, out _) != MetaFormat.XML;
        }

        public static string ConvertTargetName_V52(string name) =>
            name != null && name.Length > 4 ? name.Substring(0, name.Length - 4) : name;

        public static string ConvertDiskXml_V52(string path, out string error)
        {
            error = null;
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) { error = "that file is gone"; return null; }
                string name = Path.GetFileName(path);
                var data = XmlConvertCli_V49.Convert(path, ref name, out error);
                if (data == null) return null;
                var outPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)) ?? "", name);
                File.WriteAllBytes(outPath, data);
                try { if (new FileInfo(outPath).Length > 0) File.Delete(path); } catch { }
                return outPath;
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        private void DoRpfDiskConvert_V52(string path)
        {
            var outPath = ConvertDiskXml_V52(path, out var error);
            if (outPath == null)
            {
                panel.RpfStatus = "Could not convert " + Path.GetFileName(path) + ": " + (error ?? "unknown");
                return;
            }
            panel.RpfStatus = $"Converted to {Path.GetFileName(outPath)} ({new FileInfo(outPath).Length / 1024:N0} KB)";
            Console.WriteLine($"RPFCONVERT {path} -> {outPath}");
            panel.Rpf.RefreshCurrent_O1();
        }

        private void DoRpfDiskConvertMany_V52(List<string> paths)
        {
            if (paths == null || paths.Count == 0) return;
            int ok = 0, failed = 0;
            string firstError = null;
            foreach (var p in paths)
            {
                if (ConvertDiskXml_V52(p, out var error) != null) ok++;
                else { failed++; firstError ??= Path.GetFileName(p) + ": " + (error ?? "unknown"); }
            }
            panel.RpfStatus = $"Converted {ok} of {paths.Count} XML file(s) to game files" +
                              (failed > 0 ? $" - {failed} failed ({firstError})" : "");
            Console.WriteLine($"RPFCONVERT batch: {ok} ok, {failed} failed of {paths.Count}");
            panel.Rpf.RefreshCurrent_O1();
        }

        private CollisionMesh BuildViewerCollision_V54(YbnFile ybn)
        {
            var keep = collisionView.Alpha;
            collisionView.Alpha = 0.92f;
            try { return collisionView.Build(ybn); }
            finally { collisionView.Alpha = keep; }
        }

        public struct XmlSweep_V53
        {
            public int Converted, UpToDate, Failed, Remaining;
            public string FirstError;
        }

        public static XmlSweep_V53 SweepConvertXmls_V53(string root, int maxMs = 4500)
        {
            var r = new XmlSweep_V53();
            try
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return r;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                foreach (var f in Directory.EnumerateFiles(root, "*.xml", SearchOption.AllDirectories))
                {
                    if (!IsConvertibleXml_V52(Path.GetFileName(f))) continue;
                    var target = Path.Combine(Path.GetDirectoryName(f) ?? "", ConvertTargetName_V52(Path.GetFileName(f)));
                    try
                    {
                        if (File.Exists(target) &&
                            File.GetLastWriteTimeUtc(target) >= File.GetLastWriteTimeUtc(f))
                        {
                            r.UpToDate++;
                            continue;
                        }
                    }
                    catch { }
                    if (sw.ElapsedMilliseconds > maxMs) { r.Remaining++; continue; }
                    if (ConvertDiskXml_V52(f, out var err) != null) r.Converted++;
                    else
                    {
                        r.Failed++;
                        r.FirstError ??= Path.GetFileName(f) + ": " + (err ?? "unknown");
                    }
                }
            }
            catch (Exception ex) { r.FirstError ??= ex.Message; }
            return r;
        }

        public static string SweepNote_V53(in XmlSweep_V53 s)
        {
            if (s.Converted == 0 && s.Failed == 0 && s.Remaining == 0) return "";
            var note = $" - {s.Converted} XML file(s) converted to game files";
            if (s.UpToDate > 0) note += $", {s.UpToDate} already current";
            if (s.Failed > 0) note += $", {s.Failed} failed ({s.FirstError})";
            if (s.Remaining > 0) note += $", {s.Remaining} left - use the folder's Convert all button";
            return note;
        }

        private void SeqTest_RpfDiskConvert_V52(Action<string, bool, string> check)
        {
            string dir = null;
            try
            {
                dir = Path.Combine(Path.GetTempPath(), "rle_v52_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(dir);
                var xml = Path.Combine(dir, "rle_v52.ymap.xml");
                File.WriteAllText(xml,
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<CMapData>\n <name>rle_v52</name>\n <parent/>\n" +
                    " <flags value=\"0\"/>\n <contentFlags value=\"1\"/>\n" +
                    " <streamingExtentsMin x=\"-10\" y=\"-10\" z=\"-10\"/>\n <streamingExtentsMax x=\"10\" y=\"10\" z=\"10\"/>\n" +
                    " <entitiesExtentsMin x=\"-1\" y=\"-1\" z=\"-1\"/>\n <entitiesExtentsMax x=\"1\" y=\"1\" z=\"1\"/>\n" +
                    " <entities/>\n</CMapData>\n");

                check("v52 convert: a .ybn.xml/.ymap.xml disk file is recognised as convertible",
                      IsConvertibleXml_V52("hi@apa_ch2_04_6.ybn.xml") && IsConvertibleXml_V52("rle.ymap.xml") &&
                      !IsConvertibleXml_V52("readme.xml") && !IsConvertibleXml_V52("model.ydr"),
                      "double game extension + a converter = yes; plain xml or binary = no");

                var outPath = ConvertDiskXml_V52(xml, out var error);
                check("v52 convert: the XML converts to the game file beside it",
                      outPath != null && File.Exists(outPath) &&
                      Path.GetFileName(outPath) == "rle_v52.ymap" && new FileInfo(outPath).Length > 50,
                      outPath != null ? $"{Path.GetFileName(outPath)}, {new FileInfo(outPath).Length:N0} bytes"
                                      : error ?? "failed");

                check("v54 convert: ...and the old XML is deleted once the game file is written",
                      !File.Exists(xml), File.Exists(xml) ? "still there" : "gone");

                var bad = Path.Combine(dir, "notes.abc.xml");
                File.WriteAllText(bad, "<x/>");
                check("v52 convert: a file with no converter fails honestly instead of writing junk",
                      ConvertDiskXml_V52(bad, out var whyBad) == null && File.Exists(bad),
                      whyBad ?? "no error given");
            }
            catch (Exception ex) { check("v52 convert", false, ex.Message); }
            finally
            {
                try { if (dir != null && Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
