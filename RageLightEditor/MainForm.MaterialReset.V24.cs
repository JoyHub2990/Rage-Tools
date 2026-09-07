using System;
using System.IO;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private void SeqTest_MaterialReset_V24(Action<string, bool, string> check)
        {
            var c = gameFiles?.Cache;
            if (c?.YdrDict == null || !gameFiles.Ready) { Console.WriteLine("  v24 reset: (skipped - no game folder)"); return; }
            if (!c.YdrDict.TryGetValue(CodeWalker.GameFiles.JenkHash.GenHash("w_ar_carbinerifle"), out var fe) || fe == null)
            { check("v24 reset: the carbine is in the archives", false, "not found"); return; }

            string tmp = Path.Combine(Path.GetTempPath(), "rle_v24_reset");
            Directory.CreateDirectory(tmp);
            string path = Path.Combine(tmp, "w_ar_carbinerifle.ydr");
            try
            {
                var data = ArchiveBrowser.ExtractForDisk(fe);
                File.WriteAllBytes(path, data);
                bool loaded = scene.LoadModelFile(path, additive: false);
                check("v24 reset: the carbine loads into the material editor's scene", loaded, loaded ? "loaded" : scene.LoadError);
                if (loaded) MaterialPanel.ResetSelfTest_V24(check, scene, materialPanel);
            }
            catch (Exception ex) { check("v24 reset: the fixture could be written and read", false, ex.Message); }
            finally
            {
                try { scene.CloseAllFiles(); } catch { }
                try { File.Delete(path); } catch { }
            }
        }
    }
}

