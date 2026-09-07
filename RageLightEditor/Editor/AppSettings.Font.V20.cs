using System.IO;

namespace RageLightEditor.Editor
{
    public partial class AppSettings
    {
        public string UiFontV20 { get; set; } = "";
        public float UiFontPxV20 { get; set; } = 0f;

        public static (string file, float px) PeekUiFont_V20()
        {
            try
            {
                if (!File.Exists(FilePath)) return ("", 0f);
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(FilePath));
                var root = doc.RootElement;
                string file = root.TryGetProperty("UiFontV20", out var f) && f.ValueKind == System.Text.Json.JsonValueKind.String ? f.GetString() ?? "" : "";
                float px = root.TryGetProperty("UiFontPxV20", out var p) && p.TryGetSingle(out float v) ? v : 0f;
                return (file, px);
            }
            catch { return ("", 0f); }
        }
    }
}
