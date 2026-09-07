using System;
using System.Collections.Generic;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private void SeqTest_FiveM_U13(Action<string, bool, string> check)
        {
            try
            {
                var tc = new TimecycleData();
                var xml = "<timecycle_keyframe_data version=\"1.000000\"><cycle name=\"test\"><region name=\"GLOBAL\">" +
                          "<light_dir_col_r>1 1 1 1 1 1 1 1 1 1 1 1 1</light_dir_col_r>" +
                          "<light_dir_col_g>0.5 0.5 0.5 0.5 0.5 0.5 0.5 0.5 0.5 0.5 0.5 0.5 0.5</light_dir_col_g>" +
                          "</region></cycle></timecycle_keyframe_data>";
                bool loaded = tc.LoadTimecycleXmlText(xml, "test", out var err);
                var r = tc.Current;
                check("fivem tc: a cycle loads with a snapshot of its original values", loaded && r != null && r.Original != null && r.Original.Count == r.Values.Count, err ?? (r == null ? "no region" : $"{r.Values.Count} vars"));
                check("fivem tc: nothing edited means nothing pushed", TimecycleJson_U13(tc, 12.0f) == "{\"type\":\"timecycle\",\"off\":true}", TimecycleJson_U13(tc, 12.0f));
                if (r != null && r.Values.TryGetValue("light_dir_col_g", out var vals))
                {
                    for (int i = 0; i < vals.Length; i++) vals[i] = 0.9f;
                    var j = TimecycleJson_U13(tc, 12.0f);
                    check("fivem tc: only the edited variable goes to the game, at the tool's hour", j.Contains("\"light_dir_col_g\":0.9") && !j.Contains("light_dir_col_r"), j);
                }

                var modXml = "<timecycle_modifier_data version=\"1.000000\"><modifier name=\"u13_room\" numMods=\"1\" userFlags=\"0\">" +
                             "<light_ambient_down_col_r>0.2 0.2</light_ambient_down_col_r></modifier></timecycle_modifier_data>";
                bool modsLoaded = tc.LoadModifiersXmlText(modXml, "u13.xml", out var merr);
                var m = tc.Modifiers.Count > 0 ? tc.Modifiers[tc.Modifiers.Count - 1] : null;
                check("fivem tc: a modifier loads with its original values remembered", modsLoaded && m != null && m.Original != null && m.Original.Count == m.Values.Count, merr ?? (m == null ? "none" : $"{m.Values.Count} vars"));
                check("fivem tc: untouched modifiers stay quiet", TimecycleModJson_U13(tc, false) == "{\"type\":\"tcmod\",\"off\":true}", TimecycleModJson_U13(tc, false));
                if (m != null)
                {
                    m.Values["light_ambient_down_col_r"] = 0.7f;
                    var j = TimecycleModJson_U13(tc, false);
                    check("fivem tc: an edited modifier is sent by name with its values", j.Contains("\"name\":\"u13_room\"") && j.Contains("\"light_ambient_down_col_r\":0.7") && j.Contains("\"apply\":null"), j);
                    tc.SelectedModifier = tc.Modifiers.IndexOf(m);
                    tc.ModifierStrength = 0.5f;
                    j = TimecycleModJson_U13(tc, true);
                    check("fivem tc: previewing the modifier tab applies it in the game", j.Contains("\"apply\":\"u13_room\"") && j.Contains("\"strength\":0.5"), j);
                }

                check("fivem materials: the apply waits out the delay", !AutoApplyDue_U13(10.0, 11.0, 2.0f, true) && AutoApplyDue_U13(10.0, 12.5, 2.0f, true) && !AutoApplyDue_U13(10.0, 20.0, 2.0f, false), "");
                int s0 = MaterialEditing.EditSerial_U13;
                MaterialEditing.MarkDirty(null);
                check("fivem materials: every material edit bumps the serial the auto-apply watches", MaterialEditing.EditSerial_U13 == s0 + 1, $"{s0} -> {MaterialEditing.EditSerial_U13}");

                var frame = GameCapture_U13.CaptureWindow(Handle, 320);
                check("fivem view: capturing a window gives a scaled BGRA frame", frame != null && frame.Width <= 320 && frame.Width > 0 && frame.Bgra.Length == frame.Width * frame.Height * 4,
                      frame == null ? "null" : $"{frame.Width}x{frame.Height} {frame.Source}");
                var none = GameCapture_U13.CaptureWindow(IntPtr.Zero, 320);
                check("fivem view: no window means no frame", none == null, "");
                GameCapture_U13.FindGameWindow(out var title);
                check("fivem view: looking for the game window does not throw", true, title ?? "");
            }
            catch (Exception ex) { check("fivem u13: no exception", false, ex.ToString()); }
        }
    }
}
