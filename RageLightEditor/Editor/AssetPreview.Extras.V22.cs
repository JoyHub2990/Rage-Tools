using System;
using System.Collections.Generic;
using System.Linq;
using CodeWalker.GameFiles;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor.Editor
{
    public partial class AssetPreview
    {
        public class Attachment
        {
            public string Name;
            public string Bone;
            public RenderModel Model;
            public string MetaBone;
        }

        public readonly List<(string Name, YtdFile Ytd)> AttachedYtds = new List<(string, YtdFile)>();

        public readonly List<Attachment> Attachments = new List<Attachment>();

        public string[] MissingTextures { get; private set; } = Array.Empty<string>();

        private Func<RenderModel> lastBuild_V22;
        private YtdFile lastArchDict_V22;

        public bool IsWeapon => Scene.IsWeaponSkeleton_V22(Drawable?.Skeleton);

        public RenderModel[] RenderList_V22()
        {
            var list = new List<RenderModel>(1 + Attachments.Count);
            if (Model != null && Model.Meshes.Count > 0) list.Add(Model);
            foreach (var a in Attachments)
                if (a?.Model != null && a.Model.Meshes.Count > 0) list.Add(a.Model);
            return list.ToArray();
        }

        public bool AttachYtd(YtdFile ytd, string name)
        {
            if (ytd?.TextureDict == null) return false;
            if (AttachedYtds.Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase) || ReferenceEquals(a.Ytd, ytd)))
                return false;
            AttachedYtds.Add((name, ytd));
            Rebuild_V22();
            return true;
        }

        public bool DetachYtd(int index)
        {
            if (index < 0 || index >= AttachedYtds.Count) return false;
            ForgetYtdMute_U6(AttachedYtds[index].Name);
            AttachedYtds.RemoveAt(index);
            Rebuild_V22();
            return true;
        }

        public void Rebuild_V22()
        {
            if (lastBuild_V22 == null) return;
            DisposeModel();
            Build(lastArchDict_V22, lastBuild_V22);
            var again = Attachments.Select(a => (a.Name, a.MetaBone, Ydr: a.Model?.SourceYdr_V22)).ToList();
            foreach (var a in Attachments) a.Model?.Dispose();
            Attachments.Clear();
            foreach (var (name, metaBone, ydr) in again)
                if (ydr != null) AttachComponent(ydr, name, metaBone, out _);
        }

        public string AttachComponent(YdrFile comp, string name, out string why) => AttachComponent(comp, name, null, out why);

        public string AttachComponent(YdrFile comp, string name, string metaBone, out string why)
        {
            why = null;
            var skel = Drawable?.Skeleton;
            if (skel?.Bones?.Items == null) { why = "the open model has no skeleton"; return null; }
            var cd = comp?.Drawable;
            if (cd == null) { why = "the component would not read"; return null; }

            var aap = Scene.AapBoneOf_V21(cd.Skeleton);
            var bone = Scene.SlotBoneOf_V26(skel, aap?.Name, metaBone);
            if (bone == null) { why = "the weapon has no bone to hang this on"; return null; }
            var place = Scene.AttachMatrixOf_V22(bone, cd.Skeleton);

            RenderModel model = null;
            lock (ModelBuildLock)
            {
                var dicts = new List<TextureDictionary>();
                try
                {
                    if (lastArchDict_V22?.TextureDict != null && !builder.ExternalTextureDicts.Contains(lastArchDict_V22.TextureDict))
                    { builder.ExternalTextureDicts.Add(lastArchDict_V22.TextureDict); dicts.Add(lastArchDict_V22.TextureDict); }
                    foreach (var (_, y) in AttachedYtds)
                        if (y?.TextureDict != null && !builder.ExternalTextureDicts.Contains(y.TextureDict))
                        { builder.ExternalTextureDicts.Add(y.TextureDict); dicts.Add(y.TextureDict); }
                    builder.TextureContext = TxdContext;
                    builder.DiffuseRemap_V23 = VariantRemap_V23();
                    model = builder.BuildFromYdr(comp, place);
                }
                catch (Exception ex) { why = "the component's model would not build: " + ex.Message; }
                finally
                {
                    builder.TextureContext = 0;
                    builder.DiffuseRemap_V23 = null;
                    foreach (var d in dicts) builder.ExternalTextureDicts.Remove(d);
                }
            }
            if (model == null) { why ??= "the component's model would not build"; return null; }
            model.SourceYdr_V22 = comp;
            Attachments.Add(new Attachment { Name = name, Bone = bone.Name, Model = model, MetaBone = metaBone });
            return bone.Name;
        }

        public bool DetachComponent(int index)
        {
            if (index < 0 || index >= Attachments.Count) return false;
            Attachments[index].Model?.Dispose();
            Attachments.RemoveAt(index);
            return true;
        }

        private void ClearExtras_V22()
        {
            foreach (var a in Attachments) a.Model?.Dispose();
            Attachments.Clear();
            AttachedYtds.Clear();
            ForgetAllYtdMutes_U6();
            MissingTextures = Array.Empty<string>();
            lastBuild_V22 = null;
            lastArchDict_V22 = null;
        }
    }
}

