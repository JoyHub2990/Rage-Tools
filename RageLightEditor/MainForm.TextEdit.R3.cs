using System;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private string rpfTextLastSaveDir_R3;
        private bool xmlEditDone_R3;

        private void RpfTextTick_R3()
        {
            if (panel == null) return;
            var view = panel.RpfText_R3;
            if (view.RequestSave) { view.RequestSave = false; RpfTextSave_R3(false); }
            if (view.RequestSaveAs) { view.RequestSaveAs = false; RpfTextSave_R3(true); }
            XmlOpenHeadless_R3();
            XmlEditHeadless_R3();
        }

        private bool xmlOpenDone_R3;

        private void XmlOpenHeadless_R3()
        {
            if (xmlOpenDone_R3) return;
            var path = Environment.GetEnvironmentVariable("RLE_XMLOPEN");
            if (string.IsNullOrWhiteSpace(path)) return;
            xmlOpenDone_R3 = true;
            bool took = false;
            OpenMetaFileInExplorer_Q1(path.Trim(), ref took);

            var view = Environment.GetEnvironmentVariable("RLE_XMLVIEW");
            if (!string.IsNullOrWhiteSpace(view))
            {
                var v = panel.RpfText_R3;
                foreach (var w in view.Split(','))
                    switch (w.Trim().ToLowerInvariant())
                    {
                        case "wrap": v.Wrap = true; break;
                        case "edit": v.Editing = true; break;
                        case "nocolour": case "nocolor": v.Colour = false; break;
                        case "nolines": v.ShowLineNumbers = false; break;
                    }
                Console.WriteLine($"XMLVIEW wrap={v.Wrap} edit={v.Editing} colour={v.Colour} lines={v.ShowLineNumbers}");
            }

            Console.WriteLine($"XMLOPEN {path} -> {(took ? "shown as XML" : "refused")}, " +
                              $"{panel.RpfText_R3.LineCount:N0} lines, canSave={panel.RpfText_R3.CanSave}");
        }

        public bool RpfTextToBytes_R3(string viewTitle, string text, out byte[] data, out string outName, out string why)
        {
            data = null;
            outName = viewTitle ?? "file.txt";
            why = null;
            var lower = outName.ToLowerInvariant();

            bool converted = lower.EndsWith(".xml", StringComparison.Ordinal) &&
                             lower.IndexOf('.') != lower.LastIndexOf('.');
            if (!converted)
            {
                data = new UTF8Encoding(false).GetBytes(text ?? "");
                return true;
            }

            var mformat = XmlMeta.GetXMLFormat(lower, out int trim);
            if (mformat == MetaFormat.XML)
            {
                data = new UTF8Encoding(false).GetBytes(text ?? "");
                return true;
            }

            var doc = new XmlDocument();
            try { doc.LoadXml(text ?? ""); }
            catch (XmlException ex)
            {
                why = $"the XML does not parse: {ex.Message}";
                return false;
            }
            catch (Exception ex)
            {
                why = "the XML does not parse: " + ex.Message;
                return false;
            }

            var tmp = Path.Combine(Path.GetTempPath(), "rle_r3xml");
            try { Directory.CreateDirectory(tmp); } catch { }
            var inpath = Path.Combine(tmp, Path.GetFileNameWithoutExtension(outName.Substring(0, outName.Length - trim)));

            try { data = XmlMeta.GetData(doc, mformat, inpath); }
            catch (Exception ex)
            {
                why = $"the XML is valid but {XmlMeta.GetXMLFormatName(mformat)} refused it: {ex.Message}";
                return false;
            }
            if (data == null || data.Length == 0)
            {
                why = $"{XmlMeta.GetXMLFormatName(mformat)} produced nothing from this XML";
                return false;
            }
            outName = outName.Substring(0, outName.Length - trim);
            return true;
        }

        private void RpfTextSave_R3(bool saveAs)
        {
            var view = panel.RpfText_R3;
            var src = panel.RpfViewFrom_Q1;
            string title = panel.RpfViewTitle ?? src?.Name ?? "file.txt";

            if (!RpfTextToBytes_R3(title, view.Text, out var data, out var outName, out var why))
            {
                view.Status = why;
                Console.WriteLine("XMLEDIT refused: " + why);
                return;
            }

            if (saveAs) { RpfTextSaveAs_R3(view, title, outName, data); return; }

            if (src == null)
            {
                view.Status = "this text has no file to go back to - use Save as...";
                return;
            }

            try
            {
                if (src.DiskPath != null)
                {
                    if (File.Exists(src.DiskPath) && !File.Exists(src.DiskPath + ".bak"))
                        File.Copy(src.DiskPath, src.DiskPath + ".bak");
                    File.WriteAllBytes(src.DiskPath, data);
                    view.MarkSaved();
                    view.Status = $"wrote {Path.GetFileName(src.DiskPath)} ({data.Length:N0} bytes) - {RoundTripNote_R3(src, title)}";
                    Console.WriteLine($"XMLEDIT wrote {src.DiskPath} {data.Length} bytes");
                    panel.RpfStatus = "saved " + Path.GetFileName(src.DiskPath);
                    return;
                }

                if (src.Entry == null) { view.Status = "this text has no file to go back to - use Save as..."; return; }

                if (!panel.RpfEditMode)
                {
                    view.Status = "edit mode is off - nothing was written";
                    return;
                }
                var dir = src.Entry.Parent;
                if (dir == null) { view.Status = "this entry has no folder in the archive to write into"; return; }

                RpfEdit.EnsureBackup(src.Entry.File?.GetPhysicalFilePath());
                var created = RpfFile.CreateFile(dir, outName, data);
                if (created != null) src.Entry = created;
                panel.Rpf?.RefreshCurrent_O1();
                view.MarkSaved();
                view.Status = $"wrote {outName} into {dir.Path} ({data.Length:N0} bytes) - {RoundTripNote_R3(src, title)}";
                Console.WriteLine($"XMLEDIT wrote {dir.Path}\\{outName} {data.Length} bytes");
                panel.RpfStatus = "saved " + outName + " into the archive";
            }
            catch (Exception ex)
            {
                view.Status = "save failed: " + ex.Message;
                Console.WriteLine("XMLEDIT save failed: " + ex);
            }
        }

        private void RpfTextSaveAs_R3(TextEditView_R3 view, string title, string outName, byte[] data)
        {
            using var dlg = new SaveFileDialog
            {
                Title = "Save the edited file",
                Filter = "The file itself|*.*|XML text|*.xml",
                FileName = outName,
            };
            if (!string.IsNullOrEmpty(rpfTextLastSaveDir_R3) && Directory.Exists(rpfTextLastSaveDir_R3))
                dlg.InitialDirectory = rpfTextLastSaveDir_R3;
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                bool asXml = dlg.FileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                             !title.Equals(Path.GetFileName(dlg.FileName), StringComparison.OrdinalIgnoreCase);
                var bytes = asXml ? new UTF8Encoding(false).GetBytes(view.Text ?? "") : data;
                File.WriteAllBytes(dlg.FileName, bytes);
                rpfTextLastSaveDir_R3 = Path.GetDirectoryName(dlg.FileName);
                view.MarkSaved();
                view.Status = $"wrote {Path.GetFileName(dlg.FileName)} ({bytes.Length:N0} bytes)";
                Console.WriteLine($"XMLEDIT saved as {dlg.FileName} {bytes.Length} bytes");
            }
            catch (Exception ex)
            {
                view.Status = "save failed: " + ex.Message;
                Console.WriteLine("XMLEDIT save-as failed: " + ex);
            }
        }

        private string RoundTripNote_R3(LightPanel.RpfViewSource_Q1 src, string title)
        {
            try
            {
                var lower = (title ?? "").ToLowerInvariant();
                if (!(lower.EndsWith(".xml") && lower.IndexOf('.') != lower.LastIndexOf('.')))
                    return "written as text";

                byte[] back;
                string xml;
                var tmp = Path.Combine(Path.GetTempPath(), "rle_r3xml");
                Directory.CreateDirectory(tmp);
                if (src.Entry != null)
                {
                    back = ArchiveBrowser.Extract(src.Entry);
                    xml = back == null ? null : MetaXml.GetXml(src.Entry, back, out _, tmp);
                }
                else
                {
                    back = File.ReadAllBytes(src.DiskPath);
                    var ext = (Path.GetExtension(src.DiskPath) ?? "").ToLowerInvariant();
                    xml = MetaXmlOfDiskFile_Q1(ext, Path.GetFileName(src.DiskPath), back, out _);
                }
                if (string.IsNullOrEmpty(xml))
                {
                    Console.WriteLine("XMLEDIT ROUNDTRIP FAILED: the file read back could not be converted");
                    return "WARNING: the file does not read back - check the XML";
                }
                int lines = 1;
                for (int i = 0; i < xml.Length; i++) if (xml[i] == '\n') lines++;
                Console.WriteLine($"XMLEDIT roundtrip ok: read back {back.Length} bytes -> {xml.Length} chars, {lines} lines");
                return $"reads back as {lines:N0} lines of XML";
            }
            catch (Exception ex)
            {
                Console.WriteLine("XMLEDIT ROUNDTRIP FAILED: " + ex.Message);
                return "WARNING: the file does not read back (" + ex.Message + ")";
            }
        }

        private bool xmlFindDone_R3;

        private void XmlFindHeadless_R3()
        {
            if (xmlFindDone_R3) return;
            var want = Environment.GetEnvironmentVariable("RLE_XMLFIND");
            if (string.IsNullOrWhiteSpace(want)) return;
            var view = panel.RpfText_R3;
            if (!panel.RpfViewOpen || string.IsNullOrEmpty(view.Text)) return;
            xmlFindDone_R3 = true;

            view.Query = want;
            view.Step(1);
            view.CurrentPosition(out int line, out int col);
            Console.WriteLine($"XMLFIND '{want}' in {panel.RpfViewTitle}: {view.MatchCount:N0} matches over " +
                              $"{view.LineCount:N0} lines; the first is at line {line}, column {col}");
            if (line > 0) Console.WriteLine("XMLFIND line " + line + ": " + view.LineAt(line).Trim());
        }

        private void XmlEditHeadless_R3()
        {
            XmlFindHeadless_R3();
            if (xmlEditDone_R3) return;
            var spec = Environment.GetEnvironmentVariable("RLE_XMLEDIT");
            if (string.IsNullOrWhiteSpace(spec)) return;
            var view = panel.RpfText_R3;
            if (!panel.RpfViewOpen || string.IsNullOrEmpty(view.Text)) return;
            xmlEditDone_R3 = true;

            var parts = spec.Split('|');
            string find = parts[0], repl = parts.Length > 1 ? parts[1] : parts[0];
            int at = view.Text.IndexOf(find, StringComparison.Ordinal);
            if (at < 0)
            {
                Console.WriteLine($"XMLEDIT '{find}' is not in {panel.RpfViewTitle} ({view.Text.Length} chars)");
                return;
            }
            view.Query = find;
            view.Text = view.Text.Substring(0, at) + repl + view.Text.Substring(at + find.Length);
            view.Load(panel.RpfViewTitle, view.Text, view.CanSave, view.CannotSaveWhy);
            view.Query = repl;
            Console.WriteLine($"XMLEDIT replaced '{find}' with '{repl}' at {at} in {panel.RpfViewTitle}; " +
                              $"{view.MatchCount} matches for the new text, {view.LineCount} lines");
            RpfTextSave_R3(false);
            Console.WriteLine("XMLEDIT status: " + view.Status);
        }

        private void RpfTextEditTest_R3(Action<string, bool, string> check)
        {
            bool r1 = RpfTextToBytes_R3("notes.txt", "hello", out var d1, out var n1, out _);
            bool r2 = RpfTextToBytes_R3("thing.xml", "<a/>", out var d2, out var n2, out _);
            check("text editor writes plain files as themselves",
                  r1 && n1 == "notes.txt" && d1.Length == 5 &&
                  r2 && n2 == "thing.xml",
                  $"{n1} {d1?.Length} bytes; {n2} {d2?.Length} bytes (a single-dot .xml is not a converted asset)");

            bool bad = RpfTextToBytes_R3("thing.ytyp.xml", "<CMapTypes><oops>", out _, out _, out var whyBad);
            check("text editor refuses XML that does not parse",
                  !bad && !string.IsNullOrEmpty(whyBad) && whyBad.Contains("does not parse"),
                  whyBad ?? "(no message)");

            var view = new TextEditView_R3();
            view.Load("probe.xml", "<a>\n  <b name=\"x\">1</b>\n  <b name=\"x\">2</b>\n</a>\n", false, "");
            view.Query = "name";
            view.MatchCase = true;
            int n = view.MatchCount;
            view.Step(1);
            view.CurrentPosition(out int ln1, out _);
            view.Step(1);
            view.CurrentPosition(out int ln2, out _);
            view.Step(-1);
            view.CurrentPosition(out int ln3, out _);
            check("text editor search counts and steps",
                  n == 2 && ln1 == 2 && ln2 == 3 && ln3 == 2 && view.LineCount == 5,
                  $"{n} matches, first at line {ln1}, next at line {ln2}, back to line {ln3}, {view.LineCount} lines");

            var spans = new System.Collections.Generic.List<XmlPaint_R3.Span>();
            XmlPaint_R3.Tokenise("  <name>v_res_mp_bed</name>", XmlPaint_R3.State.None, spans);
            bool sawTag = false, sawText = false;
            foreach (var s in spans)
            {
                if (s.Kind == XmlPaint_R3.Tok.Tag) sawTag = true;
                if (s.Kind == XmlPaint_R3.Tok.Text) sawText = true;
            }
            spans.Clear();
            XmlPaint_R3.Tokenise("  <Item value=\"1.5\" />", XmlPaint_R3.State.None, spans);
            bool sawAttr = false, sawValue = false;
            foreach (var s in spans)
            {
                if (s.Kind == XmlPaint_R3.Tok.Attr) sawAttr = true;
                if (s.Kind == XmlPaint_R3.Tok.Value) sawValue = true;
            }
            var st = XmlPaint_R3.Tokenise("<!-- a note", XmlPaint_R3.State.None, spans);
            bool carried = (st & XmlPaint_R3.State.InComment) != 0;
            XmlPaint_R3.Tokenise("still the note -->", st, spans);
            bool closed = spans.Count > 0 && spans[0].Kind == XmlPaint_R3.Tok.Comment;
            check("xml colouring names tags, attributes, values and comments",
                  sawTag && sawText && sawAttr && sawValue && carried && closed,
                  $"tag={sawTag} text={sawText} attr={sawAttr} value={sawValue} multiline comment={carried && closed}");

            var ytyp = FindProbeYtyp_Q1();
            if (ytyp == null)
            {
                check("ytyp XML round trip", true, "no .ytyp on this machine to test with - skipped");
                return;
            }
            RpfArchiveEditTest_R3(check, ytyp);
            try
            {
                var bytes = File.ReadAllBytes(ytyp);
                var f = new YtypFile();
                f.Load(bytes);
                var xml = MetaXml.GetXml(f, out _);
                bool gotXml = !string.IsNullOrEmpty(xml);

                string edited = xml, marker = "lodDist value=\"";
                int at = gotXml ? xml.IndexOf(marker, StringComparison.Ordinal) : -1;
                bool changed = false;
                if (at >= 0)
                {
                    int s = at + marker.Length;
                    int e = xml.IndexOf('"', s);
                    if (e > s)
                    {
                        edited = xml.Substring(0, s) + "1234.5" + xml.Substring(e);
                        changed = true;
                    }
                }

                bool built = RpfTextToBytes_R3(Path.GetFileName(ytyp) + ".xml", edited, out var outBytes,
                                               out var outName, out var whyNot);
                bool reads = false, kept = false;
                if (built)
                {
                    var back = new YtypFile();
                    back.Load(outBytes);
                    reads = back.AllArchetypes != null && back.AllArchetypes.Length > 0;
                    var backXml = MetaXml.GetXml(back, out _);
                    kept = !changed || (backXml != null && backXml.Contains("1234.5"));
                }
                check("ytyp XML round trip: edit, convert back, read it again",
                      gotXml && built && reads && kept,
                      $"{Path.GetFileName(ytyp)}: xml={xml?.Length ?? 0} chars, edited={changed}, " +
                      $"binary={(built ? outBytes.Length.ToString("N0") + " bytes as " + outName : "FAILED: " + whyNot)}, " +
                      $"reads back={reads}, the edit survived={kept}");
            }
            catch (Exception ex)
            {
                check("ytyp XML round trip: edit, convert back, read it again", false, ex.Message);
            }
        }

        private void RpfArchiveEditTest_R3(Action<string, bool, string> check, string ytypPath)
        {
            var root = Path.Combine(Path.GetTempPath(), "rle_r3_rpf");
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
            try
            {
                Directory.CreateDirectory(root);
                var arcPath = Path.Combine(root, "r3_test.rpf");
                var rpf = RpfFile.CreateNew(root, arcPath, RpfEncryption.OPEN);

                var entry = RpfFile.CreateFile(rpf.Root, "probe.ytyp", File.ReadAllBytes(ytypPath));
                bool imported = entry is RpfResourceFileEntry;

                var tmp = Path.Combine(root, "xml");
                Directory.CreateDirectory(tmp);
                var xml = MetaXml.GetXml(entry, ArchiveBrowser.Extract(entry), out _, tmp);
                bool converted = !string.IsNullOrEmpty(xml);

                const string marker = "lodDist value=\"";
                int at = converted ? xml.IndexOf(marker, StringComparison.Ordinal) : -1;
                bool changed = false;
                string edited = xml;
                if (at >= 0)
                {
                    int s = at + marker.Length, e = xml.IndexOf('"', at + marker.Length);
                    if (e > s) { edited = xml.Substring(0, s) + "4321.5" + xml.Substring(e); changed = true; }
                }

                bool built = RpfTextToBytes_R3("probe.ytyp.xml", edited, out var data, out var outName, out var why);
                if (built) RpfFile.CreateFile(entry.Parent, outName, data);

                var reopened = new RpfFile(arcPath, "r3_test.rpf");
                reopened.ScanStructure(null, null);
                var back = reopened.Root?.Files?.FirstOrDefault(f => f.NameLower == "probe.ytyp");
                string backXml = null;
                if (back != null)
                {
                    var bytes = reopened.ExtractFile(back);
                    if (bytes != null) backXml = MetaXml.GetXml(back, bytes, out _, tmp);
                }
                bool survived = backXml != null && (!changed || backXml.Contains("4321.5"));

                check("edited XML saves back into an .rpf and the archive re-opens",
                      imported && converted && built && back is RpfResourceFileEntry && survived,
                      $"imported={imported}, xml={xml?.Length ?? 0} chars, edited={changed}, " +
                      $"binary={(built ? data.Length.ToString("N0") + " bytes as " + outName : "FAILED: " + why)}, " +
                      $"re-opened entry={(back == null ? "missing" : back.GetType().Name + " " + back.FileSize + " B")}, " +
                      $"the edit survived={survived}");
            }
            catch (Exception ex)
            {
                check("edited XML saves back into an .rpf and the archive re-opens", false, ex.Message);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }
    }
}

