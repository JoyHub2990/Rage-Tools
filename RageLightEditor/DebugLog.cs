using System;
using System.IO;
using System.Text;

namespace RageLightEditor
{
    public static class DebugLog
    {
        private static readonly StringBuilder sb = new StringBuilder();
        public static bool Enabled = false;

        public static void Log(string msg)
        {
            if (!Enabled) return;
            sb.AppendLine(msg);
        }

        public static void Flush()
        {
            if (!Enabled || sb.Length == 0) return;
            try
            {
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "rle_debug.log"), sb.ToString());
            }
            catch { }
        }
    }
}

