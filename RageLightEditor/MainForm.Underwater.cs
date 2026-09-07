using System;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private byte[] waterFogPixels;
        private int waterFogW, waterFogH;
        private bool waterFogTried;

        public bool CameraUnderwater { get; private set; }

        private void UpdateUnderwater_H3(Camera cam, double now)
        {
            CameraUnderwater = false;
            var v = postFx.Vars;
            v.UnderwaterParams = Vector4.Zero;
            postFx.Vars = v;
            if (!panel.WorldMode || worldWater == null || !worldWater.Ready || cam == null) return;
            if (Environment.GetEnvironmentVariable("RLE_NOUNDERWATER") == "1") return;

            var p = cam.Position;
            var h = worldWater.HeightAt(p.X, p.Y, p.Z);
            if (!h.HasValue || p.Z >= h.Value) return;
            CameraUnderwater = true;
            float below = h.Value - p.Z;

            Vector3 fogRgb = new Vector3(0.23f, 0.48f, 0.50f);
            if (!waterFogTried) LoadWaterFogPixels();
            if (waterFogPixels != null)
            {
                float u = (p.X + 4000.0f) / 8500.0f, vv = 1.0f - (p.Y + 4000.0f) / 12000.0f;
                int x = Math.Clamp((int)(u * waterFogW), 0, waterFogW - 1), y = Math.Clamp((int)(vv * waterFogH), 0, waterFogH - 1);
                int i = (y * waterFogW + x) * 4;
                fogRgb = new Vector3(waterFogPixels[i + 2] / 255.0f, waterFogPixels[i + 1] / 255.0f, waterFogPixels[i] / 255.0f);
            }
            Vector3 fogLight;
            float sunUp = 0.0f;
            Vector3 sunDir = Vector3.UnitZ;
            var gl = sceneRenderer.GlobalLight;
            if (gl.HasValue)
            {
                var g = gl.Value;
                sunDir = g.LightDir.LengthSquared() > 0.001f ? Vector3.Normalize(g.LightDir) : Vector3.UnitZ;
                sunUp = Math.Max(0.0f, sunDir.Z);
                fogLight = new Vector3(g.LightDirColour.X, g.LightDirColour.Y, g.LightDirColour.Z) * sunUp
                         + new Vector3(g.NaturalAmbUp.X + g.NaturalAmbDown.X, g.NaturalAmbUp.Y + g.NaturalAmbDown.Y, g.NaturalAmbUp.Z + g.NaturalAmbDown.Z)
                         + new Vector3(g.LightDirAmbColour.X, g.LightDirAmbColour.Y, g.LightDirAmbColour.Z) * sunUp;
            }
            else fogLight = sceneRenderer.AmbientColour * 6.0f;
            float intensity = Math.Abs(sceneRenderer.Water?.FogLightIntensity ?? 0.9f);
            var col = fogRgb * fogRgb * fogLight * intensity;
            col *= (float)Math.Exp(-below * 0.06);

            const float seaOpacity = 0.0104f * 0.35f;

            var pm = cam.ProjMatrix;
            v = postFx.Vars;
            v.UnderwaterParams = new Vector4(1.0f, seaOpacity, below, (float)now);
            v.UnderwaterColour = new Vector4(col, 1.0f);
            v.UnderwaterProj = new Vector4(pm.M33, pm.M43, 1.0f / Math.Max(Math.Abs(pm.M11), 1e-4f), 1.0f / Math.Max(Math.Abs(pm.M22), 1e-4f));
            var view = cam.ViewMatrix;
            var sunView = Vector3.TransformNormal(sunDir, view);
            sunView = new Vector3(sunView.X, sunView.Y, -sunView.Z);
            v.UnderwaterSun = new Vector4(sunView, sunUp);
            var upView = Vector3.TransformNormal(Vector3.UnitZ, view);
            v.UnderwaterUp = new Vector4(upView.X, upView.Y, -upView.Z, 0.0f);
            postFx.Vars = v;
        }

        private void LoadWaterFogPixels()
        {
            waterFogTried = true;
            try
            {
                if (gameFiles == null) { waterFogTried = false; return; }
                var tf = gameFiles.FindTexture(4047019542, 3154743001);
                if (tf == null) return;
                var px = CodeWalker.Utils.DDSIO.GetPixels(tf, 0);
                if (px != null && px.Length >= tf.Width * tf.Height * 4)
                {
                    waterFogPixels = px; waterFogW = tf.Width; waterFogH = tf.Height;
                }
            }
            catch (Exception ex) { Console.WriteLine("UNDERWATER fog map: " + ex.Message); }
        }
    }
}

