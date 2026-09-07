using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace RageLightEditor.Editor
{
    public class ProjectFile
    {
        public int Version { get; set; } = 1;
        public List<string> Models { get; set; } = new List<string>();
        public List<string> Ytds { get; set; } = new List<string>();
        public string Ytyp { get; set; } = "";
        public string GtaFolder { get; set; } = "";
        public List<string> PropFolders { get; set; } = new List<string>();
        public string TimecycleXml { get; set; } = "";
        public string GameTimecycle { get; set; } = "";
        public int PreviewHour { get; set; } = -1;
        public float[] CameraTarget { get; set; }
        public float CameraDistance { get; set; }
        public float CameraYaw { get; set; }
        public float CameraPitch { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public string Path { get; set; }

        public const string Extension = ".rlep";
        public const string Filter = "RAGE Tools project (*.rlep)|*.rlep|All files (*.*)|*.*";

        public void Save(string path)
        {
            var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
            var copy = new ProjectFile
            {
                Version = Version,
                Ytyp = MakeRelative(Ytyp, dir),
                GtaFolder = GtaFolder,
                PropFolders = new List<string>(PropFolders),
                TimecycleXml = MakeRelative(TimecycleXml, dir),
                GameTimecycle = GameTimecycle,
                PreviewHour = PreviewHour,
                CameraTarget = CameraTarget,
                CameraDistance = CameraDistance,
                CameraYaw = CameraYaw,
                CameraPitch = CameraPitch,
            };
            foreach (var m in Models) copy.Models.Add(MakeRelative(m, dir));
            foreach (var t in Ytds) copy.Ytds.Add(MakeRelative(t, dir));

            File.WriteAllText(path, JsonSerializer.Serialize(copy, new JsonSerializerOptions { WriteIndented = true }));
            Path = path;
        }

        public static ProjectFile Load(string path)
        {
            var p = JsonSerializer.Deserialize<ProjectFile>(File.ReadAllText(path)) ?? new ProjectFile();
            var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
            p.Models ??= new List<string>();
            p.Ytds ??= new List<string>();
            p.PropFolders ??= new List<string>();
            for (int i = 0; i < p.Models.Count; i++) p.Models[i] = MakeAbsolute(p.Models[i], dir);
            for (int i = 0; i < p.Ytds.Count; i++) p.Ytds[i] = MakeAbsolute(p.Ytds[i], dir);
            p.Ytyp = MakeAbsolute(p.Ytyp, dir);
            p.TimecycleXml = MakeAbsolute(p.TimecycleXml, dir);
            p.Path = path;
            return p;
        }

        private static string MakeRelative(string path, string baseDir)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(baseDir)) return path ?? "";
            try
            {
                var full = System.IO.Path.GetFullPath(path);
                if (!full.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase)) return full;
                return System.IO.Path.GetRelativePath(baseDir, full);
            }
            catch { return path; }
        }

        private static string MakeAbsolute(string path, string baseDir)
        {
            if (string.IsNullOrEmpty(path)) return "";
            try
            {
                return System.IO.Path.IsPathRooted(path) ? path
                    : System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, path));
            }
            catch { return path; }
        }
    }
}

