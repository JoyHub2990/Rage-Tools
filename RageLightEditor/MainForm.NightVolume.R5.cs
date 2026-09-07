using System;
using System.Globalization;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private static readonly bool NoGameFog_R5 = Environment.GetEnvironmentVariable("RLE_NOGAMEFOG") == "1";
        private static readonly bool FogDebug_R5 = Environment.GetEnvironmentVariable("RLE_FOGDBG") == "1";
        private static readonly bool NoVolumes_R5 = Environment.GetEnvironmentVariable("RLE_NOVOLUMES") == "1";
        private bool fogDumped_R5;

        partial void FogProbe_R5();
        partial void SeqTest_Volume_R5(Action<string, bool, string> check);

        private static void ConeShape_R5(float halfAngleRad, float len, out float radius, out float axial)
        {
            if (LegacyConeShape_R5) { radius = (float)Math.Tan(halfAngleRad) * len; axial = len; return; }
            radius = (float)Math.Sin(halfAngleRad) * len;
            axial = Math.Max((float)Math.Cos(halfAngleRad), 0.15f) * len;
            float rim = (float)Math.Sqrt(radius * radius + axial * axial);
            if (rim > len && rim > 1e-4f) { float k = len / rim; radius *= k; axial *= k; }
        }

        private static readonly bool LegacyConeShape_R5 = Environment.GetEnvironmentVariable("RLE_VOLLEGACY_R5") == "1";

        partial void SeqTest_Volume_R5(Action<string, bool, string> check)
        {
            if (LegacyConeShape_R5)
            {
                check("r5 volume: RLE_VOLLEGACY_R5 restores the old cone", true, "A/B mode");
                return;
            }
            ConeShape_R5(1.53f, 4.0f, out float r90, out float a90);
            float rim90 = (float)Math.Sqrt(r90 * r90 + a90 * a90);
            check("r5 volume: a 90-degree lamp's shaft stays inside its 4 m reach",
                  r90 <= 4.01f && rim90 <= 4.05f, $"radius {r90:0.00} m axial {a90:0.00} m rim {rim90:0.00} m (was 98.0 m radius)");

            float half = 20.0f * 0.0174533f;
            ConeShape_R5(half, 10.0f, out float r20, out float a20);
            float old20 = (float)Math.Tan(half) * 10.0f;
            check("r5 volume: a 20-degree shaft is within 10% of the shape it always had",
                  Math.Abs(r20 - old20) / old20 < 0.10f && a20 > 9.0f, $"radius {r20:0.000} was {old20:0.000}, axial {a20:0.00}");

            for (float d = 5.0f; d < 88.0f; d += 20.0f)
            {
                float h = d * 0.0174533f;
                ConeShape_R5(h, 12.0f, out float rr, out float aa);
                float rim = (float)Math.Sqrt(rr * rr + aa * aa);
                check($"r5 volume: a {d:0}-degree shaft's rim is at the light's reach", rim <= 12.05f, $"rim {rim:0.000} m of 12");
            }
        }

        private static readonly bool VolumeDebug_R5 = Environment.GetEnvironmentVariable("RLE_VOLDBG") == "1";
        private bool volDumped_R5;

        private void VolumeDump_R5(System.Collections.Generic.List<Editor.Scene.VolumeDraw> list)
        {
            if (!VolumeDebug_R5 || volDumped_R5 || list == null || screenshotFrames != 1) return;
            volDumped_R5 = true;
            var ci = CultureInfo.InvariantCulture;
            var cam = camera.Position;
            var sorted = new System.Collections.Generic.List<Editor.Scene.VolumeDraw>(list);
            sorted.Sort((a, b) => Vector3.DistanceSquared(cam, a.Pos).CompareTo(Vector3.DistanceSquared(cam, b.Pos)));
            Console.WriteLine($"VOLDBG {list.Count} volumes to draw from {cam}");
            for (int i = 0; i < sorted.Count && i < 20; i++)
            {
                var v = sorted[i];
                float len = Math.Max(v.Falloff * v.SizeScale, 0.05f);
                float a0 = MathUtil.Clamp(0.22f * v.Intensity, 0.02f, 0.5f);
                Console.WriteLine($"  {Vector3.Distance(cam, v.Pos),7:0.0} m type {v.Type} falloff {v.Falloff.ToString("0.0", ci)} " +
                                  $"sizeScale {v.SizeScale.ToString("0.00", ci)} len {len.ToString("0.0", ci)} " +
                                  $"volInten {v.Intensity.ToString("0.000", ci)} alpha {a0.ToString("0.000", ci)} " +
                                  $"halfAngle {(v.OuterAngleRad * 57.2958f).ToString("0.0", ci)} deg -> radius {((float)Math.Tan(MathUtil.Clamp(v.OuterAngleRad, 0.03f, 1.53f)) * len).ToString("0.0", ci)} m " +
                                  $"colour {v.Colour} at {v.Pos}");
            }
            float sumFar = 0.0f; int far = 0;
            foreach (var v in list)
            {
                float len = Math.Max(v.Falloff * v.SizeScale, 0.05f);
                float d = Vector3.Distance(cam, v.Pos);
                if (d > len * 3.0f) { far++; sumFar += MathUtil.Clamp(0.22f * v.Intensity, 0.02f, 0.5f); }
            }
            Console.WriteLine($"  VOLDBG {far} of {list.Count} volumes are further than 3x their own length from the eye, alpha sum {sumFar.ToString("0.00", ci)}");
        }

        partial void FogProbe_R5()
        {
            if (!FogDebug_R5 || fogDumped_R5) return;
            fogDumped_R5 = true;
            var f = sceneRenderer.GameFog;
            var ci = CultureInfo.InvariantCulture;
            var nray = camera.GetForward();
            Console.WriteLine($"FOGDBG cam {camera.Position} looking {nray} enabled {f.Params0.W}");
            Console.WriteLine($"  moonDir {f.MoonDirAndPower} pow {f.MoonDirAndPower.W.ToString("0.00", ci)} " +
                              $"dot {Vector3.Dot(nray, new Vector3(f.MoonDirAndPower.X, f.MoonDirAndPower.Y, f.MoonDirAndPower.Z)).ToString("0.000", ci)} " +
                              $"moonAmount {MoonAmount_R5(f, nray).ToString("0.0000", ci)} colMoon {f.ColMoon}");
            foreach (float d in new[] { 50.0f, 100.0f, 250.0f, 600.0f, 1500.0f })
            {
                var ray = nray * d;
                FogAt_R5(f, ray, out float ground, out float haze, out var col);
                Console.WriteLine($"  at {d,6:0} m: ground {ground.ToString("0.000", ci)} haze {haze.ToString("0.000", ci)} " +
                                  $"blend {Math.Min(1.0f, ground + haze).ToString("0.000", ci)} colour {col}");
            }
        }

        private static float MoonAmount_R5(in GameFogVars f, Vector3 nray)
        {
            float d = Math.Max(0.0f, Vector3.Dot(Vector3.Normalize(nray), new Vector3(f.MoonDirAndPower.X, f.MoonDirAndPower.Y, f.MoonDirAndPower.Z)));
            return (float)Math.Pow(d, Math.Max(f.MoonDirAndPower.W, 0.01f));
        }

        private static void FogAt_R5(in GameFogVars f, Vector3 eyeRayToPoint, out float groundFog, out float haze, out Vector3 colour)
        {
            groundFog = haze = 0.0f; colour = Vector3.Zero;
            if (f.Params0.W < 0.5f) return;
            float full = eyeRayToPoint.Length();
            float dist = Math.Max(0.0f, full - f.Params0.X);
            float deltaZ = eyeRayToPoint.Z * (dist / Math.Max(full, 1e-4f));
            float t = f.Params2.Z * deltaZ;
            float fogInt = (Math.Abs(deltaZ) > 0.01f) ? (1.0f - (float)Math.Exp(-t)) / t : 1.0f;
            float val = Math.Min(1.0f, f.Params1.W * dist * fogInt);
            groundFog = (1.0f - MathUtil.Clamp((float)Math.Exp(val), 0.0f, 1.0f)) * f.Params2.Y;

            var nray = Vector3.Normalize(eyeRayToPoint);
            float moonAmount = (float)Math.Pow(Math.Max(0.0f, Vector3.Dot(nray, new Vector3(f.MoonDirAndPower.X, f.MoonDirAndPower.Y, f.MoonDirAndPower.Z))), Math.Max(f.MoonDirAndPower.W, 0.01f));
            float sunAmount = (float)Math.Pow(Math.Max(0.0f, Vector3.Dot(nray, new Vector3(f.SunDirAndPower.X, f.SunDirAndPower.Y, f.SunDirAndPower.Z))), Math.Max(f.SunDirAndPower.W, 0.01f));
            float hazeBlend = f.Params1.Y * (1.0f - groundFog);
            haze = hazeBlend * (1.0f - (float)Math.Exp(f.Params1.X * Math.Max(0.0f, dist - f.Params2.X)));
            float atmoBlend = 1.0f - (float)Math.Exp(-f.Params1.Z * dist);
            var atmoMoon = Vector3.Lerp(V3(f.ColAtmosphere), V3(f.ColMoon), moonAmount);
            var atmo = Vector3.Lerp(atmoMoon, V3(f.ColSun), sunAmount);
            var groundAtmo = Vector3.Lerp(V3(f.ColGround), atmo, atmoBlend);
            colour = Vector3.Lerp(groundAtmo, V3(f.ColHaze), hazeBlend);
        }

        private static Vector3 V3(Vector4 v) => new Vector3(v.X, v.Y, v.Z);
    }
}

