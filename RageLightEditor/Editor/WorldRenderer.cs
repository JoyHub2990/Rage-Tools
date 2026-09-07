using System;
using System.Collections.Generic;
using CodeWalker.GameFiles;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor.Editor
{
    public partial class WorldRenderer : IDisposable
    {
        private System.Threading.Thread[] loaders;
        private volatile bool stopping;
        private readonly System.Collections.Concurrent.BlockingCollection<Archetype> loadQueue =
            new System.Collections.Concurrent.BlockingCollection<Archetype>(new System.Collections.Concurrent.ConcurrentQueue<Archetype>());
        private readonly System.Collections.Concurrent.ConcurrentQueue<(uint hash, Archetype arch, DrawableBase drawable, int gen, bool dropped, System.Collections.Generic.Dictionary<CodeWalker.GameFiles.DrawableGeometry, RageLightEditor.Rendering.DecodedGeom_V66> pre)> loaded =
            new System.Collections.Concurrent.ConcurrentQueue<(uint, Archetype, DrawableBase, int, bool, System.Collections.Generic.Dictionary<CodeWalker.GameFiles.DrawableGeometry, RageLightEditor.Rendering.DecodedGeom_V66>)>();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<uint, long> lastAsked =
            new System.Collections.Concurrent.ConcurrentDictionary<uint, long>();
        public int StaleLoadMs = 2000;
        public int LoadsDropped { get; private set; }
        private volatile int resolutionGen;
        private readonly HashSet<uint> loading = new HashSet<uint>();
        private GameFileManager loaderGame;
        public int LoadsPending => loadQueue.Count + loaded.Count;

        public static int LoaderThreadCount =>
            Math.Clamp(Environment.ProcessorCount / 2, 2, 8);

        public int LoadersAlive
        {
            get
            {
                var ls = loaders;
                if (ls == null) return 0;
                int n = 0;
                foreach (var t in ls) if (t != null && t.IsAlive) n++;
                return n;
            }
        }

        private void StartLoader(GameFileManager game)
        {
            loaderGame = game;
            if (loaders != null) return;
            stopping = false;
            int n = LoaderThreadCount;
            var ls = new System.Threading.Thread[n];
            for (int i = 0; i < n; i++)
            {
                ls[i] = new System.Threading.Thread(LoaderProc)
                {
                    IsBackground = true,
                    Name = "WorldDrawables" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Priority = System.Threading.ThreadPriority.Normal,
                };
            }
            loaders = ls;
            foreach (var t in ls) t.Start();
        }

        public string LoaderStatus =>
            $"threads {LoadersAlive}/{(loaders == null ? 0 : loaders.Length)} queue {loadQueue.Count} loaded {loaded.Count} loading {loading.Count} errors {LoaderErrors} " +
            $"tex thread {(texLoader == null ? "not started" : texLoader.IsAlive ? "alive" : "DEAD")} texQueue {texQueue.Count} prefetch {prefetch.Count} awaited {awaited.Count} stale {StaleLoadsDropped} dropped(unwanted) {LoadsDropped} texDeferred {BuildsDeferredForTextures} builtWithoutPrefetch {BuildsWithoutPrefetch} slowestBuild [{SessionSlowestBuildName} {SessionSlowestBuildMs:0} ms]";
        public int LoaderErrors { get; private set; }

        private void LoaderProc()
        {
            while (!stopping)
            {
                Archetype a;
                try { a = loadQueue.Take(); }
                catch { return; }
                if (a == null || stopping) continue;
                int gen = resolutionGen;
                if (StaleLoadMs > 0 && lastAsked.TryGetValue(a.Hash, out long asked) && Environment.TickCount64 - asked > StaleLoadMs)
                {
                    try { loaded.Enqueue((a.Hash, a, null, gen, true, null)); } catch { LoaderErrors++; }
                    continue;
                }
                DrawableBase d = null;
                try { d = loaderGame?.GetDrawable(a.Hash, out _); }
                catch { d = null; LoaderErrors++; }
                System.Collections.Generic.Dictionary<CodeWalker.GameFiles.DrawableGeometry, RageLightEditor.Rendering.DecodedGeom_V66> pre = null;
                if (d != null) { try { pre = RageLightEditor.Rendering.ModelRenderer.PrecomputeDrawable_V66(d); } catch { pre = null; } }
                try { loaded.Enqueue((a.Hash, a, d, gen, false, pre)); } catch { LoaderErrors++; }
                if (d != null && loaderGame != null) { try { texQueue.Add((d, a)); StartTexLoader(); } catch { } }
            }
        }

        public int TexturesPrefetched;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<uint, byte> texPrefetched =
            new System.Collections.Concurrent.ConcurrentDictionary<uint, byte>();
        public int MaxTexDeferFrames = 45;
        private readonly Dictionary<uint, int> texDefers = new Dictionary<uint, int>();
        public int BuildsDeferredForTextures, BuildsWithoutPrefetch;
        private readonly System.Collections.Concurrent.BlockingCollection<(DrawableBase d, Archetype a)> texQueue =
            new System.Collections.Concurrent.BlockingCollection<(DrawableBase, Archetype)>(new System.Collections.Concurrent.ConcurrentQueue<(DrawableBase, Archetype)>());
        private System.Threading.Thread texLoader;
        private void StartTexLoader()
        {
            if (texLoader != null) return;
            texLoader = new System.Threading.Thread(() =>
            {
                while (!stopping)
                {
                    (DrawableBase d, Archetype a) item;
                    try { item = texQueue.Take(); } catch { return; }
                    if (stopping) return;
                    try { PrefetchTextures(item.d, item.a); } catch { }
                    if (item.a != null) texPrefetched[item.a.Hash] = 1;
                }
            })
            { IsBackground = true, Name = "WorldTextures", Priority = System.Threading.ThreadPriority.BelowNormal };
            texLoader.Start();
        }
        private void PrefetchTextures(DrawableBase d, Archetype a)
        {
            try
            {
                uint txd = a?.TextureDict ?? 0;
                var models = d.AllModels;
                if (models == null) return;
                var hi = d.DrawableModels?.High ?? models;
                foreach (var m in hi)
                {
                    var geoms = m?.Geometries;
                    if (geoms == null) continue;
                    foreach (var g in geoms)
                    {
                        var plist = g?.Shader?.ParametersList?.Parameters;
                        if (plist == null) continue;
                        foreach (var p in plist)
                        {
                            if (!(p?.Data is TextureBase tb)) continue;
                            if (tb is Texture gt && gt.Data?.FullData != null) continue;
                            if (tb.NameHash == 0) continue;
                            loaderGame.FindTexture(tb.NameHash, txd);
                            TexturesPrefetched++;
                        }
                    }
                }
            }
            catch { }
        }

        private readonly Dictionary<uint, RenderModel> byArchetype = new Dictionary<uint, RenderModel>();
        private readonly Dictionary<uint, int> archetypeUsed = new Dictionary<uint, int>();
        private readonly Dictionary<uint, bool> archetypeFailed = new Dictionary<uint, bool>();
        private readonly Dictionary<YmapEntityDef, List<RenderMesh>> byEntity =
            new Dictionary<YmapEntityDef, List<RenderMesh>>();
        private int frame;

        public readonly RenderModel Model = new RenderModel { Name = "World" };
        public IEnumerable<KeyValuePair<YmapEntityDef, List<RenderMesh>>> LiveInstances => byEntity;

        public int BuildBudget = 24;
        public double BuildBudgetMs = 2.5;
        public double EffectiveBuildBudgetMs = 2.5;
        private readonly System.Diagnostics.Stopwatch buildClock = new System.Diagnostics.Stopwatch();

        public bool IsBuilt(Archetype a)
        {
            if (a == null) return true;
            walkAsked.Add(a.Hash);
            if (byArchetype.ContainsKey(a.Hash) || archetypeFailed.ContainsKey(a.Hash))
            {
                archetypeUsed[a.Hash] = frame;
                return true;
            }
            if (!loading.Contains(a.Hash)) prefetch[a.Hash] = a;
            else lastAsked[a.Hash] = Environment.TickCount64;
            awaited.Add(a.Hash);
            return false;
        }
        private readonly HashSet<uint> awaited = new HashSet<uint>();
        private HashSet<uint> walkAsked = new HashSet<uint>(), walkAskedPrev = new HashSet<uint>();
        public void WalkStarted()
        {
            var t = walkAskedPrev; walkAskedPrev = walkAsked; walkAsked = t; walkAsked.Clear();
        }
        public bool WaitAnswerChanged { get; private set; }
        public void ClearWaitAnswer() => WaitAnswerChanged = false;
        private void Answered(uint hash) { if (awaited.Remove(hash)) WaitAnswerChanged = true; }
        private readonly Dictionary<uint, Archetype> prefetch = new Dictionary<uint, Archetype>();
        private readonly List<uint> prefetchDone = new List<uint>();

        public int PrefetchQueueGuard_S5 = 192;
        private static readonly bool prefetchGuardOff_S5 = Environment.GetEnvironmentVariable("RLE_NOPREFETCHGUARD") == "1";
        public int PrefetchHeld_S5 { get; private set; }

        private void ServicePrefetch(GameFileManager game, ModelRenderer builder)
        {
            if (prefetch.Count == 0) return;
            if (!prefetchGuardOff_S5 && PrefetchQueueGuard_S5 > 0 && loadQueue.Count > PrefetchQueueGuard_S5) { PrefetchHeld_S5++; return; }
            prefetchDone.Clear();
            int asked = 0;
            foreach (var kv in prefetch)
            {
                if (byArchetype.ContainsKey(kv.Key) || archetypeFailed.ContainsKey(kv.Key)) { prefetchDone.Add(kv.Key); continue; }
                if (asked >= 96) break;
                EnsureArchetype(kv.Value, game, builder);
                asked++;
                prefetchDone.Add(kv.Key);
            }
            foreach (var h in prefetchDone) prefetch.Remove(h);
        }

        private RenderModel EnsureArchetype(Archetype arche, GameFileManager game, ModelRenderer builder)
        {
            uint hash = arche.Hash;
            if (archetypeFailed.ContainsKey(hash)) return null;
            if (byArchetype.TryGetValue(hash, out var baseModel)) return baseModel;
            lastAsked[hash] = Environment.TickCount64;
            if (loading.Contains(hash)) return null;
            StartLoader(game);
            loading.Add(hash);
            loadQueue.Add(arche);
            return null;
        }

        private void ServiceLoaded(ModelRenderer builder)
        {
            int cycle = loaded.Count;
            while (cycle-- > 0 && loaded.TryPeek(out var item))
            {
                if (BuiltThisFrame >= BuildBudget) return;
                if (BuiltThisFrame > 0 && buildClock.Elapsed.TotalMilliseconds > EffectiveBuildBudgetMs) return;
                loaded.TryDequeue(out item);
                if (item.dropped) { loading.Remove(item.hash); LoadsDropped++; lastAsked.TryRemove(item.hash, out _); continue; }
                if (MaxTexDeferFrames > 0 && item.drawable != null && item.gen == resolutionGen && texLoader != null && texLoader.IsAlive && !texPrefetched.ContainsKey(item.hash))
                {
                    texDefers.TryGetValue(item.hash, out int defers);
                    if (defers < MaxTexDeferFrames)
                    {
                        texDefers[item.hash] = defers + 1;
                        BuildsDeferredForTextures++;
                        loaded.Enqueue(item);
                        continue;
                    }
                    BuildsWithoutPrefetch++;
                }
                texDefers.Remove(item.hash);
                texPrefetched.TryRemove(item.hash, out _);
                loading.Remove(item.hash);
                lastAsked.TryRemove(item.hash, out _);
                if (item.gen != resolutionGen) { StaleLoadsDropped++; continue; }
                if (byArchetype.ContainsKey(item.hash) || archetypeFailed.ContainsKey(item.hash)) continue;
                BuiltThisFrame++;
                try
                {
                    if (item.drawable == null) { archetypeFailed[item.hash] = true; Answered(item.hash); continue; }
                    try { Lights.Register(item.hash, item.drawable); } catch { }
                    NoteAnimCandidate_U7(item.arch, item.drawable);
                    builder.TextureContext = item.arch?.TextureDict ?? 0;
                    builder.AssetNameContext = item.arch?._BaseArchetypeDef.assetName ?? 0;
                    builder.Precomputed_V66 = item.pre;
                    double b0 = buildClock.Elapsed.TotalMilliseconds;
                    var model = builder.BuildFromDrawable(item.drawable, item.arch?.Name ?? item.hash.ToString());
                    double bms = buildClock.Elapsed.TotalMilliseconds - b0;
                    if (bms > SlowestBuildMs) { SlowestBuildMs = bms; SlowestBuildName = item.arch?.Name ?? item.hash.ToString(); }
                    if (bms > SessionSlowestBuildMs) { SessionSlowestBuildMs = bms; SessionSlowestBuildName = SlowestBuildName + $" ({model?.Meshes.Count ?? 0} meshes)"; }
                    builder.TextureContext = 0;
                    builder.AssetNameContext = 0;
                    builder.Precomputed_V66 = null;
                    if (model == null || model.Meshes.Count == 0) { archetypeFailed[item.hash] = true; Answered(item.hash); continue; }
                    byArchetype[item.hash] = model;
                    archetypeUsed[item.hash] = frame;
                    everBuilt.Add(item.hash);
                    Answered(item.hash);
                    try { Lights.Register(item.hash, item.drawable); } catch { }
                }
                catch { archetypeFailed[item.hash] = true; Answered(item.hash); }
            }
        }

        public int MaxLiveEntities = 20000;

        public int MaxArchetypes = 6000;
        public int RecentFrames = 600;
        public int EffectiveMaxArchetypes { get; private set; } = 6000;
        public int WorkingSetArchetypes { get; private set; }
        public bool AdaptiveCap = true;
        private int WorkingSet()
        {
            int n = 0;
            foreach (var kv in archetypeUsed) if (frame - kv.Value < RecentFrames) n++;
            return n;
        }
        private void UpdateEffectiveCap()
        {
            WorkingSetArchetypes = WorkingSet();
            EffectiveMaxArchetypes = AdaptiveCap
                ? Math.Max(MaxArchetypes, (int)(WorkingSetArchetypes * 1.2))
                : MaxArchetypes;
        }

        public void ReleaseAllModels()
        {
            ClearInstances();
            foreach (var m in byArchetype.Values) m.Dispose();
            ModelsReleased += byArchetype.Count;
            byArchetype.Clear();
            resolutionGen++;
            while (loaded.TryDequeue(out var stale)) loading.Remove(stale.hash);
            lastAsked.Clear();
            texDefers.Clear();
            texPrefetched.Clear();
            archetypeUsed.Clear();
            archetypeFailed.Clear();
            awaited.Clear();
            prefetch.Clear();
            Lights.Clear();
            WaitAnswerChanged = true;
        }

        public readonly WorldLights Lights = new WorldLights();

        public RenderModel PeekModel(uint hash) => byArchetype.TryGetValue(hash, out var m) ? m : null;
        public bool IsFailed(uint archHash) => archetypeFailed.ContainsKey(archHash);
        public bool IsLoading(uint archHash) => loading.Contains(archHash);
        public bool WasBuilt(uint archHash) => everBuilt.Contains(archHash);
        private readonly HashSet<uint> everBuilt = new HashSet<uint>();
        public int ArchetypesLoaded => byArchetype.Count;
        public int EntitiesLive => byEntity.Count;
        public bool HasInstances(YmapEntityDef e) => e != null && byEntity.ContainsKey(e);
        public int InstancesOfYmap(YmapFile y)
        {
            if (y == null) return 0;
            int n = 0;
            foreach (var kv in byEntity) if (ReferenceEquals(kv.Key?.Ymap, y)) n++;
            return n;
        }
        public int ArchetypesFailed => archetypeFailed.Count;
        public long ModelsReleased { get; private set; }
        public int StaleLoadsDropped { get; private set; }
        public int MeshesDrawn { get; private set; }
        public double BuildMs, PlaceMs;
        public double SlowestBuildMs, SessionSlowestBuildMs;
        public string SlowestBuildName = "", SessionSlowestBuildName = "";
        public int BuiltThisFrame { get; private set; }

        public void Update(IReadOnlyList<YmapEntityDef> visible, GameFileManager game,
                           ModelRenderer builder, BoundingFrustum frustum, bool cull,
                           IReadOnlyList<float> fades = null)
        {
            UpdateAnimations_U7(game);
            Model.Meshes.Clear();
            BuiltThisFrame = 0;
            MeshesDrawn = 0;
            BeginMirrorExtras_U4();
            SlowestBuildMs = 0; SlowestBuildName = "";
            if (visible == null || game == null || builder == null) return;
            frame++;
            buildClock.Restart();
            int backlog = loaded.Count + loading.Count;
            EffectiveBuildBudgetMs = backlog > 400 ? BuildBudgetMs * 4 : backlog > 100 ? BuildBudgetMs * 2 : BuildBudgetMs;
            ServiceLoaded(builder);
            BuildMs = buildClock.Elapsed.TotalMilliseconds;
            var placeClock = System.Diagnostics.Stopwatch.StartNew();

            seen.Clear();
            uint lastStamped = 0;
            Model.Meshes.Capacity = Math.Max(Model.Meshes.Capacity, lastMeshCount + 256);

            for (int vi = 0; vi < visible.Count; vi++)
            {
                var e = visible[vi];
                if (e?.Archetype == null) continue;
                seen.Add(e);
                float fade = fades != null && vi < fades.Count ? fades[vi] : 1.0f;

                if (!byEntity.TryGetValue(e, out var instances))
                {
                    instances = BuildEntity(e, game, builder);
                    if (instances == null) continue;
                    byEntity[e] = instances;
                }

                uint ah = e.Archetype.Hash;
                if (ah != lastStamped) { archetypeUsed[ah] = frame; lastStamped = ah; }

                foreach (var m in instances)
                {
                    if (cull && !frustum.Intersects(ref m.WorldSphere)) { NoteCulled_K2(m); NoteFrustumCulled_U4(m, fade); continue; }
                    if (InteriorCull != null && InteriorCull.IsHidden(e, ref m.WorldSphere)) { NoteCulled_K2(m); continue; }
                    m.FadeAlpha = fade;
                    Model.Meshes.Add(m);
                    MeshesDrawn++;
                }
            }

            lastMeshCount = Model.Meshes.Count;
            PlaceMs = placeClock.Elapsed.TotalMilliseconds;
            ServicePrefetch(game, builder);

            bool overCap = false;
            if (frame % 120 == 0) UpdateEffectiveCap();
            if (byArchetype.Count > Math.Min(MaxArchetypes, EffectiveMaxArchetypes) && frame - lastEvictFrame >= EvictInterval)
            {
                UpdateEffectiveCap();
                overCap = byArchetype.Count > EffectiveMaxArchetypes;
                if (!overCap) lastEvictFrame = frame;
            }
            SweepMs = 0; EvictMs = 0;
            if (byEntity.Count > seen.Count + StaleSlack || byEntity.Count > MaxLiveEntities || overCap)
            {
                var sc = System.Diagnostics.Stopwatch.StartNew();
                var drop = dropScratch; drop.Clear();
                foreach (var kv in byEntity) if (!seen.Contains(kv.Key)) drop.Add(kv.Key);
                foreach (var d in drop)
                {
                    foreach (var m in byEntity[d]) ownerByMesh.Remove(m);
                    byEntity.Remove(d);
                    ForgetAnim_U7(d);
                }
                SweepMs = sc.Elapsed.TotalMilliseconds; SweepMsTotal += SweepMs;
            }

            if (overCap)
            {
                var ec = System.Diagnostics.Stopwatch.StartNew();
                EvictArchetypes(); lastEvictFrame = frame;
                EvictMs = ec.Elapsed.TotalMilliseconds; EvictMsTotal += EvictMs;
            }
        }
        public double SweepMs, EvictMs, SweepMsTotal, EvictMsTotal;

        public int StaleSlack = 4000;
        private readonly HashSet<YmapEntityDef> seen = new HashSet<YmapEntityDef>();
        private readonly List<YmapEntityDef> dropScratch = new List<YmapEntityDef>();
        private int lastMeshCount;

        public int EvictInterval = 30;
        private int lastEvictFrame;

        public bool CapBelowWorkingSet { get; private set; }

        private void EvictArchetypes()
        {
            var held = new HashSet<uint>();
            foreach (var kv in byEntity)
                if (kv.Key?.Archetype != null) held.Add(kv.Key.Archetype.Hash);

            var candidates = new List<KeyValuePair<uint, int>>();
            foreach (var kv in byArchetype)
            {
                if (held.Contains(kv.Key)) continue;
                if (walkAsked.Contains(kv.Key) || walkAskedPrev.Contains(kv.Key)) continue;
                archetypeUsed.TryGetValue(kv.Key, out int used);
                if (frame - used < RecentFrames) continue;
                candidates.Add(new KeyValuePair<uint, int>(kv.Key, used));
            }
            candidates.Sort((a, b) => a.Value.CompareTo(b.Value));

            int cap = EffectiveMaxArchetypes;
            CapBelowWorkingSet = held.Count >= cap;
            int target = Math.Min(byArchetype.Count - cap, candidates.Count);
            int done = 0;
            foreach (var c in candidates)
            {
                if (done >= target) break;
                if (!byArchetype.TryGetValue(c.Key, out var model)) continue;
                model.Dispose();
                ModelsReleased++;
                byArchetype.Remove(c.Key);
                archetypeUsed.Remove(c.Key);
                archetypeFailed.Remove(c.Key);
                done++;
            }
            ArchetypesEvicted += done;
        }

        public int ArchetypesEvicted { get; private set; }

        private List<RenderMesh> BuildEntity(YmapEntityDef e, GameFileManager game, ModelRenderer builder)
        {
            uint hash = e.Archetype.Hash;
            if (archetypeFailed.ContainsKey(hash)) return null;

            if (!byArchetype.TryGetValue(hash, out var baseModel))
            {
                baseModel = EnsureArchetype(e.Archetype, game, builder);
                if (baseModel == null) return null;
            }

            var world = Matrix.Scaling(e.Scale)
                      * Matrix.RotationQuaternion(e.Orientation)
                      * Matrix.Translation(e.Position);

            var list = new List<RenderMesh>(baseModel.Meshes.Count);
            foreach (var m in baseModel.Meshes)
            {
                var inst = m.CreateInstance(world);
                inst.TintPaletteIndex = e._CEntityDef.tintValue;
                StampInstance_L2(inst, e);
                StampSun_N2(inst, e);
                ownerByMesh[inst] = e;
                list.Add(inst);
            }
            NoteAnimEntity_U7(e);
            return list;
        }

        public YmapEntityDef OwnerOf(RenderMesh m)
        {
            if (m == null) return null;
            if (ownerByMesh.TryGetValue(m, out var e)) return e;
            return null;
        }
        private readonly Dictionary<RenderMesh, YmapEntityDef> ownerByMesh = new Dictionary<RenderMesh, YmapEntityDef>();

        public void Forget(YmapEntityDef e)
        {
            if (e == null) return;
            if (byEntity.TryGetValue(e, out var old))
                foreach (var m in old) ownerByMesh.Remove(m);
            byEntity.Remove(e);
            ForgetAnim_U7(e);
        }

        public void ClearInstances()
        {
            byEntity.Clear();
            ownerByMesh.Clear();
            Model.Meshes.Clear();
            ClearAnim_U7();
        }

        public void Dispose()
        {
            stopping = true;
            try { loadQueue.CompleteAdding(); } catch { }
            try { texQueue.CompleteAdding(); } catch { }
            byEntity.Clear();
            Model.Meshes.Clear();
            foreach (var m in byArchetype.Values) m.Dispose();
            byArchetype.Clear();
            archetypeUsed.Clear();
            archetypeFailed.Clear();
        }
    }
}

