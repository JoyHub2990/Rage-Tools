using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using CodeWalker.GameFiles;

namespace RageLightEditor.Editor
{
    public class WeaponMeta_V26
    {
        public class AttachPoint { public string Bone; public string Component; public bool Default; }
        public class WeaponDef { public string Name; public string Model; public string Source; public readonly List<AttachPoint> Attach = new List<AttachPoint>(); }
        public class ComponentDef { public string Name; public string Model; public string OwnBone; public string Source; }

        public readonly Dictionary<string, WeaponDef> Weapons = new Dictionary<string, WeaponDef>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, ComponentDef> Components = new Dictionary<string, ComponentDef>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> Txds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public int FilesRead, FilesFailed;
        public readonly List<string> Problems = new List<string>();

        public static bool IsWeaponMetaName(string nameLower)
        {
            if (nameLower == null || !nameLower.EndsWith(".meta")) return false;
            if (nameLower.IndexOf("weapon") < 0) return false;
            if (nameLower.Contains("animation") || nameLower.Contains("targetsequence") || nameLower.StartsWith("shop_")) return false;
            return true;
        }

        public static WeaponMeta_V26 FromArchives(RpfManager man)
        {
            var m = new WeaponMeta_V26();
            if (man?.AllRpfs == null) return m;
            foreach (var rpf in man.AllRpfs)
            {
                if (rpf?.AllEntries == null) continue;
                foreach (var e in rpf.AllEntries)
                {
                    if (!(e is RpfFileEntry fe)) continue;
                    var n = fe.NameLower ?? fe.Name?.ToLowerInvariant();
                    if (!IsWeaponMetaName(n)) continue;
                    byte[] data = null;
                    try { data = fe.File?.ExtractFile(fe); } catch { }
                    if (data == null) { m.FilesFailed++; continue; }
                    m.ParseXml(data, fe.Path);
                }
            }
            return m;
        }

        public void AddDiskTree(string fileDir, int parentsUp = 3)
        {
            if (string.IsNullOrEmpty(fileDir) || !Directory.Exists(fileDir)) return;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string dir = fileDir;
            for (int up = 0; up <= parentsUp && dir != null; up++)
            {
                try
                {
                    foreach (var f in Directory.EnumerateFiles(dir, "*.meta", SearchOption.AllDirectories))
                    {
                        if (!IsWeaponMetaName(Path.GetFileName(f).ToLowerInvariant())) continue;
                        if (!seen.Add(f)) continue;
                        try { ParseXml(File.ReadAllBytes(f), f); }
                        catch (Exception ex) { FilesFailed++; Problems.Add(Path.GetFileName(f) + ": " + ex.Message); }
                    }
                }
                catch { }
                dir = Path.GetDirectoryName(dir);
            }
        }

        public void ParseXml(byte[] data, string source)
        {
            if (data == null || data.Length < 4) return;
            if ((data[0] == (byte)'P' && data[1] == (byte)'S' && data[2] == (byte)'I') || (data[0] == (byte)'R' && data[1] == (byte)'S' && data[2] == (byte)'C'))
            { FilesFailed++; Problems.Add(Path.GetFileName(source) + ": binary meta, not read"); return; }
            var doc = new XmlDocument();
            try { doc.LoadXml(Encoding.UTF8.GetString(data).TrimStart('﻿')); }
            catch (Exception ex) { FilesFailed++; Problems.Add(Path.GetFileName(source) + ": " + ex.Message); return; }
            FilesRead++;

            foreach (XmlNode item in doc.SelectNodes("//Item[@type]"))
            {
                var type = item.Attributes?["type"]?.Value ?? "";
                if (type.Equals("CWeaponInfo", StringComparison.OrdinalIgnoreCase))
                {
                    var model = item.SelectSingleNode("Model")?.InnerText?.Trim();
                    if (string.IsNullOrEmpty(model)) continue;
                    var w = new WeaponDef { Name = item.SelectSingleNode("Name")?.InnerText?.Trim(), Model = model, Source = source };
                    foreach (XmlNode ap in item.SelectNodes("AttachPoints/Item"))
                    {
                        var bone = ap.SelectSingleNode("AttachBone")?.InnerText?.Trim();
                        foreach (XmlNode comp in ap.SelectNodes("Components/Item"))
                        {
                            var cn = comp.SelectSingleNode("Name")?.InnerText?.Trim();
                            if (string.IsNullOrEmpty(cn)) continue;
                            var def = comp.SelectSingleNode("Default");
                            bool isDefault = def != null && string.Equals(def.Attributes?["value"]?.Value ?? def.InnerText, "true", StringComparison.OrdinalIgnoreCase);
                            w.Attach.Add(new AttachPoint { Bone = bone, Component = cn, Default = isDefault });
                        }
                    }
                    Weapons[model] = w;
                }
                else if (type.StartsWith("CWeaponComponent", StringComparison.OrdinalIgnoreCase))
                {
                    var name = item.SelectSingleNode("Name")?.InnerText?.Trim();
                    var model = item.SelectSingleNode("Model")?.InnerText?.Trim();
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(model)) continue;
                    Components[name] = new ComponentDef { Name = name, Model = model, OwnBone = item.SelectSingleNode("AttachBone")?.InnerText?.Trim(), Source = source };
                }
            }
            foreach (XmlNode item in doc.SelectNodes("//Item[modelName and txdName]"))
            {
                var model = item.SelectSingleNode("modelName")?.InnerText?.Trim();
                var txd = item.SelectSingleNode("txdName")?.InnerText?.Trim();
                if (!string.IsNullOrEmpty(model) && !string.IsNullOrEmpty(txd)) Txds[model] = txd;
            }
        }

        public class Resolved { public string Bone; public string Component; public string Model; public string Txd; public bool Default; }

        public List<Resolved> For(string weaponModel)
        {
            var list = new List<Resolved>();
            if (string.IsNullOrEmpty(weaponModel) || !Weapons.TryGetValue(weaponModel, out var w)) return list;
            foreach (var ap in w.Attach)
            {
                if (!Components.TryGetValue(ap.Component, out var comp)) { list.Add(new Resolved { Bone = ap.Bone, Component = ap.Component, Model = null, Default = ap.Default }); continue; }
                Txds.TryGetValue(comp.Model, out var txd);
                list.Add(new Resolved { Bone = ap.Bone, Component = ap.Component, Model = comp.Model, Txd = txd, Default = ap.Default });
            }
            return list;
        }

        public bool Knows(string weaponModel) => !string.IsNullOrEmpty(weaponModel) && Weapons.ContainsKey(weaponModel);
    }
}

