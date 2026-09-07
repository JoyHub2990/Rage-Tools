using System;
using System.IO;
using System.Windows.Forms;
using RageLightEditor.Editor;
using CodeWalker.GameFiles;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private bool rpfDragOutBusy_V55;
        public bool InRpfDragOut_V55 => rpfDragOutBusy_V55;

        private void ServiceRpfDragOut_V55()
        {
            var p = panel;
            if (p?.RequestRpfDragOut_V55 == null || rpfDragOutBusy_V55) return;
            var r = p.RequestRpfDragOut_V55.Value;
            p.RequestRpfDragOut_V55 = null;

            string path = null;
            if (r.IsFs)
            {
                path = r.Path;
            }
            else if (r.Entry is RpfFileEntry fe)
            {
                try
                {
                    var data = ArchiveBrowser.ExtractForDisk(fe);
                    if (data == null || data.Length == 0)
                    {
                        p.RpfStatus = "could not extract " + fe.Name;
                        return;
                    }
                    var dir = Path.Combine(Path.GetTempPath(), "rle_dragout");
                    Directory.CreateDirectory(dir);
                    path = Path.Combine(dir, RpfExplorer.SafeName(fe.Name));
                    File.WriteAllBytes(path, data);
                }
                catch (Exception ex)
                {
                    p.RpfStatus = "could not extract " + fe.Name + ": " + ex.Message;
                    return;
                }
            }
            if (path == null || (!File.Exists(path) && !Directory.Exists(path))) return;

            rpfDragOutBusy_V55 = true;
            var name = Path.GetFileName(path.TrimEnd('\\', '/'));
            p.RpfStatus = "dragging " + name + " - drop it in Explorer or on the desktop";
            BeginInvoke(new Action(() =>
            {
                try
                {
                    var obj = new DataObject(DataFormats.FileDrop, new[] { path });
                    var effect = DoDragDrop(obj, DragDropEffects.Copy);
                    panel.RpfStatus = effect == DragDropEffects.None
                        ? "drag cancelled - " + name + " went nowhere"
                        : "dropped " + name;
                }
                catch (Exception ex) { panel.RpfStatus = "drag failed: " + ex.Message; }
                finally
                {
                    rpfDragOutBusy_V55 = false;
                    try { ImGuiNET.ImGui.GetIO().AddMouseButtonEvent(0, false); } catch { }
                }
            }));
        }
    }
}
