using System;
using System.Collections.Generic;

namespace RageLightEditor.Editor
{
    public partial class AssetPreview
    {
        private readonly HashSet<string> mutedYtds_U6 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public bool IsYtdMuted_U6(string name) => name != null && mutedYtds_U6.Contains(name);

        public void SetYtdMuted_U6(string name, bool muted)
        {
            if (string.IsNullOrEmpty(name)) return;
            bool changed = muted ? mutedYtds_U6.Add(name) : mutedYtds_U6.Remove(name);
            if (changed) Rebuild_V22();
        }

        public void UnmuteAllYtds_U6()
        {
            if (mutedYtds_U6.Count == 0) return;
            mutedYtds_U6.Clear();
            Rebuild_V22();
        }

        public int SoloYtdIndex_U6()
        {
            int solo = -1;
            for (int i = 0; i < AttachedYtds.Count; i++)
            {
                if (IsYtdMuted_U6(AttachedYtds[i].Name)) continue;
                if (solo >= 0) return -1;
                solo = i;
            }
            return solo;
        }

        public int StepYtdSolo_U6(int dir)
        {
            int n = AttachedYtds.Count;
            if (n == 0) return -1;
            int at = SoloYtdIndex_U6();
            int next = at < 0
                ? (dir < 0 ? n - 1 : 0)
                : (at + (dir < 0 ? -1 : 1) + n) % n;
            mutedYtds_U6.Clear();
            for (int i = 0; i < n; i++)
                if (i != next) mutedYtds_U6.Add(AttachedYtds[i].Name);
            Rebuild_V22();
            return next;
        }

        internal void ForgetYtdMute_U6(string name) => mutedYtds_U6.Remove(name);

        internal void ForgetAllYtdMutes_U6() => mutedYtds_U6.Clear();

        public static int SelfTestYtdSolo_U6(Action<string, bool, string> check)
        {
            int fails = 0;
            void Chk(string what, bool ok, string d) { if (!ok) fails++; check(what, ok, d); }
            var p = new AssetPreview(null, null, null);
            p.AttachedYtds.Add(("dog_a.ytd", null));
            p.AttachedYtds.Add(("dog_b.ytd", null));
            p.AttachedYtds.Add(("dog_c.ytd", null));

            Chk("u6 dicts: with everything on there is no solo",
                p.SoloYtdIndex_U6() == -1 && !p.IsYtdMuted_U6("dog_a.ytd"), "all on");

            int at = p.StepYtdSolo_U6(+1);
            Chk("u6 dicts: the first step solos the first dictionary",
                at == 0 && p.SoloYtdIndex_U6() == 0 &&
                !p.IsYtdMuted_U6("dog_a.ytd") && p.IsYtdMuted_U6("dog_b.ytd") && p.IsYtdMuted_U6("dog_c.ytd"),
                "solo " + at);

            at = p.StepYtdSolo_U6(+1);
            Chk("u6 dicts: the next step moves to the second", at == 1 && p.SoloYtdIndex_U6() == 1, "solo " + at);

            at = p.StepYtdSolo_U6(-1);
            at = p.StepYtdSolo_U6(-1);
            Chk("u6 dicts: stepping back wraps round to the end",
                at == 2 && p.SoloYtdIndex_U6() == 2, "solo " + at);

            p.UnmuteAllYtds_U6();
            Chk("u6 dicts: All on brings every dictionary back",
                p.SoloYtdIndex_U6() == -1 && !p.IsYtdMuted_U6("dog_b.ytd"), "all on again");

            p.SetYtdMuted_U6("dog_b.ytd", true);
            Chk("u6 dicts: a single dictionary can be turned off on its own",
                p.IsYtdMuted_U6("dog_b.ytd") && p.SoloYtdIndex_U6() == -1, "b off, two left on");
            p.AttachedYtds.Clear();
            p.ForgetAllYtdMutes_U6();
            return fails;
        }
    }
}
