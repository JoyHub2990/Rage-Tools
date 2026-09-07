using System;

namespace RageLightEditor.Editor
{
    public static class SpaceNames
    {
        public static string NameOf(LightPanel.Space s)
        {
            switch (s)
            {
                case LightPanel.Space.Light: return "Light";
                case LightPanel.Space.Material: return "Material";
                case LightPanel.Space.Cinematic: return "Cinematic";
                case LightPanel.Space.Archive: return "Archive";
                case LightPanel.Space.World: return "World";
                case LightPanel.Space.Mlo: return "Mlo";
                case LightPanel.Space.Particles: return "Particles";
                case LightPanel.Space.NavMesh: return "NavMesh";
                case LightPanel.Space.Terrain: return "Terrain";
                case LightPanel.Space.Animation: return "Animation";
                case LightPanel.Space.Extension: return "Extension";
                default: return ((int)s).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        public static bool TryParse(string text, out LightPanel.Space space)
        {
            space = LightPanel.Space.World;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var t = text.Trim();

            foreach (LightPanel.Space s in Enum.GetValues(typeof(LightPanel.Space)))
                if (string.Equals(NameOf(s), t, StringComparison.OrdinalIgnoreCase)) { space = s; return true; }

            if (string.Equals(t, "lights", StringComparison.OrdinalIgnoreCase)) { space = LightPanel.Space.Light; return true; }
            if (string.Equals(t, "materials", StringComparison.OrdinalIgnoreCase)) { space = LightPanel.Space.Material; return true; }
            if (string.Equals(t, "rpf", StringComparison.OrdinalIgnoreCase)) { space = LightPanel.Space.Archive; return true; }
            if (string.Equals(t, "animations", StringComparison.OrdinalIgnoreCase)) { space = LightPanel.Space.Animation; return true; }
            if (string.Equals(t, "extensions", StringComparison.OrdinalIgnoreCase)) { space = LightPanel.Space.Extension; return true; }

            if (int.TryParse(t, out int n) && Enum.IsDefined(typeof(LightPanel.Space), n))
            { space = (LightPanel.Space)n; return true; }
            return false;
        }

        public static int SelfTest()
        {
            int fails = 0;
            foreach (LightPanel.Space s in Enum.GetValues(typeof(LightPanel.Space)))
            {
                var name = NameOf(s);
                bool named = !int.TryParse(name, out _);
                bool back = TryParse(name, out var r) && r == s;
                if (!named || !back)
                {
                    fails++;
                    Console.WriteLine($"  SPACENAME FAIL {(int)s} -> '{name}' named={named} roundTrip={back}");
                }
            }
            Console.WriteLine("SPACE NAMES self-test: " + (fails == 0 ? "PASSED" : fails + " failed"));
            return fails;
        }
    }
}

