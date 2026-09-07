using System;
using System.Globalization;
using System.IO;
using System.Text;
using CodeWalker.GameFiles;

namespace RageLightEditor.Editor
{
    public static class LightProxyBuilder
    {
        private const float QuadHalfSize = 0.02f;

        public static YdrFile Create(string name)
        {
            name = Sanitise(name);
            var tmpDir = Path.Combine(Path.GetTempPath(), "rle_proxy_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(tmpDir);
            try
            {
                var ydr = XmlYdr.GetYdr(BuildXml(name), tmpDir);
                if (ydr?.Drawable == null) throw new Exception("Couldn't build the proxy drawable.");
                ydr.Name = name + ".ydr";
                ydr.Drawable.LightAttributes = new ResourceSimpleList64<LightAttributes>
                {
                    data_items = Array.Empty<LightAttributes>(),
                };
                return ydr;
            }
            finally
            {
                try { Directory.Delete(tmpDir, true); } catch { }
            }
        }

        public static string Sanitised(string s) => Sanitise(s);

        public static int SelfTest(string outPath)
        {
            try
            {
                outPath = Path.GetFullPath(string.IsNullOrEmpty(outPath) ? "light_proxy_test.ydr" : outPath);
                Directory.CreateDirectory(Path.GetDirectoryName(outPath));

                var ydr = Create("my_light_prop");
                Console.WriteLine($"Built proxy: models={ydr.Drawable.AllModels?.Length}, " +
                    $"lights={ydr.Drawable.LightAttributes?.data_items?.Length}");

                ydr.Drawable.LightAttributes.data_items = new[]
                {
                    new LightAttributes
                    {
                        Position = new SharpDX.Vector3(0, 0, 2),
                        ColorR = 255, ColorG = 200, ColorB = 150,
                        Intensity = 6.0f, Falloff = 9.0f, FalloffExponent = 32.0f,
                        Type = LightType.Point,
                        Direction = new SharpDX.Vector3(0, 0, -1),
                        Tangent = new SharpDX.Vector3(-1, 0, 0),
                        Extent = new SharpDX.Vector3(1, 1, 1),
                        TimeFlags = 0xFFFFFF,
                    }
                };

                var data = ydr.Save();
                File.WriteAllBytes(outPath, data);
                Console.WriteLine($"Wrote {outPath} ({data.Length} bytes)");

                var check = new YdrFile();
                check.Load(File.ReadAllBytes(outPath));
                var d = check.Drawable;
                int lights = d?.LightAttributes?.data_items?.Length ?? 0;
                int models = d?.AllModels?.Length ?? 0;
                Console.WriteLine($"Reload: models={models}, lights={lights}, " +
                    $"pos=({d?.LightAttributes?.data_items?[0].Position})");
                if (models < 1 || lights != 1) { Console.WriteLine("PROXY TEST FAILED"); return 1; }
                Console.WriteLine("PROXY TEST PASSED");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("PROXY TEST FAILED: " + ex);
                return 1;
            }
        }

        private static string Sanitise(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "light_proxy";
            var sb = new StringBuilder();
            foreach (var c in s.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
                else if (c == ' ' || c == '-') sb.Append('_');
            }
            return sb.Length > 0 ? sb.ToString() : "light_proxy";
        }

        private static string F(float v) => v.ToString("0.######", CultureInfo.InvariantCulture);

        private static string BuildXml(string name)
        {
            const float h = QuadHalfSize;
            string Vert(float x, float y, float u, float v) =>
                $"      {F(x)}, {F(y)}, 0.000000, 0.000000, 0.000000, 1.000000, " +
                $"1.000000, 0.000000, 0.000000, 1.000000, 255, 255, 255, 255, {F(u)}, {F(v)}";

            var sb = new StringBuilder();
            sb.AppendLine(@"<?xml version=""1.0"" encoding=""UTF-8""?>");
            sb.AppendLine(@"<Drawable>");
            sb.AppendLine($" <Name>{name}</Name>");
            sb.AppendLine(@" <BoundingSphereCenter x=""0"" y=""0"" z=""0"" />");
            sb.AppendLine(@" <BoundingSphereRadius value=""1"" />");
            sb.AppendLine($" <BoundingBoxMin x=\"{F(-h)}\" y=\"{F(-h)}\" z=\"{F(-h)}\" />");
            sb.AppendLine($" <BoundingBoxMax x=\"{F(h)}\" y=\"{F(h)}\" z=\"{F(h)}\" />");
            sb.AppendLine(@" <LodDistHigh value=""9998"" />");
            sb.AppendLine(@" <LodDistMed value=""9998"" />");
            sb.AppendLine(@" <LodDistLow value=""9998"" />");
            sb.AppendLine(@" <LodDistVlow value=""9998"" />");
            sb.AppendLine(@" <FlagsHigh value=""1"" />");
            sb.AppendLine(@" <FlagsMed value=""0"" />");
            sb.AppendLine(@" <FlagsLow value=""0"" />");
            sb.AppendLine(@" <FlagsVlow value=""0"" />");
            sb.AppendLine(@" <ShaderGroup>");
            sb.AppendLine(@"  <TextureDictionary />");
            sb.AppendLine(@"  <Shaders>");
            sb.AppendLine(@"   <Item>");
            sb.AppendLine(@"    <Name>default</Name>");
            sb.AppendLine(@"    <FileName>default.sps</FileName>");
            sb.AppendLine(@"    <RenderBucket value=""0"" />");
            sb.AppendLine(@"    <Parameters />");
            sb.AppendLine(@"   </Item>");
            sb.AppendLine(@"  </Shaders>");
            sb.AppendLine(@" </ShaderGroup>");
            sb.AppendLine(@" <DrawableModelsHigh>");
            sb.AppendLine(@"  <Item>");
            sb.AppendLine(@"   <RenderMask value=""255"" />");
            sb.AppendLine(@"   <Flags value=""0"" />");
            sb.AppendLine(@"   <HasSkin value=""0"" />");
            sb.AppendLine(@"   <BoneIndex value=""0"" />");
            sb.AppendLine(@"   <Unknown1 value=""0"" />");
            sb.AppendLine(@"   <Geometries>");
            sb.AppendLine(@"    <Item>");
            sb.AppendLine(@"     <ShaderIndex value=""0"" />");
            sb.AppendLine($"     <BoundingBoxMin x=\"{F(-h)}\" y=\"{F(-h)}\" z=\"{F(-h)}\" />");
            sb.AppendLine($"     <BoundingBoxMax x=\"{F(h)}\" y=\"{F(h)}\" z=\"{F(h)}\" />");
            sb.AppendLine(@"     <VertexBuffer>");
            sb.AppendLine(@"      <Flags value=""0"" />");
            sb.AppendLine(@"      <Layout type=""GTAV1"">");
            sb.AppendLine(@"       <Position />");
            sb.AppendLine(@"       <Normal />");
            sb.AppendLine(@"       <Tangent />");
            sb.AppendLine(@"       <Colour0 />");
            sb.AppendLine(@"       <TexCoord0 />");
            sb.AppendLine(@"      </Layout>");
            sb.AppendLine(@"      <Data>");
            sb.AppendLine(Vert(-h, -h, 0, 0));
            sb.AppendLine(Vert(h, -h, 1, 0));
            sb.AppendLine(Vert(h, h, 1, 1));
            sb.AppendLine(Vert(-h, h, 0, 1));
            sb.AppendLine(@"      </Data>");
            sb.AppendLine(@"     </VertexBuffer>");
            sb.AppendLine(@"     <IndexBuffer>");
            sb.AppendLine(@"      <Data>");
            sb.AppendLine(@"       0 1 2 0 2 3");
            sb.AppendLine(@"      </Data>");
            sb.AppendLine(@"     </IndexBuffer>");
            sb.AppendLine(@"    </Item>");
            sb.AppendLine(@"   </Geometries>");
            sb.AppendLine(@"  </Item>");
            sb.AppendLine(@" </DrawableModelsHigh>");
            sb.AppendLine(@"</Drawable>");
            return sb.ToString();
        }
    }
}

