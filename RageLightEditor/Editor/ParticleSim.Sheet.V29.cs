using System;

namespace RageLightEditor.Editor
{
    public partial class PtfxSimulator
    {
        public struct SheetFrameResult
        {
            public int Cell, Next;
            public float Blend;
        }

        public static SheetFrameResult SheetFrame_V29(int texMin, int texMax, int frames, bool animated,
                                                      int loopMode, bool holdLast, bool overLife, bool blend,
                                                      float lifeT, float age, float rate, uint seed, int total)
        {
            total = Math.Max(1, total);
            int lo = Math.Clamp(Math.Min(texMin, texMax), 0, total - 1);
            int hi = Math.Clamp(Math.Max(texMin, texMax), 0, total - 1);
            int window = hi - lo + 1;
            int start = lo + (int)((seed >> 7) % (uint)window);
            var r = new SheetFrameResult { Cell = start, Next = start, Blend = 0f };
            frames = Math.Max(1, frames);
            if (!animated || frames <= 1 && loopMode != 2) return r;

            float adv = overLife ? Math.Clamp(lifeT, 0f, 1f) * frames : age * Math.Max(0.01f, rate);
            if (!float.IsFinite(adv) || adv < 0f) adv = 0f;
            int step = (int)adv;
            float frac = adv - step;

            int Place(int s)
            {
                switch (loopMode)
                {
                    case 2:
                        return lo + ((start - lo + s) % window);
                    case 0:
                        {
                            int idx = start + s;
                            return Math.Min(idx, frames - 1);
                        }
                    default:
                        return (start + s) % frames;
                }
            }

            r.Cell = Math.Clamp(Place(step), 0, total - 1);
            r.Next = Math.Clamp(Place(step + 1), 0, total - 1);
            r.Blend = blend && r.Next != r.Cell ? Math.Clamp(frac, 0f, 1f) : 0f;
            _ = holdLast;
            return r;
        }
    }
}

