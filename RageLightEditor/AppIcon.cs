using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace RageLightEditor
{
    internal static partial class AppIcon
    {
        private static Icon cached;

        public static Icon Load()
        {
            if (cached != null) return cached;
            try
            {
                using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("icon.ico");
                if (s != null) cached = new Icon(s);
            }
            catch { }
            if (cached == null)
            {
                try { cached = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            }
            return cached;
        }
    }
}

