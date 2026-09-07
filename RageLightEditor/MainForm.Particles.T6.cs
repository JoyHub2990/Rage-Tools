using System;
using System.Linq;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using SDX = SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private bool ptfxProtoDone_T6;

        private void ServiceParticleAuthoring_T6()
        {
            ServiceParticleProto_T6();
            var p = Ptfx;
            if (p == null) return;

            if (p.RequestNewDocument_T6) { p.RequestNewDocument_T6 = false; NewPtfxDocument_T6(p); }
            if (p.RequestNewFromPlaying_T6) { p.RequestNewFromPlaying_T6 = false; NewPtfxFromPlaying_T6(p); }
            if (p.RequestLoadSheets_T6) { p.RequestLoadSheets_T6 = false; LoadSheetLibrary_T6(p); }
            if (p.RequestImportSheet_T6 >= 0)
            { int em = p.RequestImportSheet_T6; p.RequestImportSheet_T6 = -1; ImportSheetDialog_V18(p, em); }

            if (!ptfxNewFlagDone_T6)
            {
                var want = Environment.GetEnvironmentVariable("RLE_PTFXNEW");
                if (!string.IsNullOrWhiteSpace(want) && (gameFiles == null || gameFiles.Ready))
                {
                    ptfxNewFlagDone_T6 = true;
                    var parts = want.Split(',', StringSplitOptions.RemoveEmptyEntries);
                    p.NewAssetName_T6 = parts.Length > 0 ? parts[0].Trim() : "my_particles";
                    p.NewEffectName_T6 = parts.Length > 1 ? parts[1].Trim() : "my_effect";
                    panel.SwitchWorkspace(Editor.LightPanel.Space.Particles);
                    NewPtfxDocument_T6(p);
                    RunPtfxAuthorScript_T6(p);
                }
            }
        }

        private bool ptfxNewFlagDone_T6;

        private void NewPtfxDocument_T6(ParticlePanel p)
        {
            try
            {
                if (p.SheetLibrary_T6.Count == 0 && gameFiles != null && gameFiles.Ready) LoadSheetLibrary_T6(p);
                var doc = PtfxAuthor.NewDocument(p.NewAssetName_T6, p.NewEffectName_T6, DefaultSheet_T6(p));
                p.SetDocument(doc);
                panel.SwitchWorkspace(Editor.LightPanel.Space.Particles);
                p.Sim.Origin = NewEffectOrigin_T6();
                p.Sim.Playing = true;
                p.Status = $"new asset {doc.Name} - shape it in Create, then Save .ypt as...";
                WarmParticles_N4(1.5f);
                Console.WriteLine($"PTFXNEW {doc.Name}: {doc.Effects.Count} effect(s), " +
                                  $"{doc.Effects.FirstOrDefault()?.Emitters.Count ?? 0} emitter(s), " +
                                  $"sheet {PtfxAuthor.EmitterSheet(doc.Effects.FirstOrDefault()?.Emitters.FirstOrDefault())?.Name}" +
                                  $", {p.Sim.AliveCount} particles alive after 1.5 s at " +
                                  $"({p.Sim.Origin.X:0.##}, {p.Sim.Origin.Y:0.##}, {p.Sim.Origin.Z:0.##})");
            }
            catch (Exception ex) { p.Status = "could not make a new asset: " + ex.Message; }
        }

        private SDX.Vector3 NewEffectOrigin_T6()
        {
            SDX.Vector3 at;
            try { at = ParticleViewPoint_N4(); }
            catch { at = default; }
            if (Finite_T6(at)) return at;
            var eye = camera?.Position ?? SDX.Vector3.Zero;
            var ahead = eye + (camera?.GetForward() ?? SDX.Vector3.UnitX) * 4.0f;
            return Finite_T6(ahead) ? ahead : SDX.Vector3.Zero;
        }

        private static bool Finite_T6(SDX.Vector3 v) =>
            float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

        private void NewPtfxFromPlaying_T6(ParticlePanel p)
        {
            var src = p.Sim.Effect;
            if (src == null) { p.Status = "nothing is playing"; return; }
            try
            {
                var doc = PtfxAuthor.NewDocument(p.NewAssetName_T6, p.NewEffectName_T6, DefaultSheet_T6(p));
                var copy = PtfxAuthor.CopyEffectInto(doc, src, p.NewEffectName_T6 + "_copy");
                if (copy != null) PtfxAuthor.RemoveEffect(doc, doc.Effects.FirstOrDefault(e => !ReferenceEquals(e, copy)));
                p.SetDocument(doc);
                p.PlayByName(copy?.Name);
                p.Sim.Playing = true;
                p.Status = copy != null
                    ? $"{doc.Name}: {copy.Name} copied from {src.Name}"
                    : "could not copy " + src.Name;
                Console.WriteLine($"PTFXCOPY {src.Name} -> {doc.Name}/{copy?.Name} " +
                                  $"({copy?.Emitters.Count ?? 0} emitters)");
            }
            catch (Exception ex) { p.Status = "could not copy the effect: " + ex.Message; }
        }

        private void LoadSheetLibrary_T6(ParticlePanel p)
        {
            p.SheetLibrary_T6.Clear();
            if (gameFiles == null || !gameFiles.Ready)
            {
                p.SheetLibraryNote_T6 = "the game archives are not open - the puff below still works";
                return;
            }
            var asset = PtfxAuthor.Clean(p.SheetAsset_T6, "core");
            var list = p.GameYpts ?? PtfxGameIndex.ListYpts(gameFiles);
            var paths = list.Where(e => string.Equals(e.Name, asset, StringComparison.OrdinalIgnoreCase))
                            .Select(e => e.Path).Reverse().ToList();
            foreach (var path in paths)
            {
                try
                {
                    var doc = PtfxDocument.FromGame(gameFiles, asset, path);
                    var texs = doc?.PtxList?.TextureDictionary?.Textures?.data_items;
                    if (texs == null) continue;
                    foreach (var t in texs) if (t?.Name != null) p.SheetLibrary_T6.Add(t);
                    break;
                }
                catch { }
            }
            p.SheetLibraryNote_T6 = p.SheetLibrary_T6.Count > 0
                ? $"{p.SheetLibrary_T6.Count} sheets in {asset}.ypt"
                : $"no .ypt called {asset} in this install";
        }

        private void ImportSheetDialog_V18(ParticlePanel p, int emitterIndex)
        {
            var em = p?.Sim?.Effect?.Emitters?.ElementAtOrDefault(emitterIndex);
            if (em == null) { if (p != null) p.Status = "pick an emitter first"; return; }

            using var dlg = new System.Windows.Forms.OpenFileDialog
            {
                Title = "An image for this emitter's sprite",
                Filter = Editor.ImageImport_V18.Filter,
                InitialDirectory = System.IO.Directory.Exists(sheetImportDir_V18 ?? "") ? sheetImportDir_V18 : null,
            };
            if (dlg.ShowDialog(this) != System.Windows.Forms.DialogResult.OK) return;
            sheetImportDir_V18 = System.IO.Path.GetDirectoryName(dlg.FileName);

            var tex = Editor.ImageImport_V18.Load_V18(dlg.FileName, out var err);
            if (tex == null)
            {
                p.Status = "could not read that image: " + err;
                Console.WriteLine("PTFXIMPORT failed " + dlg.FileName + ": " + err);
                return;
            }

            tex.Name = UniqueSheetName_V18(p.Doc, tex.Name);
            tex.NameHash = JenkHash.GenHash(tex.Name);

            PtfxAuthor.SetSheet(p.Doc, em, tex);
            if (!p.SheetLibrary_T6.Any(t => string.Equals(t?.Name, tex.Name, StringComparison.OrdinalIgnoreCase)))
                p.SheetLibrary_T6.Add(tex);
            p.TouchFromTimeline(true);
            p.Status = $"sheet {tex.Name} - {Editor.ImageImport_V18.Describe_V18(tex)}, embedded in this .ypt";
            Console.WriteLine($"PTFXIMPORT {dlg.FileName} -> {tex.Name} {Editor.ImageImport_V18.Describe_V18(tex)}");
        }

        private string sheetImportDir_V18;

        internal static string UniqueSheetName_V18(PtfxDocument doc, string want)
        {
            var have = doc?.PtxList?.TextureDictionary?.Textures?.data_items;
            if (have == null || string.IsNullOrEmpty(want)) return want;
            bool Taken(string n) => have.Any(t => string.Equals(t?.Name, n, StringComparison.OrdinalIgnoreCase));
            if (!Taken(want)) return want;
            for (int i = 2; i < 1000; i++)
            {
                var c = want + "_" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!Taken(c)) return c;
            }
            return want;
        }

        private Texture DefaultSheet_T6(ParticlePanel p)
        {
            var have = p.SheetLibrary_T6.FirstOrDefault(t =>
                t?.Name != null && t.Name.IndexOf("smoke", StringComparison.OrdinalIgnoreCase) >= 0);
            if (have != null) return have;
            var em = p.Sim.Effect?.Emitters.FirstOrDefault();
            return PtfxAuthor.EmitterSheet(em);
        }

        private void RunPtfxAuthorScript_T6(ParticlePanel p)
        {
            var script = Environment.GetEnvironmentVariable("RLE_PTFXEDIT");
            if (string.IsNullOrWhiteSpace(script)) return;
            var eff = p.EditedEffect;
            var em = eff?.Emitters.FirstOrDefault();
            var er = em?.EmitterRule;
            var pr = em?.ParticleRule;
            if (er == null || pr == null) return;

            foreach (var part in script.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split('=', 2);
                if (kv.Length != 2) continue;
                var key = kv[0].Trim().ToLowerInvariant();
                if (key == "sheet")
                {
                    var wanted = kv[1].Trim();
                    var tex = p.SheetLibrary_T6.FirstOrDefault(t =>
                        string.Equals(t?.Name, wanted, StringComparison.OrdinalIgnoreCase));
                    if (tex != null) { PtfxAuthor.SetSheet(p.Doc, em, tex); Console.WriteLine("PTFXSHEET " + tex.Name); }
                    else Console.WriteLine($"PTFXSHEET '{wanted}' is not in {p.SheetAsset_T6}.ypt " +
                                           $"({p.SheetLibrary_T6.Count} sheets loaded)");
                    continue;
                }
                var nums = kv[1].Split(',').Select(s =>
                    float.TryParse(s.Trim(), System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : 0f).ToArray();
                if (nums.Length == 0) continue;
                switch (key)
                {
                    case "rate":
                        SetFirstKey_T6(PtfxKeyframes.Find(er.KeyframeProps, "ptxemitterrule:m_spawnrateovertimekfp"),
                                       nums[0], nums.Length > 1 ? nums[1] : nums[0] * 1.3f);
                        break;
                    case "life":
                        SetFirstKey_T6(PtfxKeyframes.Find(er.KeyframeProps, "ptxemitterrule:m_particlelifekfp"),
                                       nums[0], nums.Length > 1 ? nums[1] : nums[0] * 1.3f);
                        break;
                    case "speed":
                        SetFirstKey_T6(PtfxKeyframes.Find(er.KeyframeProps, "ptxemitterrule:m_speedscalarkfp"),
                                       nums[0], nums.Length > 1 ? nums[1] : nums[0] * 1.2f);
                        break;
                    case "size":
                        foreach (var sz in pr.AllBehaviours?.data_items?.OfType<ParticleBehaviourSize>()
                                           ?? Enumerable.Empty<ParticleBehaviourSize>())
                        {
                            ScaleCurve_T6(sz.WhdMinKFP, nums[0], nums.Length > 1 ? nums[1] : nums[0] * 3f, 1f);
                            ScaleCurve_T6(sz.WhdMaxKFP, nums[0], nums.Length > 1 ? nums[1] : nums[0] * 3f, 1.3f);
                        }
                        break;
                    case "colour":
                        if (nums.Length < 3) break;
                        foreach (var c in pr.AllBehaviours?.data_items?.OfType<ParticleBehaviourColour>()
                                          ?? Enumerable.Empty<ParticleBehaviourColour>())
                        {
                            TintCurve_T6(c.RGBAMinKFP, nums[0], nums[1], nums[2], 1f);
                            TintCurve_T6(c.RGBAMaxKFP, nums[0], nums[1], nums[2], 1.25f);
                        }
                        break;
                    case "blend":
                        pr.BlendSet = (int)nums[0];
                        break;
                    case "emitters":
                        for (int i = 1; i < (int)nums[0] && i < 8; i++)
                            PtfxAuthor.AddEmitter(p.Doc, p.EditedEffect, eff.Name + "_extra" + i, null);
                        break;
                }
            }
            p.SelectEffect(0);
            p.Sim.Playing = true;
            Console.WriteLine("PTFXEDIT applied: " + script);
            SaveAndReopen_T6(p);
        }

        private void SaveAndReopen_T6(ParticlePanel p)
        {
            var path = Environment.GetEnvironmentVariable("RLE_PTFXSAVE");
            if (string.IsNullOrWhiteSpace(path) || p.Doc == null) return;
            try
            {
                var name = p.Sim.Effect?.Name;
                var written = p.Doc.Save(path.Trim());
                var size = new System.IO.FileInfo(written).Length;
                var back = PtfxDocument.FromFile(written);
                p.SetDocument(back);
                if (name != null) p.PlayByName(name);
                p.Sim.Playing = true;
                var eff = p.Sim.Effect;
                Console.WriteLine($"PTFXSAVE wrote {written} ({size:N0} bytes) and reopened it: " +
                                  $"{back.Effects.Count} effect(s), playing {eff?.Name} with " +
                                  $"{eff?.Emitters.Count ?? 0} emitter(s), sheet " +
                                  PtfxAuthor.EmitterSheet(eff?.Emitters.FirstOrDefault())?.Name);
                p.Status = $"saved and reopened {System.IO.Path.GetFileName(written)} ({size:N0} bytes)";
                WarmParticles_N4(1.5f);
            }
            catch (Exception ex)
            {
                Console.WriteLine("PTFXSAVE failed: " + ex.Message);
                p.Status = "save failed: " + ex.Message;
            }
        }

        private static void SetFirstKey_T6(ParticleKeyframeProp p, float x, float y)
        {
            var v = p?.Values?.data_items;
            if (v == null || v.Length == 0) return;
            v[0].KeyframeValue = new SDX.Vector4(x, y, v[0].KeyframeValue.Z, v[0].KeyframeValue.W);
        }

        private static void ScaleCurve_T6(ParticleKeyframeProp p, float start, float end, float spread)
        {
            var v = p?.Values?.data_items;
            if (v == null || v.Length == 0) return;
            for (int i = 0; i < v.Length; i++)
            {
                float f = v.Length == 1 ? 0f : i / (float)(v.Length - 1);
                float w = (start + (end - start) * f) * spread;
                v[i].KeyframeValue = new SDX.Vector4(w, w, v[i].KeyframeValue.Z, v[i].KeyframeValue.W);
            }
        }

        private static void TintCurve_T6(ParticleKeyframeProp p, float r, float g, float b, float boost)
        {
            var v = p?.Values?.data_items;
            if (v == null) return;
            for (int i = 0; i < v.Length; i++)
                v[i].KeyframeValue = new SDX.Vector4(Math.Min(1f, r * boost), Math.Min(1f, g * boost),
                                                     Math.Min(1f, b * boost), v[i].KeyframeValue.W);
        }

        private void ServiceParticleProto_T6()
        {
            if (ptfxProtoDone_T6) return;
            var want = Environment.GetEnvironmentVariable("RLE_PTFXPROTO");
            if (string.IsNullOrWhiteSpace(want)) return;
            if (gameFiles == null || !gameFiles.Ready) return;
            ptfxProtoDone_T6 = true;
            EnsureParticles_N4();
            foreach (var fx in want.Split(',', StringSplitOptions.RemoveEmptyEntries))
                ProtoDump_T6(fx.Trim());
        }

        private void ProtoDump_T6(string fx)
        {
            PlayEffectByName_N4(fx);
            var eff = Ptfx?.Sim?.Effect;
            var doc = Ptfx?.Doc;
            if (eff == null) { Console.WriteLine("PTFXPROTO " + fx + ": not found"); return; }
            var er = eff.Rule;
            Console.WriteLine($"PTFXPROTO list name={doc?.PtxList?.Name?.Value} " +
                              $"texdict={doc?.PtxList?.TextureDictionary?.Textures?.data_items?.Length ?? -1} " +
                              $"drawdict={doc?.PtxList?.DrawableDictionary?.Drawables?.data_items?.Length ?? -1}");
            Console.WriteLine($"PTFXPROTO effect {eff.Name} VFT=0x{er.VFT:X8} VFT2=0x{er.VFT2:X8} " +
                              $"unk8={er.Unknown_8h} ref={er.RefCount} fileVer={er.FileVersion} " +
                              $"effectList=0x{er.EffectList:X} evCount={er.EventEmittersCount} evCap={er.EventEmittersCapacity} " +
                              $"loops={er.NumLoops} sortByDist={er.SortEventsByDistance} drawList={er.DrawListID} " +
                              $"shortLived={er.IsShortLived} noShadows={er.HasNoShadows} " +
                              $"preUpd={er.PreUpdateTime}/{er.PreUpdateTimeInterval} dur={er.DurationMin}..{er.DurationMax} " +
                              $"play={er.PlaybackRateScalarMin}..{er.PlaybackRateScalarMax} " +
                              $"vpCull={er.ViewportCullingMode}/{er.RenderWhenViewportCulled}{er.UpdateWhenViewportCulled}{er.EmitWhenViewportCulled} " +
                              $"distCull={er.DistanceCullingMode}/{er.RenderWhenDistanceCulled}{er.UpdateWhenDistanceCulled}{er.EmitWhenDistanceCulled} " +
                              $"cullSphere={er.ViewportCullingSphereOffset} r={er.ViewportCullingSphereRadius} pad02=0x{er.padding02:X} " +
                              $"fade={er.DistanceCullingFadeDist} cull={er.DistanceCullingCullDist} " +
                              $"lodEvo={er.LodEvoDistanceMin}..{er.LodEvoDistanceMax} " +
                              $"coll={er.CollisionRange}/{er.CollisionProbeDistance}/{er.CollisionType} " +
                              $"kfpCount={er.KeyframePropsCount}/{er.KeyframePropsCapacity} tintMax={er.ColourTintMaxEnable} " +
                              $"dataVol={er.UseDataVolume}/{er.DataVolumeType} zoom={er.ZoomLevel} evoList={er.EvolutionList != null}");

            for (int i = 0; i < eff.Emitters.Count; i++)
            {
                var em = eff.Emitters[i];
                var ev = em.Event;
                var emr = em.EmitterRule;
                var pr = em.ParticleRule;
                Console.WriteLine($"PTFXPROTO  ev[{i}] VFT=0x{ev.VFT:X8} idx={ev.Index} type={ev.EventType} " +
                                  $"ratio={ev.StartRatio}..{ev.EndRatio} evo={ev.EvolutionList != null} " +
                                  $"emName={ev.EmitterRuleName?.Value} prName={ev.ParticleRuleName?.Value}");
                if (emr != null)
                    Console.WriteLine($"PTFXPROTO  emr[{i}] VFT=0x{emr.VFT:X8} unk4={emr.Unknown_4h} ref={emr.RefCount} " +
                                      $"fileVer={emr.FileVersion} oneShot={emr.IsOneShot} " +
                                      $"kfpCount={emr.KeyframePropsCount1}/{emr.KeyframePropsCount2} " +
                                      $"kfpNames=[{string.Join(" ", emr.KeyframeProps.Select(k => k?.Name.ToString() ?? "-"))}] " +
                                      $"creation={emr.CreationDomainObj?.DomainType.ToString() ?? "-"} " +
                                      $"(ws={emr.CreationDomainObj?.IsWorldSpace} pr={emr.CreationDomainObj?.IsPointRelative} " +
                                      $"cr={emr.CreationDomainObj?.IsCreationRelative} tr={emr.CreationDomainObj?.IsTargetRelatve} " +
                                      $"VFT=0x{emr.CreationDomainObj?.VFT:X8} idx={emr.CreationDomainObj?.Index} ver={emr.CreationDomainObj?.FileVersion}) " +
                                      $"target={emr.TargetDomainObj?.DomainType.ToString() ?? "-"} " +
                                      $"attractor={emr.AttractorDomainObj?.DomainType.ToString() ?? "-"}");
                if (pr == null) continue;
                Console.WriteLine($"PTFXPROTO  pr[{i}] VFT=0x{pr.VFT:X8} VFT2=0x{pr.VFT2:X8} VFT3=0x{pr.VFT3:X8} " +
                                  $"ref={pr.RefCount} cull={pr.CullMode} blend={pr.BlendSet} light={pr.LightingMode} " +
                                  $"dw={pr.DepthWrite} dt={pr.DepthTest} ab={pr.AlphaBlend} fileVer={pr.FileVersion} " +
                                  $"texFrame={pr.TexFrameIDMin}..{pr.TexFrameIDMax} " +
                                  $"shader={pr.ShaderFile?.Value} tech={pr.ShaderTechnique?.Value} " +
                                  $"techID={pr.ShaderTemplateTechniqueID} shHash=0x{pr.ShaderTemplateHashName.Hash:X8} " +
                                  $"diffuse={pr.DiffuseMode} proj={pr.ProjectionMode} lit={pr.IsLit} soft={pr.IsSoft} " +
                                  $"screen={pr.IsScreenSpace} refract={pr.IsRefract} normspec={pr.IsNormalSpec} " +
                                  $"inSync={pr.IsDataInSync} sort={pr.SortType} drawType={pr.DrawType} flags={pr.Flags} " +
                                  $"rtFlags={pr.RuntimeFlags} biasLinks={pr.BiasLinks?.data_items?.Length ?? 0} " +
                                  $"drawables={pr.Drawables?.data_items?.Length ?? 0} " +
                                  $"spawnAt={(pr.EffectSpawnerAtRatio != null ? "0x" + pr.EffectSpawnerAtRatio.VFT.ToString("X8") : "-")} " +
                                  $"spawnColl={(pr.EffectSpawnerOnCollision != null ? "0x" + pr.EffectSpawnerOnCollision.VFT.ToString("X8") : "-")}");
                Console.WriteLine($"PTFXPROTO  pr[{i}] all=[{Names_T6(pr.AllBehaviours)}] init=[{Names_T6(pr.InitBehaviours)}] " +
                                  $"upd=[{Names_T6(pr.UpdateBehaviours)}] fin=[{Names_T6(pr.UpdateFinalizeBehaviours)}] " +
                                  $"draw=[{Names_T6(pr.DrawBehaviours)}]");
                foreach (var b in pr.AllBehaviours?.data_items ?? Array.Empty<ParticleBehaviour>())
                    if (b != null)
                        Console.WriteLine($"PTFXPROTO   beh {b.Type} VFT=0x{b.VFT:X8} unk4={b.Unknown_4h} " +
                                          $"kfp={b.KeyframeProps?.EntriesCount}/{b.KeyframeProps?.EntriesCapacity} " +
                                          $"items={b.KeyframeProps?.data_items?.Length}");
                foreach (var sv in pr.ShaderVars?.data_items ?? Array.Empty<ParticleShaderVar>())
                    if (sv != null)
                        Console.WriteLine($"PTFXPROTO   var {sv.GetType().Name} VFT=0x{sv.VFT:X8} name=0x{sv.Name.Hash:X8} " +
                                          $"type={sv.Type} " +
                                          (sv is ParticleShaderVarTexture t
                                            ? $"id={t.ShaderVarID} kf={t.IsKeyframeable} owns={t.OwnsInfo} ext={t.ExternalReference} " +
                                              $"tex={t.Texture?.Name} texName={t.TextureName?.Value} hash=0x{t.TextureNameHash.Hash:X8}"
                                            : sv is ParticleShaderVarVector v
                                              ? $"id={v.ShaderVarID} value=({v.VectorX},{v.VectorY},{v.VectorZ},{v.VectorW})" : ""));
            }
        }

        private static string Names_T6(ResourcePointerList64<ParticleBehaviour> l) =>
            l?.data_items == null ? "" : string.Join(" ", l.data_items.Where(b => b != null).Select(b => b.Type.ToString()));
    }
}

