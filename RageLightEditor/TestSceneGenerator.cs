using System;
using System.Globalization;
using System.IO;
using System.Text;
using CodeWalker.GameFiles;

namespace RageLightEditor
{
    public static class TestSceneGenerator
    {
        public static bool SpotOnly = false;

        public static bool CullPlaneDemo = false;

        public static bool VolumeDemo = false;

        public static bool ProjTexDemo = false;

        public static bool HeavyDemo = false;

        public static int Run(string outPath)
        {
            try
            {
                if (string.IsNullOrEmpty(outPath)) outPath = "light_test_scene.ydr";
                outPath = Path.GetFullPath(outPath);
                Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                var tmpDir = Path.Combine(Path.GetTempPath(), "rle_gentest_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(tmpDir);

                File.WriteAllBytes(Path.Combine(tmpDir, "checker.dds"), BuildCheckerDds(64, 8));

                var xml = BuildXml();
                var ydr = XmlYdr.GetYdr(xml, tmpDir);
                if (ydr?.Drawable == null) throw new Exception("XML import failed");

                var data = ydr.Save();
                File.WriteAllBytes(outPath, data);
                Console.WriteLine($"Wrote {outPath} ({data.Length} bytes)");

                var check = new YdrFile();
                check.Load(File.ReadAllBytes(outPath));
                var d = check.Drawable;
                Console.WriteLine($"Reload OK: models={d?.AllModels?.Length}, lights={d?.LightAttributes?.data_items?.Length}, " +
                    $"shaders={d?.ShaderGroup?.Shaders?.data_items?.Length}, textures={d?.ShaderGroup?.TextureDictionary?.Textures?.data_items?.Length}");

                try { Directory.Delete(tmpDir, true); } catch { }
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("GENTEST FAILED: " + ex);
                return 1;
            }
        }

        private static string F(float v) => v.ToString("0.######", CultureInfo.InvariantCulture);

        private class MeshBuilder
        {
            public StringBuilder Verts = new StringBuilder();
            public StringBuilder Inds = new StringBuilder();
            public int VertCount;
            public float MinX = float.MaxValue, MinY = float.MaxValue, MinZ = float.MaxValue;
            public float MaxX = float.MinValue, MaxY = float.MinValue, MaxZ = float.MinValue;

            public int AddVert(float px, float py, float pz, float nx, float ny, float nz, float u, float v)
            {
                Verts.AppendLine($"    {F(px)} {F(py)} {F(pz)}   {F(nx)} {F(ny)} {F(nz)}   255 255 255 255   {F(u)} {F(v)}");
                MinX = Math.Min(MinX, px); MinY = Math.Min(MinY, py); MinZ = Math.Min(MinZ, pz);
                MaxX = Math.Max(MaxX, px); MaxY = Math.Max(MaxY, py); MaxZ = Math.Max(MaxZ, pz);
                return VertCount++;
            }

            public void AddTri(int a, int b, int c)
            {
                Inds.Append($"{a} {b} {c} ");
            }

            public void AddQuad(int a, int b, int c, int d)
            {
                AddTri(a, b, c);
                AddTri(a, c, d);
            }

            public void AddBox(float x0, float y0, float z0, float x1, float y1, float z1, float uvScale = 1.0f)
            {
                int v0 = AddVert(x0, y0, z1, 0, 0, 1, 0, 0);
                int v1 = AddVert(x1, y0, z1, 0, 0, 1, uvScale, 0);
                int v2 = AddVert(x1, y1, z1, 0, 0, 1, uvScale, uvScale);
                int v3 = AddVert(x0, y1, z1, 0, 0, 1, 0, uvScale);
                AddQuad(v0, v1, v2, v3);
                v0 = AddVert(x0, y1, z0, 0, 0, -1, 0, 0);
                v1 = AddVert(x1, y1, z0, 0, 0, -1, uvScale, 0);
                v2 = AddVert(x1, y0, z0, 0, 0, -1, uvScale, uvScale);
                v3 = AddVert(x0, y0, z0, 0, 0, -1, 0, uvScale);
                AddQuad(v0, v1, v2, v3);
                v0 = AddVert(x1, y0, z0, 1, 0, 0, 0, 0);
                v1 = AddVert(x1, y1, z0, 1, 0, 0, uvScale, 0);
                v2 = AddVert(x1, y1, z1, 1, 0, 0, uvScale, uvScale);
                v3 = AddVert(x1, y0, z1, 1, 0, 0, 0, uvScale);
                AddQuad(v0, v1, v2, v3);
                v0 = AddVert(x0, y1, z0, -1, 0, 0, 0, 0);
                v1 = AddVert(x0, y0, z0, -1, 0, 0, uvScale, 0);
                v2 = AddVert(x0, y0, z1, -1, 0, 0, uvScale, uvScale);
                v3 = AddVert(x0, y1, z1, -1, 0, 0, 0, uvScale);
                AddQuad(v0, v1, v2, v3);
                v0 = AddVert(x1, y1, z0, 0, 1, 0, 0, 0);
                v1 = AddVert(x0, y1, z0, 0, 1, 0, uvScale, 0);
                v2 = AddVert(x0, y1, z1, 0, 1, 0, uvScale, uvScale);
                v3 = AddVert(x1, y1, z1, 0, 1, 0, 0, uvScale);
                AddQuad(v0, v1, v2, v3);
                v0 = AddVert(x0, y0, z0, 0, -1, 0, 0, 0);
                v1 = AddVert(x1, y0, z0, 0, -1, 0, uvScale, 0);
                v2 = AddVert(x1, y0, z1, 0, -1, 0, uvScale, uvScale);
                v3 = AddVert(x0, y0, z1, 0, -1, 0, 0, uvScale);
                AddQuad(v0, v1, v2, v3);
            }

            public void AddPlane(float half, float z, int divs, float uvTiles)
            {
                int baseIdx = VertCount;
                for (int y = 0; y <= divs; y++)
                {
                    for (int x = 0; x <= divs; x++)
                    {
                        float fx = -half + 2 * half * x / divs;
                        float fy = -half + 2 * half * y / divs;
                        AddVert(fx, fy, z, 0, 0, 1, uvTiles * x / divs, uvTiles * y / divs);
                    }
                }
                for (int y = 0; y < divs; y++)
                {
                    for (int x = 0; x < divs; x++)
                    {
                        int i0 = baseIdx + y * (divs + 1) + x;
                        int i1 = i0 + 1;
                        int i2 = i0 + divs + 2;
                        int i3 = i0 + divs + 1;
                        AddQuad(i0, i1, i2, i3);
                    }
                }
            }

            public string GeometryXml(int shaderIndex)
            {
                return $@"      <Item>
       <ShaderIndex value=""{shaderIndex}"" />
       <BoundingBoxMin x=""{F(MinX)}"" y=""{F(MinY)}"" z=""{F(MinZ)}"" w=""0"" />
       <BoundingBoxMax x=""{F(MaxX)}"" y=""{F(MaxY)}"" z=""{F(MaxZ)}"" w=""0"" />
       <VertexBuffer>
        <Flags value=""0"" />
        <Layout type=""GTAV1"">
         <Position />
         <Normal />
         <Colour0 />
         <TexCoord0 />
        </Layout>
        <Data>
{Verts}        </Data>
       </VertexBuffer>
       <IndexBuffer>
        <Data>
         {Inds}
        </Data>
       </IndexBuffer>
      </Item>";
            }
        }

        private static string ModelXml(string geometries)
        {
            return $@"  <Item>
   <RenderMask value=""255"" />
   <Flags value=""0"" />
   <HasSkin value=""0"" />
   <BoneIndex value=""0"" />
   <Unknown1 value=""0"" />
   <Geometries>
{geometries}
   </Geometries>
  </Item>";
        }

        private static string BuildXml()
        {
            var ground = new MeshBuilder();
            ground.AddPlane(HeavyDemo ? 14.0f : 10.0f, 0.0f, HeavyDemo ? 140 : 20, 10.0f);

            var props = new MeshBuilder();
            props.AddBox(-0.6f, -0.6f, 0.0f, 0.6f, 0.6f, 1.2f, 1.0f);
            props.AddBox(2.6f, 1.6f, 0.0f, 3.4f, 2.4f, 0.8f, 1.0f);
            props.AddBox(-3.5f, 2.5f, 0.0f, -2.5f, 3.5f, 2.0f, 1.0f);

            var heavyModels = new StringBuilder();
            if (HeavyDemo)
            {
                for (int gx = -6; gx <= 6; gx++)
                    for (int gy = -6; gy <= 6; gy++)
                    {
                        if (gx == 0 && gy == 0) continue;
                        float cx = gx * 2.0f, cy = gy * 2.0f;
                        var pil = new MeshBuilder();
                        pil.AddBox(cx - 0.35f, cy - 0.35f, 0.0f, cx + 0.35f, cy + 0.35f, 2.5f, 1.0f);
                        heavyModels.AppendLine(ModelXml(pil.GeometryXml(0)));
                    }
            }

            var pillar = new MeshBuilder();
            pillar.AddBox(3.8f, -3.2f, 0.0f, 4.2f, -2.8f, 3.0f, 1.0f);

            var sb = new StringBuilder();
            sb.AppendLine(@"<?xml version=""1.0"" encoding=""UTF-8""?>");
            sb.AppendLine(@"<Drawable>");
            sb.AppendLine(@" <Name>light_test_scene</Name>");
            sb.AppendLine(@" <BoundingSphereCenter x=""0"" y=""0"" z=""1"" />");
            sb.AppendLine(@" <BoundingSphereRadius value=""15"" />");
            sb.AppendLine(@" <BoundingBoxMin x=""-10"" y=""-10"" z=""0"" />");
            sb.AppendLine(@" <BoundingBoxMax x=""10"" y=""10"" z=""3"" />");
            sb.AppendLine(@" <LodDistHigh value=""9998"" />");
            sb.AppendLine(@" <LodDistMed value=""9998"" />");
            sb.AppendLine(@" <LodDistLow value=""9998"" />");
            sb.AppendLine(@" <LodDistVlow value=""9998"" />");
            sb.AppendLine(@" <FlagsHigh value=""1"" />");
            sb.AppendLine(@" <FlagsMed value=""0"" />");
            sb.AppendLine(@" <FlagsLow value=""0"" />");
            sb.AppendLine(@" <FlagsVlow value=""0"" />");
            sb.AppendLine(@" <ShaderGroup>");
            sb.AppendLine(@"  <TextureDictionary>");
            sb.AppendLine(@"   <Item>");
            sb.AppendLine(@"    <Name>checker</Name>");
            sb.AppendLine(@"    <Unk32 value=""128"" />");
            sb.AppendLine(@"    <Usage>DIFFUSE</Usage>");
            sb.AppendLine(@"    <UsageFlags>UNK24</UsageFlags>");
            sb.AppendLine(@"    <ExtraFlags value=""0"" />");
            sb.AppendLine(@"    <Width value=""64"" />");
            sb.AppendLine(@"    <Height value=""64"" />");
            sb.AppendLine(@"    <MipLevels value=""1"" />");
            sb.AppendLine(@"    <Format>D3DFMT_A8R8G8B8</Format>");
            sb.AppendLine(@"    <FileName>checker.dds</FileName>");
            sb.AppendLine(@"   </Item>");
            sb.AppendLine(@"  </TextureDictionary>");
            sb.AppendLine(@"  <Shaders>");
            sb.AppendLine(@"   <Item>");
            sb.AppendLine(@"    <Name>default</Name>");
            sb.AppendLine(@"    <FileName>default.sps</FileName>");
            sb.AppendLine(@"    <RenderBucket value=""0"" />");
            sb.AppendLine(@"    <Parameters>");
            sb.AppendLine(@"     <Item name=""DiffuseSampler"" type=""Texture"">");
            sb.AppendLine(@"      <Name>checker</Name>");
            sb.AppendLine(@"     </Item>");
            sb.AppendLine(@"    </Parameters>");
            sb.AppendLine(@"   </Item>");
            sb.AppendLine(@"   <Item>");
            sb.AppendLine(@"    <Name>emissive</Name>");
            sb.AppendLine(@"    <FileName>emissive.sps</FileName>");
            sb.AppendLine(@"    <RenderBucket value=""0"" />");
            sb.AppendLine(@"    <Parameters>");
            sb.AppendLine(@"     <Item name=""EmissiveMultiplier"" type=""Vector"" x=""4"" y=""0"" z=""0"" w=""0"" />");
            sb.AppendLine(@"    </Parameters>");
            sb.AppendLine(@"   </Item>");
            sb.AppendLine(@"  </Shaders>");
            sb.AppendLine(@" </ShaderGroup>");
            sb.AppendLine(@" <DrawableModelsHigh>");
            sb.AppendLine(ModelXml(ground.GeometryXml(0)));
            sb.AppendLine(ModelXml(props.GeometryXml(0)));
            sb.AppendLine(ModelXml(pillar.GeometryXml(1)));
            if (HeavyDemo) sb.Append(heavyModels.ToString());
            sb.AppendLine(@" </DrawableModelsHigh>");
            uint volFlag = VolumeDemo ? 0x1000u : 0u;
            sb.AppendLine(@" <Lights>");
            if (!SpotOnly)
            {
                sb.AppendLine(LightXml(
                    pos: "x=\"0\" y=\"0\" z=\"2.5\"", r: 255, g: 190, b: 120, type: "Point",
                    intensity: 10, falloff: 8, falloffExp: 32,
                    dir: "x=\"0\" y=\"0\" z=\"-1\"", tan: "x=\"-1\" y=\"0\" z=\"0\"",
                    coneIn: 0, coneOut: 0, coronaSize: 2.5f, coronaInt: 1.2f, flags: volFlag));
            }
            sb.AppendLine(LightXml(
                pos: "x=\"-4\" y=\"-4\" z=\"5\"", r: 140, g: 190, b: 255, type: "Spot",
                intensity: 20, falloff: 12, falloffExp: 16,
                dir: "x=\"0.4\" y=\"0.4\" z=\"-0.825\"", tan: "x=\"-0.9\" y=\"0.44\" z=\"0\"",
                coneIn: 12, coneOut: 38, coronaSize: 3.0f, coronaInt: 1.5f,
                flags: (CullPlaneDemo ? 0x40000u : 0u) | volFlag | (ProjTexDemo ? 0x20u : 0u),
                cullOff: CullPlaneDemo ? 3.0f : 0.0f,
                projTexHash: ProjTexDemo ? CodeWalker.GameFiles.JenkHash.GenHash("checker") : 0u));
            if (!SpotOnly)
            {
                sb.AppendLine(LightXml(
                    pos: "x=\"-3\" y=\"0.5\" z=\"1.2\"", r: 120, g: 255, b: 140, type: "Capsule",
                    intensity: 6, falloff: 3, falloffExp: 24,
                    dir: "x=\"0\" y=\"1\" z=\"0\"", tan: "x=\"-1\" y=\"0\" z=\"0\"",
                    coneIn: 0, coneOut: 0, coronaSize: 1.5f, coronaInt: 0.8f, extentX: 2.5f));
            }
            if (HeavyDemo)
            {
                int n = 0;
                for (int gx = -3; gx <= 3 && n < 40; gx++)
                    for (int gy = -3; gy <= 3 && n < 40; gy++)
                    {
                        bool spot = (n % 2) == 0;
                        int r = (n * 53) % 256, g = (n * 97) % 256, b = (n * 151) % 256;
                        sb.AppendLine(LightXml(
                            pos: $"x=\"{F(gx * 3.5f)}\" y=\"{F(gy * 3.5f)}\" z=\"3.5\"",
                            r: r, g: g, b: b, type: spot ? "Spot" : "Point",
                            intensity: 12, falloff: 6, falloffExp: 24,
                            dir: "x=\"0\" y=\"0\" z=\"-1\"", tan: "x=\"-1\" y=\"0\" z=\"0\"",
                            coneIn: spot ? 15 : 0, coneOut: spot ? 45 : 0,
                            coronaSize: 0.5f, coronaInt: 0.8f, flags: 0x180u));
                        n++;
                    }
            }
            sb.AppendLine(@" </Lights>");
            sb.AppendLine(@"</Drawable>");
            return sb.ToString();
        }

        private static string LightXml(string pos, int r, int g, int b, string type,
            float intensity, float falloff, float falloffExp, string dir, string tan,
            float coneIn, float coneOut, float coronaSize, float coronaInt, float extentX = 1,
            uint flags = 0, float cullOff = 0, uint projTexHash = 0)
        {
            return $@"  <Item>
   <Position {pos} />
   <Colour r=""{r}"" g=""{g}"" b=""{b}"" />
   <Flashiness value=""0"" />
   <Intensity value=""{F(intensity)}"" />
   <Flags value=""{flags}"" />
   <BoneId value=""0"" />
   <Type>{type}</Type>
   <GroupId value=""0"" />
   <TimeFlags value=""0"" />
   <Falloff value=""{F(falloff)}"" />
   <FalloffExponent value=""{F(falloffExp)}"" />
   <CullingPlaneNormal x=""0"" y=""0"" z=""1"" />
   <CullingPlaneOffset value=""{F(cullOff)}"" />
   <Unknown45 value=""0"" />
   <Unknown46 value=""0"" />
   <VolumeIntensity value=""1"" />
   <VolumeSizeScale value=""1"" />
   <VolumeOuterColour r=""255"" g=""255"" b=""255"" />
   <LightHash value=""0"" />
   <VolumeOuterIntensity value=""1"" />
   <CoronaSize value=""{F(coronaSize)}"" />
   <VolumeOuterExponent value=""1"" />
   <LightFadeDistance value=""0"" />
   <ShadowBlur value=""0"" />
   <ShadowFadeDistance value=""0"" />
   <SpecularFadeDistance value=""0"" />
   <VolumetricFadeDistance value=""0"" />
   <ShadowNearClip value=""0.05"" />
   <CoronaIntensity value=""{F(coronaInt)}"" />
   <CoronaZBias value=""0.1"" />
   <Direction {dir} />
   <Tangent {tan} />
   <ConeInnerAngle value=""{F(coneIn)}"" />
   <ConeOuterAngle value=""{F(coneOut)}"" />
   <Extent x=""{F(extentX)}"" y=""1"" z=""1"" />
   <ProjectedTextureHash>{(projTexHash != 0 ? "hash_" + projTexHash.ToString("X8") : "")}</ProjectedTextureHash>
  </Item>";
        }

        private static byte[] BuildCheckerDds(int size, int cells)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(0x20534444u);
            w.Write(124u);
            w.Write(0x0000100Fu);
            w.Write((uint)size);
            w.Write((uint)size);
            w.Write((uint)(size * 4));
            w.Write(0u);
            w.Write(0u);
            for (int i = 0; i < 11; i++) w.Write(0u);
            w.Write(32u);
            w.Write(0x41u);
            w.Write(0u);
            w.Write(32u);
            w.Write(0x00FF0000u);
            w.Write(0x0000FF00u);
            w.Write(0x000000FFu);
            w.Write(0xFF000000u);
            w.Write(0x1000u);
            w.Write(0u); w.Write(0u); w.Write(0u); w.Write(0u);

            int cell = Math.Max(size / cells, 1);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool a = ((x / cell) + (y / cell)) % 2 == 0;
                    byte v = a ? (byte)235 : (byte)140;
                    w.Write(v);
                    w.Write(v);
                    w.Write(v);
                    w.Write((byte)255);
                }
            }
            return ms.ToArray();
        }
    }
}

