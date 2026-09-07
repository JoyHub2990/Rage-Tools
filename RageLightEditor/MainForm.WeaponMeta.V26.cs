using System;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private WeaponMeta_V26 weaponMetaArchives_V26;
        private WeaponMeta_V26 weaponMetaDisk_V26;
        private string weaponMetaDiskFor_V26;

        private WeaponMeta_V26 WeaponMetaFor_V26(string diskPath)
        {
            var man = gameFiles?.Cache?.RpfMan;
            if (weaponMetaArchives_V26 == null && man != null)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                weaponMetaArchives_V26 = WeaponMeta_V26.FromArchives(man);
                Console.WriteLine($"WEAPONMETA {weaponMetaArchives_V26.FilesRead} meta file(s) read in {sw.ElapsedMilliseconds:N0} ms: " +
                                  $"{weaponMetaArchives_V26.Weapons.Count} weapon(s), {weaponMetaArchives_V26.Components.Count} component(s), " +
                                  $"{weaponMetaArchives_V26.Txds.Count} texture dictionary name(s)" +
                                  (weaponMetaArchives_V26.FilesFailed > 0 ? $"; {weaponMetaArchives_V26.FilesFailed} unread" : ""));
            }
            if (string.IsNullOrEmpty(diskPath)) return weaponMetaArchives_V26;

            var dir = Path.GetDirectoryName(diskPath);
            if (weaponMetaDisk_V26 == null || !string.Equals(weaponMetaDiskFor_V26, dir, StringComparison.OrdinalIgnoreCase))
            {
                var m = man != null ? WeaponMeta_V26.FromArchives(man) : new WeaponMeta_V26();
                if (weaponMetaArchives_V26 != null)
                {
                    m = new WeaponMeta_V26();
                    foreach (var kv in weaponMetaArchives_V26.Weapons) m.Weapons[kv.Key] = kv.Value;
                    foreach (var kv in weaponMetaArchives_V26.Components) m.Components[kv.Key] = kv.Value;
                    foreach (var kv in weaponMetaArchives_V26.Txds) m.Txds[kv.Key] = kv.Value;
                }
                m.AddDiskTree(dir);
                weaponMetaDisk_V26 = m;
                weaponMetaDiskFor_V26 = dir;
                Console.WriteLine($"WEAPONMETA folder {dir}: {m.FilesRead} file(s) read, {m.Weapons.Count} weapon(s), {m.Components.Count} component(s)");
            }
            return weaponMetaDisk_V26;
        }

        private bool FindComponentModel_V26(string model, string weaponDiskPath, out RpfFileEntry entry, out string diskPath)
        {
            entry = null; diskPath = null;
            if (string.IsNullOrEmpty(model)) return false;

            if (!string.IsNullOrEmpty(weaponDiskPath))
            {
                var dir = Path.GetDirectoryName(weaponDiskPath);
                for (int up = 0; up <= 3 && dir != null; up++)
                {
                    try
                    {
                        var hit = Directory.EnumerateFiles(dir, model + ".ydr", SearchOption.AllDirectories).FirstOrDefault();
                        if (hit != null) { diskPath = hit; return true; }
                    }
                    catch { }
                    dir = Path.GetDirectoryName(dir);
                }
            }

            var dict = gameFiles?.Cache?.YdrDict;
            if (dict != null && dict.TryGetValue(JenkHash.GenHash(model.ToLowerInvariant()), out var fe) && fe != null)
            { entry = fe; return true; }
            return false;
        }

        private void AttachComponentTxd_V26(ModelViewer.CompRow row, AssetPreview pv)
        {
            var txd = row?.Txd;
            if (string.IsNullOrEmpty(txd) || pv == null) return;
            if (pv.AttachedYtds.Any(a => a.Name != null && a.Name.IndexOf(txd, StringComparison.OrdinalIgnoreCase) >= 0)) return;
            try
            {
                if (row.DiskPath != null)
                {
                    var side = Path.Combine(Path.GetDirectoryName(row.DiskPath) ?? ".", txd + ".ytd");
                    if (File.Exists(side))
                    {
                        var y = new YtdFile(); y.Load(File.ReadAllBytes(side));
                        if (y.TextureDict != null) { pv.AttachYtd(y, side); return; }
                    }
                }
                var dict = gameFiles?.Cache?.YtdDict;
                if (dict != null && dict.TryGetValue(JenkHash.GenHash(txd.ToLowerInvariant()), out var fe) && fe != null)
                {
                    var y = gameFiles.Cache.RpfMan.GetFile<YtdFile>(fe);
                    if (y?.TextureDict != null) pv.AttachYtd(y, "gta:" + fe.Path);
                }
            }
            catch { }
        }

        private void SeqTest_WeaponMeta_V26(Action<string, bool, string> check)
        {
            var man = gameFiles?.Cache?.RpfMan;
            if (man == null || !gameFiles.Ready) { Console.WriteLine("  v26 meta: (skipped - no game folder)"); return; }

            var meta = WeaponMetaFor_V26(null);
            check("v26 meta: the install's weapon metas parse", meta != null && meta.FilesRead > 0 && meta.Weapons.Count > 0,
                  meta == null ? "none" : $"{meta.FilesRead} file(s), {meta.Weapons.Count} weapon(s), {meta.Components.Count} component(s)");
            if (meta == null) return;

            var list = meta.For("w_ar_carbineriflemk2");
            check("v26 meta: the Mk2 carbine's components come from its attach points",
                  list.Count > 10 && list.Any(r => r.Component != null && r.Component.StartsWith("COMPONENT_AT_MUZZLE")),
                  $"{list.Count} component(s): " + string.Join(", ", list.Take(4).Select(r => r.Component)));

            var shared = list.FirstOrDefault(r => r.Component == "COMPONENT_AT_AR_FLSH");
            check("v26 meta: ...including the SHARED ones a by-name sweep beside the weapon can never see",
                  shared?.Model != null && !shared.Model.StartsWith("w_ar_carbineriflemk2", StringComparison.OrdinalIgnoreCase),
                  shared == null ? "COMPONENT_AT_AR_FLSH not listed" : $"{shared.Component} -> {shared.Model} on {shared.Bone}");
            if (shared?.Model != null)
                check("v26 meta: ...and that model is found in the archives",
                      FindComponentModel_V26(shared.Model, null, out var fe2, out _), fe2?.Path ?? "not found");

            check("v26 meta: each component knows the bone the weapon puts it on",
                  list.All(r => !string.IsNullOrEmpty(r.Bone)), "every row has a bone");
            check("v26 meta: weaponarchetypes.meta names the texture dictionaries",
                  meta.Txds.Count > 100 && meta.Txds.ContainsKey("W_AR_CarbineRifleMk2"), $"{meta.Txds.Count} named");

            var skel = LoadSkeletonOf_V26("w_ar_carbineriflemk2");
            if (skel != null)
            {
                var byMeta = Scene.SlotBoneOf_V26(skel, null, "WAPScop_2");
                check("v26 bones: the weapon's own attach-point name is used exactly", byMeta?.Name == "WAPScop_2", byMeta?.Name ?? "(none)");
                var loose = Scene.SlotBoneOf_V26(skel, "AAPFlshLasr", null);
                check("v26 bones: a slot with no exact bone still finds the one that carries its name",
                      loose?.Name != null && loose.Name.IndexOf("Flsh", StringComparison.OrdinalIgnoreCase) >= 0, loose?.Name ?? "(none)");
            }
        }

        private Skeleton LoadSkeletonOf_V26(string model)
        {
            var dict = gameFiles?.Cache?.YdrDict;
            if (dict == null || !dict.TryGetValue(JenkHash.GenHash(model), out var fe) || fe == null) return null;
            try { return gameFiles.Cache.RpfMan.GetFile<YdrFile>(fe)?.Drawable?.Skeleton; } catch { return null; }
        }
    }
}

