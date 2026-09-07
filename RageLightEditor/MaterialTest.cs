using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using SharpDX;

namespace RageLightEditor
{
    public static class MaterialTest
    {

        private class Loaded
        {
            public YdrFile Ydr;
            public YftFile Yft;
            public byte[] Original;
            public string Path;

            public IEnumerable<DrawableBase> Drawables()
            {
                var lf = new LoadedFile { Ydr = Ydr, Yft = Yft, IsYft = Yft != null, Path = Path };
                return MaterialEditing.Drawables(lf);
            }

            public LoadedFile AsFile() =>
                new LoadedFile { Ydr = Ydr, Yft = Yft, IsYft = Yft != null, Path = Path };

            public byte[] Save() => Yft != null ? Yft.Save() : Ydr.Save();
        }

        private static Loaded Load(string path)
        {
            ShaderPresets.Seed();

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                throw new Exception("file not found: " + (path ?? "(none)"));

            var data = File.ReadAllBytes(path);
            var ext = Path.GetExtension(path).ToLowerInvariant();
            var l = new Loaded { Original = data, Path = path };

            if (ext == ".yft")
            {
                l.Yft = new YftFile();
                l.Yft.Load(data);
                if (l.Yft.Fragment == null) throw new Exception("failed to parse YFT (no fragment)");
            }
            else
            {
                l.Ydr = new YdrFile();
                l.Ydr.Load(data);
                if (l.Ydr.Drawable == null) throw new Exception("failed to parse YDR (no drawable)");
            }
            return l;
        }

        private static List<MaterialRef> Materials(Loaded l) =>
            MaterialEditing.ForFile(null, l.AsFile());

        public static int Dump(string path)
        {
            try
            {
                var l = Load(path);
                var mats = Materials(l);

                var geomCount = new Dictionary<ShaderFX, int>();
                int totalGeoms = 0;
                foreach (var d in l.Drawables())
                {
                    foreach (var m in d.AllModels ?? Array.Empty<DrawableModel>())
                    {
                        foreach (var g in m?.Geometries ?? Array.Empty<DrawableGeometry>())
                        {
                            if (g?.Shader == null) continue;
                            geomCount.TryGetValue(g.Shader, out int c);
                            geomCount[g.Shader] = c + 1;
                            totalGeoms++;
                        }
                    }
                }

                Console.WriteLine($"file={Path.GetFileName(path)} bytes={l.Original.Length} " +
                                  $"shaders={mats.Count} geometries={totalGeoms}");

                var embedded = mats.FirstOrDefault()?.EmbeddedDict;
                var embTex = embedded?.Textures?.data_items?.Where(t => t != null).ToArray()
                    ?? Array.Empty<CodeWalker.GameFiles.Texture>();
                Console.WriteLine($"embedded_textures={embTex.Length}");

                int shortTex = 0;
                foreach (var t in embTex)
                {
                    int have = t.Data?.FullData?.Length ?? 0;
                    int want = RageLightEditor.Rendering.TextureLoader.ExpectedDataSize(
                        t.Format, t.Width, t.Height, t.Levels);
                    int mip0 = RageLightEditor.Rendering.TextureLoader.Mip0Size(t.Format, t.Width, t.Height);
                    string status = have >= want ? "ok" : have >= mip0 ? "TRUNCATED-MIPS" : "TOO-SMALL";
                    if (have < mip0) shortTex++;
                    Console.WriteLine($"    tex {t.Name} {t.Width}x{t.Height} {t.Format} " +
                        $"mips={t.Levels} bytes={have} need={want} mip0={mip0} [{status}]");
                }
                if (shortTex > 0)
                {
                    Console.WriteLine($"  WARNING: {shortTex} texture(s) hold less data than mip 0 of their " +
                        "declared format needs - those cannot be uploaded and will not render.");
                }

                foreach (var m in mats)
                {
                    geomCount.TryGetValue(m.Shader, out int gc);
                    Console.WriteLine($"shader {m.Index}: name={m.Name} sps={m.Sps} " +
                        $"bucket={m.Bucket} params={m.Shader.ParameterCount} " +
                        $"textures={m.Shader.TextureParametersCount} geoms={gc}");

                    foreach (var h in MaterialEditing.ParamHashes(m.Shader))
                    {
                        int i = MaterialEditing.IndexOf(m.Shader, h);
                        var p = m.Shader.ParametersList.Parameters[i];
                        string name = MaterialDefs.NameOf(h);

                        if (p.DataType == 0)
                        {
                            var tb = p.Data as TextureBase;
                            string tname = tb?.Name ?? "(none)";
                            string src = tb == null ? "none"
                                : (tb is CodeWalker.GameFiles.Texture t && t.Data?.FullData != null) ? "embedded"
                                : embedded?.Lookup(tb.NameHash)?.Data?.FullData != null ? "embedded"
                                : "external";
                            Console.WriteLine($"    tex  {name} = {tname} [{src}]");
                        }
                        else if (p.Data is Vector4 v)
                        {
                            Console.WriteLine($"    val  {name} = {F(v)}");
                        }
                        else if (p.Data is Vector4[] arr)
                        {
                            Console.WriteLine($"    arr  {name} = {arr.Length} rows, [0]={F(arr.Length > 0 ? arr[0] : Vector4.Zero)}");
                        }
                        else
                        {
                            Console.WriteLine($"    ?    {name} = (null, type {p.DataType})");
                        }
                    }
                }

                Console.WriteLine($"MATDUMP: shaders={mats.Count} geometries={totalGeoms} " +
                                  $"embedded={embTex.Length} bad_textures={shortTex} result=OK");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("MATDUMP: result=FAILED " + ex.Message);
                return 1;
            }
        }

        private static string F(Vector4 v) => $"({v.X:0.####}, {v.Y:0.####}, {v.Z:0.####}, {v.W:0.####})";

        public static int Identity(string path)
        {
            try
            {
                var l = Load(path);
                var saved = l.Save();

                bool sameLen = saved.Length == l.Original.Length;
                int firstDiff = -1;
                int diffs = 0;
                int n = Math.Min(saved.Length, l.Original.Length);
                for (int i = 0; i < n; i++)
                {
                    if (saved[i] != l.Original[i])
                    {
                        if (firstDiff < 0) firstDiff = i;
                        diffs++;
                    }
                }
                if (!sameLen) diffs += Math.Abs(saved.Length - l.Original.Length);

                var l2 = new Loaded { Path = path, Original = saved };
                if (l.Yft != null) { l2.Yft = new YftFile(); l2.Yft.Load(saved); }
                else { l2.Ydr = new YdrFile(); l2.Ydr.Load(saved); }

                var a = Materials(l);
                var b = Materials(l2);
                bool structOk = a.Count == b.Count;
                if (structOk)
                {
                    for (int i = 0; i < a.Count; i++)
                    {
                        if (a[i].Name != b[i].Name || a[i].Sps != b[i].Sps ||
                            a[i].Bucket != b[i].Bucket ||
                            a[i].Shader.ParameterCount != b[i].Shader.ParameterCount)
                        {
                            structOk = false;
                            break;
                        }
                    }
                }

                Console.WriteLine($"MATIDENTITY: bytes_in={l.Original.Length} bytes_out={saved.Length} " +
                    $"diff_bytes={diffs} first_diff={firstDiff} shaders={a.Count} " +
                    $"reload_matches={(structOk ? "yes" : "NO")} " +
                    $"result={(diffs == 0 ? "IDENTICAL" : structOk ? "REENCODED" : "FAILED")}");
                return structOk ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine("MATIDENTITY: result=FAILED " + ex.Message);
                return 1;
            }
        }

        public static int EditTest(string path)
        {
            string tmp = null;
            try
            {
                var l = Load(path);
                var mats = Materials(l);
                if (mats.Count == 0) throw new Exception("file has no materials");

                var m = mats[0];
                int paramsBefore = m.Shader.ParameterCount;
                Console.WriteLine($"target: shader 0 '{m.Name}' ({m.Sps}) bucket={m.Bucket} params={paramsBefore}");

                const uint bumpHash = (uint)ShaderParamNames.bumpiness;
                bool bumpExisted = MaterialEditing.Has(m.Shader, bumpHash);
                var bumpBefore = MaterialEditing.GetValue(m.Shader, bumpHash, Vector4.Zero);
                var bumpWant = new Vector4(2.75f, 0, 0, 0);
                if (bumpExisted) MaterialEditing.SetValue(m.Shader, bumpHash, bumpWant);
                else MaterialEditing.AddParam(m.Shader, bumpHash, bumpWant, false);
                Console.WriteLine($"bumpiness: existed={bumpExisted} before={bumpBefore.X:0.###} set={bumpWant.X:0.###}");

                const uint detailHash = (uint)ShaderParamNames.detailSettings;
                bool detailExisted = MaterialEditing.Has(m.Shader, detailHash);
                var detailWant = new Vector4(0.5f, 0.75f, 12f, 18f);
                if (detailExisted) MaterialEditing.SetValue(m.Shader, detailHash, detailWant);
                else MaterialEditing.AddParam(m.Shader, detailHash, detailWant, false);
                Console.WriteLine($"detailSettings: existed={detailExisted} set={F(detailWant)}");

                const uint diffHash = (uint)ShaderParamNames.DiffuseSampler;
                var texBefore = MaterialEditing.GetTexture(m.Shader, diffHash)?.Name ?? "(none)";
                const string texWant = "rle_mattest_diffuse";
                bool diffExisted = MaterialEditing.Has(m.Shader, diffHash);
                if (diffExisted) MaterialEditing.SetTexture(m.Shader, diffHash, MaterialEditing.MakeTextureRef(texWant));
                else MaterialEditing.AddParam(m.Shader, diffHash, MaterialEditing.MakeTextureRef(texWant), true);
                Console.WriteLine($"DiffuseSampler: existed={diffExisted} before={texBefore} set={texWant}");

                byte bucketBefore = m.Shader.RenderBucket;
                byte bucketWant = (byte)(bucketBefore == 0 ? 3 : 0);
                MaterialEditing.SetBucket(m.Shader, bucketWant);
                Console.WriteLine($"bucket: before={bucketBefore} set={bucketWant}");

                int paramsAfter = m.Shader.ParameterCount;

                tmp = Path.Combine(Path.GetTempPath(),
                    "rle_mattest_" + Path.GetFileName(path));
                File.WriteAllBytes(tmp, l.Save());
                var back = Load(tmp);
                var mats2 = Materials(back);
                if (mats2.Count != mats.Count)
                    throw new Exception($"shader count changed: {mats.Count} -> {mats2.Count}");
                var m2 = mats2[0];

                var fails = new List<string>();

                var bumpGot = MaterialEditing.GetValue(m2.Shader, bumpHash, Vector4.Zero);
                if (Math.Abs(bumpGot.X - bumpWant.X) > 1e-4f)
                    fails.Add($"bumpiness {bumpGot.X:0.####} != {bumpWant.X:0.####}");

                var detailGot = MaterialEditing.GetValue(m2.Shader, detailHash, Vector4.Zero);
                if ((detailGot - detailWant).Length() > 1e-3f)
                    fails.Add($"detailSettings {F(detailGot)} != {F(detailWant)}");

                var texGot = MaterialEditing.GetTexture(m2.Shader, diffHash);
                if (texGot == null || !string.Equals(texGot.Name, texWant, StringComparison.OrdinalIgnoreCase))
                    fails.Add($"DiffuseSampler '{texGot?.Name ?? "(none)"}' != '{texWant}'");
                if (texGot != null && texGot.NameHash != JenkHash.GenHash(texWant))
                    fails.Add("DiffuseSampler name hash wrong");

                if (m2.Shader.RenderBucket != bucketWant)
                    fails.Add($"bucket {m2.Shader.RenderBucket} != {bucketWant}");
                if (m2.Shader.RenderBucketMask != ((1u << bucketWant) | 0xFF00u))
                    fails.Add($"bucket mask 0x{m2.Shader.RenderBucketMask:X} wrong for bucket {bucketWant}");

                if (m2.Shader.ParameterCount != paramsAfter)
                    fails.Add($"param count {m2.Shader.ParameterCount} != {paramsAfter}");

                for (int i = 1; i < mats.Count; i++)
                {
                    if (mats[i].Shader.ParameterCount != mats2[i].Shader.ParameterCount ||
                        mats[i].Name != mats2[i].Name ||
                        mats[i].Shader.RenderBucket != mats2[i].Shader.RenderBucket)
                    {
                        fails.Add($"shader {i} was disturbed by editing shader 0");
                        break;
                    }
                }

                var again = back.Save();
                var back2 = Load(tmp);
                File.WriteAllBytes(tmp, again);
                var mats3 = Materials(Load(tmp));
                if (mats3.Count != mats2.Count) fails.Add("second save changed the shader count");

                foreach (var f in fails) Console.WriteLine("  FAIL: " + f);

                Console.WriteLine($"MATTEST: shaders={mats.Count} params_before={paramsBefore} " +
                    $"params_after={paramsAfter} checks_failed={fails.Count} " +
                    $"result={(fails.Count == 0 ? "OK" : "FAILED")}");
                return fails.Count == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine("MATTEST: result=FAILED " + ex.Message);
                return 1;
            }
            finally
            {
                try { if (tmp != null && File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }

        public static int PresetTest(string path, int shaderIndex, string preset)
        {
            string tmp = null;
            try
            {
                var l = Load(path);
                var mats = Materials(l);
                if (mats.Count == 0) throw new Exception("file has no materials");
                if (shaderIndex < 0 || shaderIndex >= mats.Count)
                    throw new Exception($"shader index {shaderIndex} out of range (0..{mats.Count - 1})");

                foreach (var d in l.Drawables()) ShaderPresets.HarvestAll(d);

                var m = mats[shaderIndex];
                var t = ShaderPresets.Template(preset);
                if (t == null) throw new Exception("unknown preset: " + preset);

                var beforeName = m.Name;
                var beforeParams = MaterialEditing.ParamHashes(m.Shader).ToList();
                var beforeValues = beforeParams.ToDictionary(h => h,
                    h => MaterialEditing.GetValue(m.Shader, h, Vector4.Zero));
                var beforeTextures = beforeParams
                    .Where(h => MaterialEditing.GetTexture(m.Shader, h) != null)
                    .ToDictionary(h => h, h => MaterialEditing.GetTexture(m.Shader, h).Name);

                Console.WriteLine($"from: {beforeName} ({m.Sps}) bucket={m.Bucket} params={beforeParams.Count}");
                Console.WriteLine($"to:   {t.Name} ({t.Sps}) bucket={t.Bucket} params={t.Params.Count} " +
                                  $"source={(t.Harvested ? "harvested" : "derived")}");

                var warn = ShaderPresets.CompatibilityWarning(preset, m.Meshes.Count > 0 ? m.Meshes[0].Geometry : null);
                if (warn != null) Console.WriteLine("warning: " + warn);

                if (!ShaderPresets.Apply(m.Shader, preset)) throw new Exception("preset apply failed");

                var afterParams = MaterialEditing.ParamHashes(m.Shader).ToList();

                tmp = Path.Combine(Path.GetTempPath(), "rle_matpreset_" + Path.GetFileName(path));
                File.WriteAllBytes(tmp, l.Save());
                var back = Load(tmp);
                var m2 = Materials(back)[shaderIndex];

                var fails = new List<string>();

                if (!string.Equals(m2.Name, t.Name, StringComparison.OrdinalIgnoreCase))
                    fails.Add($"shader name '{m2.Name}' != '{t.Name}'");
                if (!string.Equals(m2.Sps, t.Sps, StringComparison.OrdinalIgnoreCase))
                    fails.Add($"sps '{m2.Sps}' != '{t.Sps}'");
                if (m2.Bucket != t.Bucket)
                    fails.Add($"bucket {m2.Bucket} != {t.Bucket}");

                var got = MaterialEditing.ParamHashes(m2.Shader).ToList();
                if (got.Count != t.Params.Count)
                    fails.Add($"param count {got.Count} != {t.Params.Count}");
                foreach (var pp in t.Params)
                {
                    if (!got.Contains(pp.Hash))
                        fails.Add($"missing {MaterialDefs.NameOf(pp.Hash)}");
                }

                int carried = 0;
                foreach (var pp in t.Params)
                {
                    if (pp.IsTexture)
                    {
                        if (!beforeTextures.TryGetValue(pp.Hash, out var wantTex)) continue;
                        var gotTex = MaterialEditing.GetTexture(m2.Shader, pp.Hash)?.Name;
                        if (!string.Equals(gotTex, wantTex, StringComparison.OrdinalIgnoreCase))
                            fails.Add($"texture {MaterialDefs.NameOf(pp.Hash)} '{gotTex}' != carried '{wantTex}'");
                        else carried++;
                    }
                    else
                    {
                        if (!beforeValues.TryGetValue(pp.Hash, out var want)) continue;
                        var gotVal = MaterialEditing.GetValue(m2.Shader, pp.Hash, Vector4.Zero);
                        if ((gotVal - want).Length() > 1e-3f)
                            fails.Add($"value {MaterialDefs.NameOf(pp.Hash)} {F(gotVal)} != carried {F(want)}");
                        else carried++;
                    }
                }

                int restored = 0, lost = 0;
                ShaderPresets.Apply(m.Shader, beforeName);
                foreach (var kv in beforeTextures)
                {
                    if (!MaterialEditing.Has(m.Shader, kv.Key)) continue;
                    var returned = MaterialEditing.GetTexture(m.Shader, kv.Key)?.Name;
                    if (string.Equals(returned, kv.Value, StringComparison.OrdinalIgnoreCase)) restored++;
                    else
                    {
                        lost++;
                        fails.Add($"round trip lost {MaterialDefs.NameOf(kv.Key)}: " +
                                  $"'{returned ?? "(none)"}' != '{kv.Value}'");
                    }
                }

                foreach (var f in fails) Console.WriteLine("  FAIL: " + f);
                Console.WriteLine($"  round trip {beforeName} -> {t.Name} -> {beforeName}: " +
                                  $"{restored} texture(s) restored, {lost} lost");

                Console.WriteLine($"MATPRESET: from={beforeName} to={t.Name} " +
                    $"params_before={beforeParams.Count} params_after={afterParams.Count} " +
                    $"carried={carried} template={(t.Harvested ? "harvested" : "derived")} " +
                    $"checks_failed={fails.Count} result={(fails.Count == 0 ? "OK" : "FAILED")}");
                return fails.Count == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine("MATPRESET: result=FAILED " + ex.Message);
                return 1;
            }
            finally
            {
                try { if (tmp != null && File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }

        public static int SaveTest(string path)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "rle_matsave_" + Path.GetFileName(path));
            int failures = 0;

            bool Step(string what, Action<MaterialRef> edit)
            {
                try
                {
                    var l = Load(path);
                    var mats = Materials(l);
                    if (mats.Count == 0) throw new Exception("no materials");
                    edit(mats[0]);
                    var bytes = l.Save();
                    File.WriteAllBytes(tmp, bytes);
                    var back = Load(tmp);
                    int n = Materials(back).Count;
                    Console.WriteLine($"  OK   {what}: saved {bytes.Length} bytes, reloaded {n} shaders");
                    return true;
                }
                catch (Exception ex)
                {
                    failures++;
                    Console.WriteLine($"  FAIL {what}: {ex.GetType().Name}: {ex.Message}");
                    var st = ex.StackTrace?.Split('\n').Take(4).Select(s => s.Trim());
                    if (st != null) foreach (var s in st) Console.WriteLine("        " + s);
                    return false;
                }
            }

            try
            {
                Console.WriteLine($"save paths on {Path.GetFileName(path)}:");

                Step("untouched", _ => { });

                Step("clear DiffuseSampler", m =>
                    MaterialEditing.SetTexture(m.Shader, (uint)ShaderParamNames.DiffuseSampler, null));

                Step("clear every texture slot", m =>
                {
                    foreach (var h in MaterialEditing.ParamHashes(m.Shader).ToList())
                    {
                        int i = MaterialEditing.IndexOf(m.Shader, h);
                        if (m.Shader.ParametersList.Parameters[i].DataType == 0)
                            MaterialEditing.SetTexture(m.Shader, h, null);
                    }
                });

                Step("add an empty texture slot", m =>
                    MaterialEditing.AddParam(m.Shader, (uint)ShaderParamNames.DetailSampler, null, true));

                Step("add a texture slot with a name", m =>
                    MaterialEditing.AddParam(m.Shader, (uint)ShaderParamNames.TintPaletteSampler,
                        MaterialEditing.MakeTextureRef("rle_test_palette"), true));

                Step("remove a value parameter", m =>
                    MaterialEditing.RemoveParam(m.Shader, (uint)ShaderParamNames.bumpiness));

                Step("switch preset then save", m => ShaderPresets.Apply(m.Shader, "default"));

                Step("switch preset twice then save", m =>
                {
                    ShaderPresets.Apply(m.Shader, "default");
                    ShaderPresets.Apply(m.Shader, "normal_spec");
                });

                try
                {
                    var src = Load(path);
                    var drawable = src.Ydr?.Drawable;
                    if (drawable == null) throw new Exception("test file has no drawable");

                    var scene = new Scene(null, null);
                    var lf = scene.AddImportedProp(null, null, null, null, drawable.Skeleton,
                        Array.Empty<LightAttributes>(), SharpDX.Matrix.Identity, 1, true,
                        "archive_shell", null, true, drawable);

                    var mats = MaterialEditing.ForFile(scene, lf);
                    if (mats.Count == 0) throw new Exception("no materials came through the drawable");
                    if (mats[0].Group == null) throw new Exception("no ShaderGroup - preset changes would be blocked");
                    if (!mats[0].CanSave) throw new Exception("CanSave is false, so the shader picker stays disabled");

                    var before = mats[0].Name;
                    ShaderPresets.Apply(mats[0].Shader, "default");

                    string outPath = Path.Combine(Path.GetTempPath(), "rle_archive_out.ydr");
                    scene.SaveOneAs(lf, outPath);

                    var back = Load(outPath);
                    var backMats = Materials(back);
                    bool applied = string.Equals(backMats[0].Name, "default", StringComparison.OrdinalIgnoreCase);
                    Console.WriteLine($"  {(applied ? "OK  " : "FAIL")} archive prop: {mats.Count} materials, " +
                        $"preset {before} -> {backMats[0].Name}, wrote {new FileInfo(outPath).Length} bytes, " +
                        $"reloaded {backMats.Count} shaders");
                    if (!applied) failures++;
                    try { File.Delete(outPath); } catch { }
                }
                catch (Exception ex)
                {
                    failures++;
                    Console.WriteLine($"  FAIL archive prop save: {ex.GetType().Name}: {ex.Message}");
                }

                try
                {
                    var scene = new Scene(null, null);
                    var lf = scene.AddImportedProp("archive_prop.ydr", null, null, null, null,
                        Array.Empty<LightAttributes>(), SharpDX.Matrix.Identity, 1, true, "archive_prop.ydr");
                    string outPath = Path.Combine(Path.GetTempPath(), "rle_should_not_exist.ydr");
                    try
                    {
                        scene.SaveOneAs(lf, outPath);
                        failures++;
                        Console.WriteLine("  FAIL save-as with no resource: it claimed to succeed");
                    }
                    catch (NullReferenceException)
                    {
                        failures++;
                        Console.WriteLine("  FAIL save-as with no resource: still a NullReferenceException");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  OK   save-as with no resource refused: \"{ex.Message}\"");
                    }
                    try { if (File.Exists(outPath)) { File.Delete(outPath); } } catch { }
                }
                catch (Exception ex)
                {
                    failures++;
                    Console.WriteLine("  FAIL save-as with no resource: setup threw " + ex.Message);
                }

                Console.WriteLine($"MATSAVETEST: failures={failures} result={(failures == 0 ? "OK" : "FAILED")}");
                return failures == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine("MATSAVETEST: result=FAILED " + ex.Message);
                return 1;
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }

        public static int ListPresets(string filter)
        {
            ShaderPresets.Seed();
            int n = 0;
            foreach (var (cat, names) in ShaderPresets.Categories)
            {
                var shown = string.IsNullOrEmpty(filter)
                    ? names
                    : names.Where(x => x.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (shown.Length == 0) continue;
                Console.WriteLine($"== {cat} ({shown.Length}) ==");
                foreach (var name in shown)
                {
                    var t = ShaderPresets.Template(name);
                    var tex = t.Params.Count(p => p.IsTexture);
                    Console.WriteLine($"  {name,-46} sps={t.Sps,-50} bucket={t.Bucket} " +
                                      $"params={t.Params.Count} textures={tex}");
                    n++;
                }
            }
            Console.WriteLine($"MATPRESETS: presets={n} result=OK");
            return 0;
        }
    }
}

