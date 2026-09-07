using System;
using System.Linq;
using System.Text;
using CodeWalker.GameFiles;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private bool weapMetaScanDone_V26;

        partial void OnWorldTick_WeaponMetaScan_V26()
        {
            if (weapMetaScanDone_V26) return;
            var model = Environment.GetEnvironmentVariable("RLE_WEAPMETA");
            if (string.IsNullOrEmpty(model)) return;
            var c = gameFiles?.Cache;
            if (c?.RpfMan == null || !gameFiles.Ready) return;
            weapMetaScanDone_V26 = true;

            int files = 0, xml = 0, pso = 0, other = 0, shown = 0;
            foreach (var rpf in c.RpfMan.AllRpfs)
            {
                if (rpf?.AllEntries == null) continue;
                foreach (var e in rpf.AllEntries)
                {
                    if (!(e is RpfFileEntry fe)) continue;
                    var n = fe.NameLower ?? fe.Name?.ToLowerInvariant() ?? "";
                    if (!n.EndsWith(".meta") || n.IndexOf("weapon") < 0) continue;
                    files++;
                    byte[] data = null;
                    try { data = fe.File?.ExtractFile(fe); } catch { }
                    if (data == null || data.Length < 4) { other++; continue; }
                    bool isPso = data[0] == (byte)'P' && data[1] == (byte)'S' && data[2] == (byte)'I' && data[3] == (byte)'N';
                    bool isRsc = data[0] == (byte)'R' && data[1] == (byte)'S' && data[2] == (byte)'C';
                    if (isPso || isRsc) { pso++; if (shown < 40) Console.WriteLine($"WEAPMETA {(isPso ? "PSO" : "RSC")} {fe.Path} ({data.Length:N0} B)"); continue; }
                    xml++;
                    string text;
                    try { text = Encoding.UTF8.GetString(data); } catch { other++; continue; }
                    int at = text.IndexOf(model, StringComparison.OrdinalIgnoreCase);
                    if (shown++ < 40) Console.WriteLine($"WEAPMETA XML {fe.Path} ({data.Length:N0} B){(at >= 0 ? "  MENTIONS " + model : "")}");
                    if (at >= 0 && n.Contains("weapons"))
                    {
                        int from = Math.Max(0, text.LastIndexOf("<Item type=", at, StringComparison.OrdinalIgnoreCase));
                        int ap = text.IndexOf("<AttachPoints>", at, StringComparison.OrdinalIgnoreCase);
                        int ape = ap >= 0 ? text.IndexOf("</AttachPoints>", ap, StringComparison.OrdinalIgnoreCase) : -1;
                        Console.WriteLine("WEAPMETA ---- " + fe.Name + " ----");
                        Console.WriteLine(text.Substring(from, Math.Min(300, text.Length - from)));
                        if (ap >= 0 && ape > ap) Console.WriteLine(text.Substring(ap, Math.Min(ape + 15 - ap, 6000)));
                        else Console.WriteLine("WEAPMETA (no AttachPoints block within this item)");
                    }
                    else if (at >= 0 && (n.Contains("components") || n.Contains("archetypes")))
                    {
                        int from = Math.Max(0, text.LastIndexOf("<Item", at, StringComparison.OrdinalIgnoreCase));
                        int to = Math.Min(text.Length, at + 900);
                        Console.WriteLine("WEAPMETA ---- " + fe.Name + " ----");
                        Console.WriteLine(text.Substring(from, to - from));
                    }
                }
            }
            Console.WriteLine($"WEAPMETA {files} weapon*.meta file(s): {xml} XML, {pso} PSO/RSC, {other} unreadable");
        }
    }
}

