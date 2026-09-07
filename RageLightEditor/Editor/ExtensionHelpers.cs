using System;
using System.Collections.Generic;
using CodeWalker.GameFiles;
using RageLightEditor.Rendering;
using SharpDX;
using CodeWalker;

namespace RageLightEditor.Editor
{
    public static partial class ExtensionHelpers
    {
        public const float DefaultShaftRange = 250.0f;

        public static void AddLightShaft(TriRenderer tri, in CExtensionDefLightShaft ls, Vector3 pos, Quaternion ori,
                                         Vector3 camPos, float sunUp, float userIntensity)
        {
            var dir = ls.direction;
            if (dir.LengthSquared() < 1e-8f) dir = -Vector3.UnitZ; else dir.Normalize();
            float len = Math.Max(ls.length, 0.05f);
            Vector3 W(Vector3 v) => ori.Multiply(v) + pos;
            var a0 = W(ls.cornerA); var b0 = W(ls.cornerB); var c0 = W(ls.cornerC); var d0 = W(ls.cornerD);
            var wdir = ori.Multiply(dir); if (wdir.LengthSquared() > 1e-8f) wdir.Normalize();
            var ext = wdir * len;
            var a1 = a0 + ext; var b1 = b0 + ext; var c1 = c0 + ext; var d1 = d0 + ext;

            float r = ((ls.color >> 16) & 255) / 255.0f, g = ((ls.color >> 8) & 255) / 255.0f, b = (ls.color & 255) / 255.0f;
            float scale = ls.scaleBySunIntensity != 0 ? MathUtil.Lerp(0.15f, 1.0f, sunUp) : 1.0f;
            float inten = Math.Clamp(ls.intensity, 0.0f, 8.0f) * scale * userIntensity;
            float centreDist = Vector3.Distance(camPos, (a0 + b0 + c0 + d0) * 0.25f + ext * 0.5f);
            float fs = ls.fadeDistanceStart, fe = ls.fadeDistanceEnd;
            if (fe <= fs + 0.01f) { fs = DefaultShaftRange * 0.7f; fe = DefaultShaftRange; }
            float distFade = 1.0f - MathUtil.Clamp((centreDist - fs) / Math.Max(fe - fs, 0.01f), 0.0f, 1.0f);
            if (distFade <= 0.001f || inten <= 0.001f) return;
            float spread = 1.0f + Math.Clamp(ls.softness, 0.0f, 1.0f) * 0.25f;
            var centre1 = (a1 + b1 + c1 + d1) * 0.25f;
            a1 = centre1 + (a1 - centre1) * spread; b1 = centre1 + (b1 - centre1) * spread;
            c1 = centre1 + (c1 - centre1) * spread; d1 = centre1 + (d1 - centre1) * spread;

            float aNear = Math.Clamp(0.10f * inten, 0.02f, 0.45f) * distFade;
            var col0 = new Vector4(r * inten, g * inten, b * inten, aNear);
            var col1 = new Vector4(r * inten, g * inten, b * inten, 0.0f);
            void Side(Vector3 p0, Vector3 p1, Vector3 q1, Vector3 q0)
            {
                tri.AddTri(p0, p1, q1, col0, col0, col1);
                tri.AddTri(p0, q1, q0, col0, col1, col1);
                tri.AddTri(p0, q1, p1, col0, col1, col0);
                tri.AddTri(p0, q0, q1, col0, col1, col1);
            }
            Side(a0, b0, b1, a1); Side(b0, c0, c1, b1); Side(c0, d0, d1, c1); Side(d0, a0, a1, d1);
            var capCol = new Vector4(col0.X, col0.Y, col0.Z, col0.W * 0.6f);
            tri.AddTri(a0, b0, c0, capCol); tri.AddTri(a0, c0, d0, capCol);
            tri.AddTri(a0, c0, b0, capCol); tri.AddTri(a0, d0, c0, capCol);
        }

        public static Vector4 MarkerColour(MetaWrapper ext)
        {
            switch (ext)
            {
                case MCExtensionDefParticleEffect _: return new Vector4(1.0f, 0.55f, 0.15f, 0.55f);
                case MCExtensionDefSpawnPoint _: return new Vector4(0.3f, 1.0f, 0.4f, 0.55f);
                case MCExtensionDefAudioEmitter _:
                case MCExtensionDefAudioCollisionSettings _: return new Vector4(0.3f, 0.75f, 1.0f, 0.55f);
                case MCExtensionDefDoor _: return new Vector4(0.9f, 0.85f, 0.3f, 0.55f);
                case MCExtensionDefLadder _: return new Vector4(0.8f, 0.5f, 1.0f, 0.55f);
                case MCExtensionDefBuoyancy _: return new Vector4(0.2f, 0.9f, 0.9f, 0.55f);
                case MCExtensionDefProcObject _: return new Vector4(0.6f, 0.9f, 0.3f, 0.55f);
                case MCExtensionDefExplosionEffect _: return new Vector4(1.0f, 0.3f, 0.2f, 0.55f);
                case MCExtensionDefExpression _: return new Vector4(0.9f, 0.9f, 0.9f, 0.55f);
                case MCExtensionDefWindDisturbance _: return new Vector4(0.7f, 0.8f, 1.0f, 0.55f);
                default: return new Vector4(0.8f, 0.8f, 0.8f, 0.45f);
            }
        }

        public static bool TryGetOffset(MetaWrapper ext, out Vector3 offset)
        {
            offset = Vector3.Zero;
            switch (ext)
            {
                case MCExtensionDefParticleEffect pe: offset = pe.Data.offsetPosition; return true;
                case MCExtensionDefAudioCollisionSettings acs: offset = acs.Data.offsetPosition; return true;
                case MCExtensionDefAudioEmitter ae: offset = ae.Data.offsetPosition; return true;
                case MCExtensionDefSpawnPoint sp: offset = sp.Data.offsetPosition; return true;
                case MCExtensionDefExplosionEffect ee: offset = ee.Data.offsetPosition; return true;
                case MCExtensionDefLadder ld: offset = ld.Data.offsetPosition; return true;
                case MCExtensionDefBuoyancy bu: offset = bu.Data.offsetPosition; return true;
                case MCExtensionDefExpression ex: offset = ex.Data.offsetPosition; return true;
                case MCExtensionDefDoor dr: offset = dr.Data.offsetPosition; return true;
                case MCExtensionDefWindDisturbance wd: offset = wd.Data.offsetPosition; return true;
                case MCExtensionDefProcObject po: offset = po.Data.offsetPosition; return true;
                case MCExtensionDefLightShaft ls: offset = ls.Data.offsetPosition; return true;
                default: return false;
            }
        }

        public static void AddMarkerBox(TriRenderer tri, Vector3 centre, float half, Vector4 col)
        {
            var x = Vector3.UnitX * half; var y = Vector3.UnitY * half; var z = Vector3.UnitZ * half;
            Vector3 p000 = centre - x - y - z, p100 = centre + x - y - z, p010 = centre - x + y - z, p110 = centre + x + y - z;
            Vector3 p001 = centre - x - y + z, p101 = centre + x - y + z, p011 = centre - x + y + z, p111 = centre + x + y + z;
            tri.AddQuad(p000, p100, p110, p010, col);
            tri.AddQuad(p001, p011, p111, p101, col);
            tri.AddQuad(p000, p001, p101, p100, col);
            tri.AddQuad(p010, p110, p111, p011, col);
            tri.AddQuad(p000, p010, p011, p001, col);
            tri.AddQuad(p100, p101, p111, p110, col);
        }
    }
}

