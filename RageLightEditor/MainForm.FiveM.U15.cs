using System;
using System.Collections.Generic;
using System.Text;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private class TrackedEntity_U15
        {
            public YmapEntityDef Entity;
            public int Id;
            public string Model;
            public int Hash;
            public Vector3 OrigPos;
            public Quaternion OrigRot;
            public bool IsCopy;
            public string Sent = "";
        }

        private readonly Dictionary<YmapEntityDef, TrackedEntity_U15> fivemEntities_U15 = new Dictionary<YmapEntityDef, TrackedEntity_U15>();
        private int fivemEntityIds_U15;
        private double fivemEntitiesAt_U15;
        private bool fivemEntitiesCleared_U15 = true;

        private static string ModelName_U15(YmapEntityDef e)
        {
            if (e == null) return "";
            var name = e.Archetype != null ? e.Archetype.Name.ToString() : e._CEntityDef.archetypeName.ToString();
            return (name ?? "").ToLowerInvariant();
        }

        private static int ModelHash_U15(YmapEntityDef e)
        {
            if (e == null) return 0;
            uint h = e.Archetype != null ? e.Archetype.Hash : e._CEntityDef.archetypeName.Hash;
            return unchecked((int)h);
        }

        public static bool EntityPresent_U15(YmapEntityDef e)
        {
            if (e == null) return false;
            if (e.MloParent != null)
            {
                var ents = e.MloParent.MloInstance?.Entities;
                return ents == null || Array.IndexOf(ents, e) >= 0;
            }
            if (e.Ymap != null)
            {
                var all = e.Ymap.AllEntities;
                return all == null || Array.IndexOf(all, e) >= 0;
            }
            return true;
        }

        public static bool IsUnsavedYmap_U15(YmapEntityDef e)
        {
            var y = e?.Ymap;
            if (y == null || e.MloParent != null) return false;
            if (y.RpfFileEntry != null) return false;
            return string.IsNullOrEmpty(y.FilePath) || !System.IO.File.Exists(y.FilePath);
        }

        private static bool LooksLikeCopy_U15(YmapEntityDef e)
        {
            if (IsUnsavedYmap_U15(e)) return true;
            YmapEntityDef[] siblings = e.MloParent != null ? e.MloParent.MloInstance?.Entities : e.Ymap?.AllEntities;
            if (siblings == null) return false;
            uint h = e.Archetype != null ? e.Archetype.Hash : e._CEntityDef.archetypeName.Hash;
            int n = 0;
            foreach (var s in siblings)
            {
                if (s == null) continue;
                uint sh = s.Archetype != null ? s.Archetype.Hash : s._CEntityDef.archetypeName.Hash;
                if (sh == h && (s.Position - e.Position).LengthSquared() < 1e-4f) n++;
            }
            return n >= 2;
        }

        public static bool SameRotation_U15(Quaternion a, Quaternion b)
        {
            float d = Math.Abs(Quaternion.Dot(Quaternion.Normalize(a), Quaternion.Normalize(b)));
            return d > 0.99999f;
        }

        public static string EntityJson_U15(int id, string model, int hash, bool isCopy, Vector3 origPos, Quaternion origRot, bool present, Vector3 pos, Quaternion rot, Vector3 scale)
        {
            var sb = new StringBuilder("{\"type\":\"entity\",\"id\":").Append(id)
                .Append(",\"model\":").Append(DccBridgeProtocol.S(model))
                .Append(",\"hash\":").Append(hash)
                .Append(",\"orig\":").Append(isCopy ? "null" : PlaceJson_U14(origPos, origRot, Vector3.One));
            if (!present) sb.Append(",\"place\":null,\"moved\":false");
            else
            {
                bool moved = isCopy || (pos - origPos).Length() > 0.005f || !SameRotation_U15(rot, origRot);
                sb.Append(",\"place\":").Append(PlaceJson_U14(pos, rot, scale)).Append(",\"moved\":").Append(moved ? "true" : "false");
            }
            return sb.Append('}').ToString();
        }

        private void Track_U15(YmapEntityDef e)
        {
            if (e == null || fivemEntities_U15.ContainsKey(e) || fivemEntities_U15.Count >= 500) return;
            fivemEntities_U15[e] = new TrackedEntity_U15
            {
                Entity = e,
                Id = ++fivemEntityIds_U15,
                Model = ModelName_U15(e),
                Hash = ModelHash_U15(e),
                OrigPos = e.Position,
                OrigRot = e.Orientation,
                IsCopy = LooksLikeCopy_U15(e),
            };
        }

        private void PushFiveMEntities_U15(double now)
        {
            if (!panel.WorldMode || WorldEdit == null) return;
            if (now - fivemEntitiesAt_U15 < 0.05) return;
            fivemEntitiesAt_U15 = now;
            foreach (var e in WorldEdit.AllSelected_V20()) Track_U15(e);
            foreach (var t in fivemEntities_U15.Values)
            {
                var e = t.Entity;
                var json = EntityJson_U15(t.Id, t.Model, t.Hash, t.IsCopy, t.OrigPos, t.OrigRot, EntityPresent_U15(e), e.Position, e.Orientation, e.Scale);
                if (json == t.Sent) continue;
                t.Sent = json;
                fivemEntitiesCleared_U15 = false;
                BroadcastFiveM_U12(json);
            }
        }

        private void PushFiveMOff_U15()
        {
            if (!fivemEntitiesCleared_U15) BroadcastFiveM_U12("{\"type\":\"entity\",\"clear\":true}");
            fivemEntitiesCleared_U15 = true;
            fivemEntities_U15.Clear();
        }

        private void ResetFiveMSent_U15()
        {
            foreach (var t in fivemEntities_U15.Values) t.Sent = "";
        }
    }
}
