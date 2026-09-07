using System;
using System.Collections.Generic;

namespace RageLightEditor.Editor
{
    public static class XmlPaint_R3
    {
        public enum Tok : byte
        {
            Text = 0,
            Punct,
            Tag,
            Attr,
            Value,
            Comment,
            Number,
        }

        public readonly struct Span
        {
            public readonly int Start, Length;
            public readonly Tok Kind;
            public Span(int start, int length, Tok kind) { Start = start; Length = length; Kind = kind; }
        }

        [Flags]
        public enum State : byte { None = 0, InComment = 1, InTag = 2 }

        public static State[] Scan(string[] lines)
        {
            var states = new State[(lines?.Length ?? 0) + 1];
            if (lines == null) return states;
            var spans = new List<Span>(64);
            var st = State.None;
            for (int i = 0; i < lines.Length; i++)
            {
                states[i] = st;
                st = Tokenise(lines[i], st, spans);
            }
            states[lines.Length] = st;
            return states;
        }

        public static State Tokenise(string line, State start, List<Span> into)
        {
            into.Clear();
            if (string.IsNullOrEmpty(line)) return start;
            int n = line.Length;
            int i = 0;
            var st = start;

            while (i < n)
            {
                if ((st & State.InComment) != 0)
                {
                    int end = line.IndexOf("-->", i, StringComparison.Ordinal);
                    if (end < 0) { Add(into, i, n - i, Tok.Comment); return st; }
                    Add(into, i, end + 3 - i, Tok.Comment);
                    i = end + 3;
                    st &= ~State.InComment;
                    continue;
                }

                if ((st & State.InTag) != 0)
                {
                    i = InsideTag(line, i, into, ref st);
                    continue;
                }

                int lt = line.IndexOf('<', i);
                if (lt < 0) { AddContent(line, i, n - i, into); return st; }
                if (lt > i) AddContent(line, i, lt - i, into);

                if (Match(line, lt, "<!--"))
                {
                    st |= State.InComment;
                    i = lt;
                    continue;
                }
                if (Match(line, lt, "<?") || Match(line, lt, "<!"))
                {
                    int q = line.IndexOf('>', lt);
                    if (q < 0) { Add(into, lt, n - lt, Tok.Comment); return st; }
                    Add(into, lt, q + 1 - lt, Tok.Comment);
                    i = q + 1;
                    continue;
                }

                int p = lt + 1;
                if (p < n && line[p] == '/') p++;
                Add(into, lt, p - lt, Tok.Punct);
                int name = p;
                while (p < n && (char.IsLetterOrDigit(line[p]) || line[p] == '_' || line[p] == ':' ||
                                 line[p] == '.' || line[p] == '-')) p++;
                if (p > name) Add(into, name, p - name, Tok.Tag);
                st |= State.InTag;
                i = p;
            }
            return st;
        }

        private static int InsideTag(string line, int i, List<Span> into, ref State st)
        {
            int n = line.Length;
            while (i < n)
            {
                char c = line[i];
                if (c == '>')
                {
                    Add(into, i, 1, Tok.Punct);
                    st &= ~State.InTag;
                    return i + 1;
                }
                if (c == '/' && i + 1 < n && line[i + 1] == '>')
                {
                    Add(into, i, 2, Tok.Punct);
                    st &= ~State.InTag;
                    return i + 2;
                }
                if (c == '"' || c == '\'')
                {
                    int end = line.IndexOf(c, i + 1);
                    if (end < 0) { Add(into, i, n - i, Tok.Value); return n; }
                    Add(into, i, end + 1 - i, Tok.Value);
                    i = end + 1;
                    continue;
                }
                if (c == '=' || char.IsWhiteSpace(c))
                {
                    Add(into, i, 1, Tok.Punct);
                    i++;
                    continue;
                }
                int s = i;
                while (i < n && !char.IsWhiteSpace(line[i]) && line[i] != '=' && line[i] != '>' &&
                       line[i] != '"' && line[i] != '\'' && !(line[i] == '/' && i + 1 < n && line[i + 1] == '>')) i++;
                if (i == s) i++;
                Add(into, s, i - s, Tok.Attr);
            }
            return i;
        }

        private static void AddContent(string line, int start, int len, List<Span> into)
        {
            int end = start + len;
            int i = start;
            while (i < end)
            {
                bool word = IsWordChar(line[i]);
                int s = i;
                while (i < end && IsWordChar(line[i]) == word) i++;
                if (i == s) i++;
                Add(into, s, i - s, word && LooksNumeric(line, s, i) ? Tok.Number : Tok.Text);
            }
        }

        private static bool IsWordChar(char c) =>
            char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '-' || c == '+';

        private static bool LooksNumeric(string s, int a, int b)
        {
            bool digit = false;
            for (int k = a; k < b; k++)
            {
                char c = s[k];
                if (c >= '0' && c <= '9') { digit = true; continue; }
                if (c == '.' || c == '-' || c == '+' || c == 'e' || c == 'E') continue;
                return false;
            }
            char f = s[a];
            return digit && ((f >= '0' && f <= '9') || f == '-' || f == '+' || f == '.');
        }

        private static bool Match(string s, int at, string what)
        {
            if (at + what.Length > s.Length) return false;
            for (int i = 0; i < what.Length; i++) if (s[at + i] != what[i]) return false;
            return true;
        }

        private static void Add(List<Span> into, int start, int len, Tok kind)
        {
            if (len <= 0) return;
            if (into.Count > 0)
            {
                var last = into[into.Count - 1];
                if (last.Kind == kind && last.Start + last.Length == start)
                {
                    into[into.Count - 1] = new Span(last.Start, last.Length + len, kind);
                    return;
                }
            }
            into.Add(new Span(start, len, kind));
        }
    }
}

