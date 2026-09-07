using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public sealed class TextEditView_R3
    {

        public string Title = "";
        public string Text = "";
        private string original = "";
        private string[] lines = Array.Empty<string>();
        private XmlPaint_R3.State[] lineStates = Array.Empty<XmlPaint_R3.State>();
        private readonly List<XmlPaint_R3.Span> spans = new List<XmlPaint_R3.Span>(64);

        private int version = 1;
        private int builtVersion, dirtyVersion;
        private bool dirtyCache;

        public bool CanSave;
        public string CannotSaveWhy = "";

        public bool Dirty
        {
            get
            {
                if (dirtyVersion != version)
                {
                    dirtyVersion = version;
                    dirtyCache = !string.Equals(Text, original, StringComparison.Ordinal);
                }
                return dirtyCache;
            }
        }

        public bool Editing;
        public bool Wrap;
        public bool ShowLineNumbers = true;
        public bool Colour = true;

        public string Query = "";
        public bool MatchCase;

        public bool RequestSave, RequestSaveAs;
        public string Status = "";

        private readonly List<int> matchLine = new List<int>();
        private readonly List<int> matchCol = new List<int>();
        private string builtQuery = "\0";
        private bool builtCase;
        private int builtQueryVersion = -1;
        private int current;
        private bool currentValid;
        public int MatchCount { get { EnsureSearch(); return matchLine.Count; } }
        public int CurrentMatch => matchLine.Count == 0 ? 0 : current + 1;

        private int scrollToLine = -1;

        public const int EditLimit = 1_500_000;

        public void Load(string title, string text, bool canSave, string cannotSaveWhy)
        {
            Title = title ?? "";
            Text = text ?? "";
            original = Text;
            CanSave = canSave;
            CannotSaveWhy = cannotSaveWhy ?? "";
            Status = "";
            Editing = false;
            version++;
            current = 0;
            scrollToLine = -1;
            builtQueryVersion = -1;
        }

        public void MarkSaved(string newTitle = null)
        {
            original = Text;
            if (!string.IsNullOrEmpty(newTitle)) Title = newTitle;
            version++;
        }

        public void Revert()
        {
            Text = original;
            version++;
        }

        private void EnsureLines()
        {
            if (builtVersion == version) return;
            builtVersion = version;
            lines = (Text ?? "").Replace("\r\n", "\n").Split('\n');
            lineStates = XmlPaint_R3.Scan(lines);
            wrapCols = -1;
        }

        public int LineCount { get { EnsureLines(); return lines.Length; } }

        public string LineAt(int oneBased)
        {
            EnsureLines();
            int i = oneBased - 1;
            return i >= 0 && i < lines.Length ? lines[i] : "";
        }

        private void EnsureSearch()
        {
            EnsureLines();
            if (builtQueryVersion == version && builtQuery == Query && builtCase == MatchCase) return;
            builtQueryVersion = version;
            builtQuery = Query;
            builtCase = MatchCase;
            matchLine.Clear();
            matchCol.Clear();
            currentValid = false;
            var q = Query ?? "";
            if (q.Length == 0) { current = 0; return; }
            var cmp = MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            const int cap = 50000;
            for (int i = 0; i < lines.Length && matchLine.Count < cap; i++)
            {
                var s = lines[i];
                int from = 0;
                while (from <= s.Length - q.Length)
                {
                    int at = s.IndexOf(q, from, cmp);
                    if (at < 0) break;
                    matchLine.Add(i);
                    matchCol.Add(at);
                    if (matchLine.Count >= cap) break;
                    from = at + Math.Max(q.Length, 1);
                }
            }
            if (current >= matchLine.Count) current = 0;
        }

        public void Step(int delta)
        {
            EnsureSearch();
            if (matchLine.Count == 0) return;
            if (!currentValid) { currentValid = true; current = delta >= 0 ? 0 : matchLine.Count - 1; }
            else
            {
                current = (current + delta) % matchLine.Count;
                if (current < 0) current += matchLine.Count;
            }
            scrollToLine = matchLine[current];
        }

        public void CurrentPosition(out int line, out int col)
        {
            EnsureSearch();
            if (matchLine.Count == 0 || current >= matchLine.Count) { line = 0; col = 0; return; }
            line = matchLine[current] + 1;
            col = matchCol[current] + 1;
        }

        private static readonly Vector4[] TokColours =
        {
            new Vector4(0.86f, 0.88f, 0.92f, 1.0f),
            new Vector4(0.52f, 0.56f, 0.64f, 1.0f),
            new Vector4(0.42f, 0.72f, 1.00f, 1.0f),
            new Vector4(0.94f, 0.78f, 0.42f, 1.0f),
            new Vector4(0.55f, 0.85f, 0.55f, 1.0f),
            new Vector4(0.44f, 0.55f, 0.46f, 1.0f),
            new Vector4(0.98f, 0.62f, 0.38f, 1.0f),
        };
        private static readonly Vector4 GutterColour = new Vector4(0.42f, 0.45f, 0.52f, 1.0f);
        private static uint HitColour => ImGui.ColorConvertFloat4ToU32(new Vector4(0.95f, 0.80f, 0.25f, 0.28f));
        private static uint HitCurrentColour => ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.55f, 0.15f, 0.55f));

        public void DrawToolbar()
        {
            EnsureSearch();

            ImGui.TextDisabled($"{lines.Length:N0} lines   {(Text?.Length ?? 0) / 1024:N0} KB");
            if (Dirty)
            {
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.72f, 0.25f, 1.0f));
                ImGui.TextUnformatted("edited");
                ImGui.PopStyleColor();
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("This text no longer matches the file it came from. Save writes it back.");
            }

            ImGui.SameLine();
            ImGui.SetNextItemWidth(Math.Max(ImGui.GetContentRegionAvail().X - 300.0f, 140.0f));
            bool enter = ImGui.InputTextWithHint("##r3find", "find a word (Enter for the next one)", ref Query, 256,
                                                 ImGuiInputTextFlags.EnterReturnsTrue);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Every match is highlighted and the current one picked out.\n" +
                                 "Enter or the arrows step through them; the count is beside them.");
            if (enter) Step(1);
            ImGui.SameLine();
            if (ImGui.ArrowButton("##r3prev", ImGuiDir.Up)) Step(-1);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("The previous match.");
            ImGui.SameLine();
            if (ImGui.ArrowButton("##r3next", ImGuiDir.Down)) Step(1);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("The next match.");
            ImGui.SameLine();
            if ((Query ?? "").Length == 0) ImGui.TextDisabled("no search");
            else if (matchLine.Count == 0)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.45f, 0.40f, 1.0f));
                ImGui.TextUnformatted("no matches");
                ImGui.PopStyleColor();
            }
            else
            {
                CurrentPosition(out int ln, out int cl);
                ImGui.Text($"{CurrentMatch:N0} / {matchLine.Count:N0}   line {ln:N0}");
                if (ImGui.IsItemHovered()) ImGui.SetTooltip($"line {ln:N0}, column {cl:N0}");
            }
            ImGui.SameLine();
            ImGui.Checkbox("Aa##r3case", ref MatchCase);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Match upper and lower case exactly.");

            ImGui.Checkbox("Colour##r3", ref Colour);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Tags, attributes, values, comments and numbers each in their own colour.");
            ImGui.SameLine();
            ImGui.Checkbox("Lines##r3", ref ShowLineNumbers);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Line numbers down the left.");
            ImGui.SameLine();
            ImGui.Checkbox("Wrap##r3", ref Wrap);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Fold long lines instead of scrolling sideways, at the window's own\n" +
                                 "width. Off by default: XML is easier to read in columns. Colours,\n" +
                                 "line numbers and the search highlight all still line up.");
            ImGui.SameLine();

            bool tooBig = (Text?.Length ?? 0) > EditLimit;
            ImGui.BeginDisabled(tooBig);
            bool ed = Editing;
            if (ImGui.Checkbox("Edit##r3", ref ed)) Editing = ed;
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(tooBig
                    ? $"This file is over {EditLimit / 1024 / 1024} MB of text - too big to type in without\n" +
                      "the window stalling. Extract it and edit it in a text editor instead."
                    : "Type in the text. The colours come back when you switch this off - an ImGui\n" +
                      "text box is one control and cannot be part coloured.");

            ImGui.SameLine();
            if (ImGui.SmallButton("Copy all##r3")) ImGui.SetClipboardText(Text ?? "");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("The whole file to the clipboard.");

            ImGui.SameLine();
            ImGui.BeginDisabled(!Dirty);
            if (ImGui.SmallButton("Revert##r3")) Revert();
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Throw the edits away and go back to what was opened.");

            ImGui.SameLine();
            ImGui.BeginDisabled(!CanSave);
            if (ImGui.SmallButton(Dirty ? "Save *##r3" : "Save##r3")) RequestSave = true;
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(CanSave
                    ? "Write this text back to the file it came from - converting the XML back to\n" +
                      "the game's own format on the way, exactly as importing it would."
                    : (CannotSaveWhy.Length > 0 ? CannotSaveWhy : "There is nowhere to write this back to."));
            ImGui.SameLine();
            if (ImGui.SmallButton("Save as...##r3")) RequestSaveAs = true;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Write it somewhere of your own choosing, as XML or converted -\n" +
                                 "the extension you type decides.");

            if (!string.IsNullOrEmpty(Status))
            {
                ImGui.SameLine();
                ImGui.TextDisabled("|");
                ImGui.SameLine();
                ImGui.TextUnformatted(Status);
            }
        }

        public void DrawBody(float height)
        {
            EnsureSearch();
            bool mono = UiMono_R3.Push();
            try
            {
                if (Editing) DrawEditBox(height);
                else DrawColoured(height);
            }
            finally { UiMono_R3.Pop(mono); }
        }

        private void DrawEditBox(float height)
        {
            uint cap = (uint)Math.Min(Math.Max((long)(Text?.Length ?? 0) * 2 + 8192, 65536), 24L * 1024 * 1024);
            if (ImGui.InputTextMultiline("##r3edit", ref Text, cap, new Vector2(-1, height),
                                         ImGuiInputTextFlags.AllowTabInput))
                version++;
            if (matchLine.Count > 0)
            {
                CurrentPosition(out int ln, out int cl);
                ImGui.TextDisabled($"match {CurrentMatch:N0} of {matchLine.Count:N0} is at line {ln:N0}, column {cl:N0} " +
                                   "- switch Edit off to see it highlighted");
            }
        }

        private void DrawColoured(float height)
        {
            var flags = Wrap ? ImGuiWindowFlags.None : ImGuiWindowFlags.HorizontalScrollbar;
            if (!ImGui.BeginChild("##r3body", new Vector2(0, height), ImGuiChildFlags.Borders, flags))
            {
                ImGui.EndChild();
                return;
            }

            float rowH = ImGui.GetTextLineHeightWithSpacing();
            float charW = UiMono_R3.CharWidth();
            float gutterW = ShowLineNumbers
                ? ImGui.CalcTextSize(lines.Length.ToString()).X + 12.0f
                : 0.0f;
            float avail = Math.Max(ImGui.GetContentRegionAvail().X - gutterW - 14.0f, 32.0f);

            if (Wrap) EnsureWrapLayout(avail, charW);

            if (scrollToLine >= 0)
            {
                int row = Wrap ? WrapRowOf(scrollToLine) : scrollToLine;
                float want = row * rowH - ImGui.GetWindowSize().Y * 0.4f;
                ImGui.SetScrollY(Math.Max(want, 0.0f));
                scrollToLine = -1;
            }

            int totalRows = Wrap ? (wrapRows.Length > 0 ? wrapRows[wrapRows.Length - 1] : 0) : lines.Length;
            float scroll = ImGui.GetScrollY();
            float viewH = ImGui.GetWindowSize().Y;
            int firstRow = Math.Max(0, (int)(scroll / rowH) - 2);
            int lastRow = Math.Min(totalRows, firstRow + (int)(viewH / rowH) + 5);

            if (firstRow > 0) ImGui.Dummy(new Vector2(1, firstRow * rowH));

            if (Wrap)
            {
                int line = WrapLineOfRow(firstRow);
                for (int r = firstRow; r < lastRow; r++)
                {
                    while (line + 1 < lines.Length && wrapRows[line + 1] <= r) line++;
                    int from = (r - wrapRows[line]) * wrapCols;
                    DrawRow(line, from, wrapCols, from == 0, gutterW, charW, rowH);
                }
            }
            else for (int i = firstRow; i < lastRow; i++) DrawRow(i, 0, int.MaxValue, true, gutterW, charW, rowH);

            int padBottom = totalRows - lastRow;
            if (padBottom > 0) ImGui.Dummy(new Vector2(1, padBottom * rowH));

            ImGui.EndChild();
        }

        private void DrawRow(int i, int from, int count, bool number, float gutterW, float charW, float rowH)
        {
            var line = lines[i];
            int len = Math.Max(Math.Min(count, line.Length - from), 0);

            if (ShowLineNumbers)
            {
                if (number)
                {
                    var num = (i + 1).ToString();
                    float w = ImGui.CalcTextSize(num).X;
                    ImGui.SetCursorPosX(Math.Max(gutterW - w - 8.0f, 0.0f));
                    ImGui.PushStyleColor(ImGuiCol.Text, GutterColour);
                    ImGui.TextUnformatted(num);
                    ImGui.PopStyleColor();
                }
                else ImGui.TextUnformatted(" ");
                ImGui.SameLine(gutterW, 0.0f);
            }

            if (matchLine.Count > 0 && (Query ?? "").Length > 0)
            {
                var scr = ImGui.GetCursorScreenPos();
                var dl = ImGui.GetWindowDrawList();
                int at = LowerBound(matchLine, i);
                for (int k = at; k < matchLine.Count && matchLine[k] == i; k++)
                {
                    int a = Math.Max(matchCol[k], from);
                    int b = Math.Min(matchCol[k] + Query.Length, from + len);
                    if (b <= a) continue;
                    float x0 = scr.X + (a - from) * charW;
                    float x1 = scr.X + (b - from) * charW;
                    dl.AddRectFilled(new Vector2(x0, scr.Y), new Vector2(x1, scr.Y + rowH),
                                     k == current ? HitCurrentColour : HitColour, 2.0f);
                }
            }

            if (len == 0) { ImGui.TextUnformatted(" "); return; }

            if (!Colour)
            {
                ImGui.TextUnformatted(line.Substring(from, len));
                return;
            }

            XmlPaint_R3.Tokenise(line, i < lineStates.Length ? lineStates[i] : XmlPaint_R3.State.None, spans);
            if (spans.Count == 0) { ImGui.TextUnformatted(line.Substring(from, len)); return; }
            bool first = true;
            for (int s = 0; s < spans.Count; s++)
            {
                var sp = spans[s];
                int a = Math.Max(sp.Start, from);
                int b = Math.Min(sp.Start + sp.Length, from + len);
                if (b <= a) continue;
                if (!first) ImGui.SameLine(0.0f, 0.0f);
                first = false;
                ImGui.PushStyleColor(ImGuiCol.Text, TokColours[(int)sp.Kind]);
                ImGui.TextUnformatted(line.Substring(a, b - a));
                ImGui.PopStyleColor();
            }
            if (first) ImGui.TextUnformatted(" ");
        }

        private static int LowerBound(List<int> list, int v)
        {
            int lo = 0, hi = list.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (list[mid] < v) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        private int[] wrapRows = Array.Empty<int>();
        private int wrapCols = -1;

        private void EnsureWrapLayout(float avail, float charW)
        {
            int cols = Math.Max(1, (int)(avail / Math.Max(charW, 1.0f)));
            if (cols == wrapCols && wrapRows.Length == lines.Length + 1) return;
            wrapCols = cols;
            wrapRows = new int[lines.Length + 1];
            int row = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                wrapRows[i] = row;
                row += Math.Max(1, (lines[i].Length + cols - 1) / cols);
            }
            wrapRows[lines.Length] = row;
        }

        private int WrapRowOf(int line)
        {
            if (wrapRows.Length == 0) return line;
            return wrapRows[Math.Clamp(line, 0, wrapRows.Length - 1)];
        }

        private int WrapLineOfRow(int row)
        {
            if (wrapRows.Length <= 1) return 0;
            int lo = 0, hi = wrapRows.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (wrapRows[mid] <= row) lo = mid; else hi = mid - 1;
            }
            return Math.Clamp(lo, 0, lines.Length - 1);
        }
    }
}

