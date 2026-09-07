using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private void ApplyMloCreatorSettings_K1()
        {
            var ui = Creator;
            if (ui == null || settings == null) return;
            ui.ExplorerWidthSetting = settings.MloCreatorExplorerWidth;
            if (settings.MloCreatorBridgePort > 0) ui.BridgePort = settings.MloCreatorBridgePort;
            ui.AssetView_O3 = MloCreatorPanel.AssetViewFor_P2(settings); ui.AssetColumns_O3 = System.Math.Clamp(settings.MloAssetColumns, 2, 8);
            ui.AssetViewChosen_P2 = settings.MloAssetViewChosen_P2;
        }

        private void ServiceMloCreatorProject_K1(MloCreatorPanel ui)
        {
            if (settings != null)
            {
                settings.MloCreatorExplorerWidth = ui.ExplorerWidthSetting;
                settings.MloCreatorBridgePort = ui.BridgePort;
                settings.MloAssetView = ui.AssetView_O3; settings.MloAssetColumns = ui.AssetColumns_O3;
                settings.MloAssetViewChosen_P2 = ui.AssetViewChosen_P2;
            }

            if (ui.RequestNewProject)
            {
                ui.RequestNewProject = false;
                ui.Session = new MloCreatorSession();
                if (scene != null) ui.Session.FitBoundsToScene(scene);
                ui.ProjectPath = null; ui.SelectRoom(0);
                ui.SetStatus("New empty interior.");
            }

            if (ui.RequestSaveProject || ui.RequestSaveProjectAs)
            {
                bool asDialog = ui.RequestSaveProjectAs || string.IsNullOrEmpty(ui.ProjectPath);
                ui.RequestSaveProject = false; ui.RequestSaveProjectAs = false;
                if (ui.Session == null) { ui.SetStatus("Nothing to save.", true); return; }
                string path = ui.ProjectPath;
                try
                {
                    if (asDialog)
                    {
                        using var dlg = new SaveFileDialog
                        {
                            Filter = "MLO Creator project (*" + MloCreatorProject.Extension + ")|*" + MloCreatorProject.Extension,
                            Title = "Save the interior's MLO Creator project",
                            FileName = Path.GetFileName(MloCreatorProject.DefaultPath(ui.Session, scene)),
                        };
                        var def = MloCreatorProject.DefaultPath(ui.Session, scene);
                        if (!string.IsNullOrEmpty(def)) { try { dlg.InitialDirectory = Path.GetDirectoryName(def); } catch { } }
                        if (dlg.ShowDialog(this) != DialogResult.OK) return;
                        path = dlg.FileName;
                    }
                    MloCreatorProject.Save(path, ui.Session, scene);
                    ui.ProjectPath = path;
                    if (settings != null) { settings.MloCreatorLastProject = path; settings.Save(); }
                    ui.SetStatus($"Project saved: {Path.GetFileName(path)}.");
                }
                catch (Exception ex) { ui.SetStatus("Save project failed: " + ex.Message, true); }
            }

            if (ui.RequestOpenProject)
            {
                ui.RequestOpenProject = false;
                try
                {
                    using var dlg = new OpenFileDialog
                    {
                        Filter = "MLO Creator project (*" + MloCreatorProject.Extension + ")|*" + MloCreatorProject.Extension,
                        Title = "Open an MLO Creator project",
                    };
                    if (!string.IsNullOrEmpty(settings?.MloCreatorLastProject)) { try { dlg.InitialDirectory = Path.GetDirectoryName(settings.MloCreatorLastProject); } catch { } }
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    OpenMloCreatorProject_K1(ui, dlg.FileName);
                }
                catch (Exception ex) { ui.SetStatus("Open project failed: " + ex.Message, true); }
            }
        }

        private void OpenMloCreatorProject_K1(MloCreatorPanel ui, string path)
        {
            var dto = MloCreatorProject.Read(path);
            bool loadedAny = false;
            if (dto.SceneFiles != null)
            {
                foreach (var f in dto.SceneFiles)
                {
                    if (string.IsNullOrEmpty(f) || !File.Exists(f)) continue;
                    if (scene.Files.Any(x => string.Equals(x.Path, f, StringComparison.OrdinalIgnoreCase))) continue;
                    try { LoadFile(f); loadedAny = true; } catch { }
                }
            }
            if (!string.IsNullOrEmpty(dto.ImportedYtyp) && File.Exists(dto.ImportedYtyp) &&
                scene.MloInfo?.Ytyps?.Any(y => string.Equals(y.Path, dto.ImportedYtyp, StringComparison.OrdinalIgnoreCase)) != true)
            {
                try { ImportYtyp(dto.ImportedYtyp); loadedAny = true; } catch { }
            }
            ui.Session = MloCreatorProject.FromDto(dto, scene);
            ui.ProjectPath = path;
            ui.SelectRoom(ui.Session.Rooms.Count > 1 ? 1 : 0);
            if (settings != null) { settings.MloCreatorLastProject = path; settings.Save(); }
            ui.SetStatus($"Project opened: {Path.GetFileName(path)} - {ui.Session.Rooms.Count} rooms, {ui.Session.Portals.Count} portals" +
                         (loadedAny ? ", scene reloaded." : "."));
            Console.WriteLine($"MLOPROJ open {path}: rooms {ui.Session.Rooms.Count} portals {ui.Session.Portals.Count} entities {ui.Session.Entities.Count} files {scene.Files.Count}");
        }
    }
}

