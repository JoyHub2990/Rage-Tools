using System;

namespace RageLightEditor.Editor
{
    public static class RotateSnapSteps_U5
    {
        public static float Up(float deg) => Clamp((float)Math.Floor(deg + 0.0001f) + 1.0f);

        public static float Down(float deg) => Clamp((float)Math.Ceiling(deg - 0.0001f) - 1.0f);

        public static float Clamp(float deg) => Math.Min(90f, Math.Max(0f, deg));

        public static string Text(float deg) => deg < 0.01f ? "no snap" : deg.ToString("0.#") + " deg";

        public static string Label(float deg) => deg < 0.01f ? "Snap: off" : "Snap: " + deg.ToString("0.#") + " deg";

        public static int SelfTest_U5(Action<string, bool, string> check)
        {
            int fails = 0;
            void Chk(string what, bool ok, string d) { if (!ok) fails++; check(what, ok, d); }

            Chk("u5 rotate snap: the plus button adds one degree",
                Math.Abs(Up(0f) - 1f) < 0.001f && Math.Abs(Up(5f) - 6f) < 0.001f &&
                Math.Abs(Up(6f) - 7f) < 0.001f && Math.Abs(Up(44f) - 45f) < 0.001f,
                $"0->{Up(0f)} 5->{Up(5f)} 6->{Up(6f)} 44->{Up(44f)}");

            Chk("u5 rotate snap: it stops at a right angle instead of running away",
                Math.Abs(Up(89f) - 90f) < 0.001f && Math.Abs(Up(90f) - 90f) < 0.001f,
                $"89->{Up(89f)} 90->{Up(90f)}");

            Chk("u5 rotate snap: the minus button takes one back off",
                Math.Abs(Down(90f) - 89f) < 0.001f && Math.Abs(Down(6f) - 5f) < 0.001f &&
                Math.Abs(Down(1f)) < 0.001f && Math.Abs(Down(0f)) < 0.001f,
                $"90->{Down(90f)} 6->{Down(6f)} 1->{Down(1f)} 0->{Down(0f)}");

            Chk("u5 rotate snap: a half degree from dragging lands on a whole one",
                Math.Abs(Up(22.5f) - 23f) < 0.001f && Math.Abs(Down(22.5f) - 22f) < 0.001f,
                $"22.5 up {Up(22.5f)}, down {Down(22.5f)}");

            Chk("u5 rotate snap: the toolbar says what the number is for",
                Label(0f) == "Snap: off" && Label(5f) == "Snap: 5 deg" && Label(22.5f) == "Snap: 22.5 deg",
                Label(0f) + " / " + Label(22.5f));

            Chk("u5 rotate snap: zero reads as off, not as zero degrees",
                Text(0f) == "no snap" && Text(22.5f) == "22.5 deg" && Text(15f) == "15 deg",
                Text(0f) + " / " + Text(22.5f));
            return fails;
        }
    }
}
