using CodeWalker.GameFiles;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodeWalker.World
{

    public enum ExternalMapPackState
    {
        Unloaded = 0,
        Loading = 1,
        Loaded = 2,
        Unloading = 3,
        Failed = 4,
    }


    /// <summary>
    /// Precomputed AABB + ymap reference. The per-frame visibility pass reads ONLY these -
    /// no entity iteration, no archetype lookups, no file IO.
    /// </summary>
    public struct ExternalYmapBounds
    {
        public YmapFile Ymap;
        public MetaHash Hash;
        public Vector3 Min;
        public Vector3 Max;
    }


    /// <summary>
    /// Immutable snapshot of a pack's renderable content. The loader thread builds these and
    /// publishes them by a single reference assignment; the render thread only ever reads one.
    /// Nothing in here is mutated after publication, so no lock is needed on the render side.
    /// </summary>
    public class ExternalMapPackSnapshot
    {
        public static readonly ExternalMapPackSnapshot Empty = new ExternalMapPackSnapshot(
            new ExternalYmapBounds[0], new Dictionary<MetaHash, YmapFile>(), new ExternalYmapBounds[0]);

        public readonly ExternalYmapBounds[] Items;
        public readonly Dictionary<MetaHash, YmapFile> ByHash;

        /// <summary>
        /// This pack's ymaps whose short name is also a base-game ymap name. While the pack is shown, each one
        /// replaces the base-game ymap of the same name wherever that appears in the visible set, the way a
        /// streamed ymap replaces the vanilla one in FiveM. Small (tens of entries), so it's walked every frame.
        /// </summary>
        public readonly ExternalYmapBounds[] Overrides;

        public ExternalMapPackSnapshot(ExternalYmapBounds[] items, Dictionary<MetaHash, YmapFile> byhash)
            : this(items, byhash, null)
        { }

        public ExternalMapPackSnapshot(ExternalYmapBounds[] items, Dictionary<MetaHash, YmapFile> byhash, ExternalYmapBounds[] overrides)
        {
            Items = items;
            ByHash = byhash;
            Overrides = overrides ?? new ExternalYmapBounds[0];
        }
    }


    /// <summary>
    /// Result of the (cheap) folder scan: paths only, nothing parsed.
    /// </summary>
    public class ExternalMapPackIndex
    {
        public string FolderPath;
        public List<string> Ymap = new List<string>();
        public List<string> Ytyp = new List<string>();
        public List<string> Ydr = new List<string>();
        public List<string> Ydd = new List<string>();
        public List<string> Ytd = new List<string>();
        public List<string> Yft = new List<string>();
        public int YbnCount;        //counted but deliberately ignored (visual only, no collision)
        public int OtherCount;
        public int DirectoryErrors;

        public int AssetCount { get { return Ydr.Count + Ydd.Count + Ytd.Count + Yft.Count; } }
    }


    public class ExternalMapPackCollision
    {
        public uint Hash;
        public string Kind;     //"ymap/ymap (within pack)" or "ymap/base game"
        public string Name;
        public string PathA;
        public string PathB;

        public override string ToString()
        {
            return Kind + ": " + Name + " [" + Hash.ToString("X8") + "]  " + PathA + "  <->  " + PathB;
        }
    }


    /// <summary>
    /// An archetype that entities in the pack's ymaps reference, but that neither the pack's ytyps nor the
    /// base game define, so those entities have a null Archetype and are never drawn. Almost always a
    /// dependency resource that isn't in the pack folder rather than a bug.
    /// </summary>
    public class ExternalMapPackUnresolvedArchetype
    {
        public uint Hash;
        public string Name;         //from JenkIndex; empty when the name isn't known
        public int PlacedCount;     //entities placed directly in ymaps
        public int InteriorCount;   //entities inside MLO interiors (including entity sets)
        public int YmapCount;       //ymaps with at least one such entity
        public string ExampleYmap;  //first ymap it was seen in
        internal YmapFile LastYmap; //load-time bookkeeping for YmapCount, cleared when the list is finalised

        public int TotalCount { get { return PlacedCount + InteriorCount; } }
        public string DisplayName { get { return string.IsNullOrEmpty(Name) ? "(name unknown)" : Name; } }

        public override string ToString()
        {
            return DisplayName + " [" + Hash.ToString("X8") + "] x" + TotalCount + " (" + PlacedCount + " placed, " + InteriorCount + " interior, in " + YmapCount + " ymaps)";
        }
    }


    /// <summary>
    /// The same (file type, short name) found at more than one path inside one pack. Every lookup is by
    /// short name hash alone, so only one of them can ever be used: the last one registered.
    /// </summary>
    public class ExternalMapPackDuplicateAsset
    {
        public GameFileType Type;
        public uint Hash;
        public string Name;
        /// <summary>All paths with this type + short name, in registration order. The LAST one is the one GameFileCache resolves.</summary>
        public List<string> Paths = new List<string>();
        /// <summary>Null until the load report has compared the files (or a file couldn't be read); true when every copy is byte-identical to the winner.</summary>
        public bool? AllIdentical;

        public string WinningPath { get { return (Paths.Count > 0) ? Paths[Paths.Count - 1] : null; } }
    }


    /// <summary>
    /// Loads a large loose-file (FiveM-style) map pack folder as read-only backdrop scenery.
    ///
    /// Drawables/dictionaries/textures are NOT loaded here - they are registered with the
    /// GameFileCache by path and pulled in lazily by the normal streaming path on first request.
    /// Only ytyps (needed for archetypes) and ymaps (needed for placement) are parsed up front,
    /// on a background thread.
    ///
    /// Ymap name priority in the world view: the user's project content > backdrop pack > base game.
    /// A pack ymap with the same short name as a base-game ymap replaces it while the pack is shown
    /// (as in FiveM); it never replaces a project ymap or another pack's ymap.
    ///
    /// Deliberately free of any WinForms/rendering dependency so it can be exercised headlessly.
    /// </summary>
    public class ExternalMapPack
    {

        public string Name { get; private set; }
        public string FolderPath { get; private set; }

        private GameFileCache GameFileCache;

        //published to the render thread by plain reference assignment - see ExternalMapPackSnapshot
        private volatile ExternalMapPackSnapshot snapshot = ExternalMapPackSnapshot.Empty;

        //render thread only: the snapshot the previous frame used, and scratch space for the one-frame relink pass
        private ExternalMapPackSnapshot lastFrameSnapshot = ExternalMapPackSnapshot.Empty;
        private readonly List<uint> relinkScratch = new List<uint>(64);

        private int stateval = (int)ExternalMapPackState.Unloaded;
        private volatile string statusText = "";
        private Task loadTask = null;
        private CancellationTokenSource cancelSource = null;
        private readonly object loadSyncRoot = new object();

        //load statistics, written by the loader thread, read for reporting once loading is done
        public int IndexedYmapCount { get; private set; }
        public int IndexedYtypCount { get; private set; }
        public int IndexedAssetCount { get; private set; }
        public int RegisteredAssetCount { get; private set; }
        public int LoadedYtypCount { get; private set; }
        public int RegisteredArchetypeCount { get; private set; }
        public int LoadedYmapCount { get; private set; }
        public int FailedYmapCount { get; private set; }
        public int FailedYtypCount { get; private set; }
        public int EntityCount { get; private set; }
        public int DegenerateExtentsCount { get; private set; }
        public int EmptyYmapCount { get; private set; }
        public List<ExternalMapPackCollision> Collisions { get; private set; }
        public List<string> Errors { get; private set; }

        /// <summary>Pack ymaps that replace a base-game ymap of the same name while the pack is shown.</summary>
        public int OverriddenBaseYmapCount { get; private set; }
        /// <summary>Distinct archetype hashes referenced by pack entities that nothing defines.</summary>
        public int UnresolvedArchetypeCount { get; private set; }
        /// <summary>Entities placed directly in pack ymaps whose archetype couldn't be resolved.</summary>
        public int UnresolvedPlacedEntityCount { get; private set; }
        /// <summary>Entities inside MLO interiors (incl. entity sets) whose archetype couldn't be resolved.</summary>
        public int UnresolvedInteriorEntityCount { get; private set; }
        public int UnresolvedEntityCount { get { return UnresolvedPlacedEntityCount + UnresolvedInteriorEntityCount; } }
        /// <summary>Sorted by entity count, highest first.</summary>
        public List<ExternalMapPackUnresolvedArchetype> UnresolvedArchetypes { get; private set; }
        /// <summary>Same type + short name at several paths in this pack. The last registered path wins.</summary>
        public List<ExternalMapPackDuplicateAsset> DuplicateAssets { get; private set; }
        public int DuplicateAssetIdenticalCount { get; private set; }
        /// <summary>Where the last load report was written, or null.</summary>
        public string ReportPath { get; private set; }

        /// <summary>Extra distance (metres) added around each ymap's streaming extents for the visibility test.</summary>
        public float ExtraRange = 0.0f;

        /// <summary>Invoked on the loader thread whenever Status/Progress changes.</summary>
        public Action<ExternalMapPack> StatusChanged;
        /// <summary>Invoked on the loader thread for non-fatal errors.</summary>
        public Action<string> ErrorLog;

        public ExternalMapPackState State { get { return (ExternalMapPackState)stateval; } }
        public bool IsLoaded { get { return stateval == (int)ExternalMapPackState.Loaded; } }
        public bool IsLoading { get { return stateval == (int)ExternalMapPackState.Loading; } }
        public bool IsBusy { get { return (stateval == (int)ExternalMapPackState.Loading) || (stateval == (int)ExternalMapPackState.Unloading); } }
        public string Status { get { return statusText; } }
        public int VisibleCandidateCount { get { return snapshot.Items.Length; } }


        /// <summary>Updates the source folder. Only allowed while nothing is loaded.</summary>
        public bool TrySetFolderPath(string path)
        {
            lock (loadSyncRoot)
            {
                if ((stateval != (int)ExternalMapPackState.Unloaded) && (stateval != (int)ExternalMapPackState.Failed)) return false;
                FolderPath = path;
                return true;
            }
        }


        public ExternalMapPack(string name, string folderPath, GameFileCache cache)
        {
            Name = name;
            FolderPath = folderPath;
            GameFileCache = cache;
            Collisions = new List<ExternalMapPackCollision>();
            Errors = new List<string>();
            UnresolvedArchetypes = new List<ExternalMapPackUnresolvedArchetype>();
            DuplicateAssets = new List<ExternalMapPackDuplicateAsset>();
        }



        #region per-frame visibility (render thread)

        /// <summary>
        /// Per-frame visibility provider. Cost is a linear scan of the precomputed AABB array
        /// (one array element per ymap) plus a dictionary insert per hit, a short parent chain climb
        /// for LOD parents, and one lookup per base-game override (tens). It performs NO entity
        /// iteration, NO archetype resolution, NO allocation and NO file IO - all of that happened once
        /// at load time. Only on the first frame after a publish or unload does it do one extra pass
        /// over the visible ymap set (see WithholdReparentedYmaps).
        /// Must run after the base game and project have filled the dictionary (WorldForm.RenderWorld order).
        /// Safe to call while the pack is still loading, and while it is being unloaded.
        /// </summary>
        public void GetVisibleYmaps(Vector3 campos, Dictionary<MetaHash, YmapFile> ymaps)
        {
            //single volatile read - the snapshot is immutable
            var snap = snapshot;
            GetVisibleYmaps(snap, campos, ExtraRange, GameFileCache, ymaps);

            if (snap != lastFrameSnapshot)
            {
                //a publish or an unload can change which instance a ymap name resolves to (base game <-> pack),
                //which RenderLodManager doesn't handle for children that stay visible across the change
                lastFrameSnapshot = snap;
                WithholdReparentedYmaps(ymaps, relinkScratch);
            }
        }

        /// <summary>Cull + base-game overrides + parent climb against a given snapshot. See GetVisibleYmaps above.</summary>
        public static void GetVisibleYmaps(ExternalMapPackSnapshot snap, Vector3 campos, float range, GameFileCache cache, Dictionary<MetaHash, YmapFile> ymaps)
        {
            if (snap == null) return;
            var items = snap.Items;
            var byhash = snap.ByHash;

            for (int i = 0; i < items.Length; i++)
            {
                ref var item = ref items[i];
                if (campos.X < (item.Min.X - range)) continue;
                if (campos.Y < (item.Min.Y - range)) continue;
                if (campos.Z < (item.Min.Z - range)) continue;
                if (campos.X > (item.Max.X + range)) continue;
                if (campos.Y > (item.Max.Y + range)) continue;
                if (campos.Z > (item.Max.Z + range)) continue;

                //a name that's already there is kept (project ymap, another pack's, or ours via an earlier climb),
                //unless it's a plain base-game ymap - the pack's copy replaces that, as in FiveM
                YmapFile existing;
                if (ymaps.TryGetValue(item.Hash, out existing) && !IsReplaceableBaseGameYmap(existing)) continue;

                AddWithParents(item.Hash, item.Ymap, byhash, cache, ymaps);
            }

            //FiveM replaces a base-game ymap by name wherever the game asks for it, not only where the
            //replacement's own extents are in range. So swap in our copy whenever the base-game ymap is in the
            //set for any reason (base game streaming, or another ymap's parent climb). This also keeps the
            //name->instance mapping constant for as long as the snapshot is, which RenderLodManager relies on.
            var overrides = snap.Overrides;
            for (int i = 0; i < overrides.Length; i++)
            {
                ref var ov = ref overrides[i];
                YmapFile existing;
                if (!ymaps.TryGetValue(ov.Hash, out existing)) continue; //not wanted by anything this frame
                if (existing == ov.Ymap) continue;
                if (!IsReplaceableBaseGameYmap(existing)) continue;
                AddWithParents(ov.Hash, ov.Ymap, byhash, cache, ymaps);
            }
        }

        /// <summary>
        /// Adds a pack ymap and climbs its LOD parent chain - RenderLodManager drops any ymap whose parent
        /// hash is nonzero but absent from the same dictionary. A parent that's already present is kept, except
        /// a plain base-game ymap this pack ships its own copy of: that's replaced by the pack's copy, so pack
        /// children are never linked to the vanilla parent (their parentIndex values index the pack's version).
        /// </summary>
        private static void AddWithParents(MetaHash hash, YmapFile ymap, Dictionary<MetaHash, YmapFile> byhash, GameFileCache cache, Dictionary<MetaHash, YmapFile> ymaps)
        {
            while ((ymap != null) && ymap.Loaded)
            {
                ymaps[hash] = ymap;
                hash = ymap._CMapData.parent;
                if (hash.Hash == 0) break;

                YmapFile own;
                bool hasown = byhash.TryGetValue(hash, out own);
                YmapFile present;
                if (ymaps.TryGetValue(hash, out present))
                {
                    //terminates: anything we replace becomes our own copy, and our own copy always stops the climb
                    if (!hasown || (present == own) || !IsReplaceableBaseGameYmap(present)) break;
                    ymap = own;
                }
                else
                {
                    //parent is ours, or else might be a base game ymap
                    ymap = hasown ? own : ((cache != null) ? cache.GetYmap(hash) : null);
                }
            }
        }

        /// <summary>
        /// The priority rule for a ymap name that's already in the visible set: project > backdrop pack > base game.
        /// True only for a plain base-game ymap - one read from an RPF archive that the user's project isn't holding.
        /// Kept (false) are:
        ///  - any pack's ymap (IsLockedBackdrop);
        ///  - project/loose ymaps: loaded from disk or created in the project, so they carry a synthetic entry
        ///    that isn't in any archive (RpfFileEntry.File == null) - ProjectFile.AddYmapFile + YmapFile.Load;
        ///  - a base-game ymap the user pulled into the project: ProjectForm.AddYmapToProject keeps the very same
        ///    instance, archive entry and all (File != null), but sets HasChanged, and ProjectForm.SaveYmap always
        ///    gives it a FilePath (SetFilePath) before it clears HasChanged. Streamed base-game ymaps
        ///    (GameFileCache.GetYmap -> new YmapFile(archive entry)) never get a FilePath.
        /// </summary>
        public static bool IsReplaceableBaseGameYmap(YmapFile ymap)
        {
            if (ymap == null) return true; //nothing there worth keeping
            if (ymap.IsLockedBackdrop) return false;
            var entry = ymap.RpfFileEntry;
            if ((entry == null) || (entry.File == null)) return false;
            if (ymap.HasChanged) return false;
            if (!string.IsNullOrEmpty(ymap.FilePath)) return false;
            return true;
        }

        /// <summary>
        /// Run on the first frame after the snapshot changes (publish or unload), after this pack has added its
        /// ymaps. RenderLodManager hangs a ymap's entities under its parent's entities only when the ymap is first
        /// added. If the parent instance is then swapped (base game &lt;-&gt; pack) while the child stays in the set,
        /// removing the old parent clears those links, and the child's entities end up in neither RootEntities nor
        /// any child list - invisible until the child happens to leave the set.
        /// So every ymap whose parent instance is about to change (or disappear), plus everything below it in the
        /// set, is left out for this one frame and its stale Parent is cleared: the LOD manager removes it cleanly,
        /// and next frame it comes back and is added fresh under the right parent.
        /// Allocation-free once the scratch list has grown; O(visible ymaps x LOD depth), transition frames only.
        /// Must NOT run every frame: a withheld ymap isn't reconnected by the LOD manager until it's back in the set.
        /// </summary>
        public static void WithholdReparentedYmaps(Dictionary<MetaHash, YmapFile> ymaps, List<uint> scratch)
        {
            if ((ymaps == null) || (scratch == null)) return;
            scratch.Clear();

            foreach (var kvp in ymaps)
            {
                var ymap = kvp.Value;
                if (ymap == null) continue;
                var oldparent = ymap.Parent;
                if (oldparent == null) continue; //never linked - nothing stale
                var phash = ymap._CMapData.parent;
                if (phash.Hash == 0) continue;
                YmapFile newparent;
                if (ymaps.TryGetValue(phash, out newparent) && (newparent == oldparent)) continue;
                //stale: the LOD manager only (re)adds a ymap with a nonzero parent hash when Parent is set, and only
                //reconnects when the parent is present - clearing it makes it wait for the parent instead of hanging
                //its entities under a dead one
                ymap.Parent = null;
                scratch.Add(kvp.Key.Hash);
            }
            if (scratch.Count == 0) return;

            //everything below a withheld ymap would lose its links the same way when that one is removed
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (var kvp in ymaps)
                {
                    var ymap = kvp.Value;
                    if (ymap == null) continue;
                    uint phash = ymap._CMapData.parent.Hash;
                    if (phash == 0) continue;
                    uint hash = kvp.Key.Hash;
                    if (!scratch.Contains(phash) || scratch.Contains(hash)) continue;
                    scratch.Add(hash);
                    grew = true;
                }
            }

            for (int i = 0; i < scratch.Count; i++)
            {
                ymaps.Remove(new MetaHash(scratch[i]));
            }
        }

        #endregion



        #region loading

        /// <summary>
        /// Starts loading on a background thread. Returns false (with Status set) if the pack
        /// can't be loaded right now - most importantly if the game file cache hasn't finished
        /// its scan, since GameFileCache.GetArchetype short-circuits until then and every
        /// entity would end up with a null archetype.
        /// </summary>
        public bool BeginLoad()
        {
            lock (loadSyncRoot)
            {
                if (stateval != (int)ExternalMapPackState.Unloaded && stateval != (int)ExternalMapPackState.Failed)
                {
                    return false; //already loading/loaded/unloading
                }
                if (GameFileCache == null)
                {
                    SetStatus("Game file cache not available.");
                    stateval = (int)ExternalMapPackState.Failed;
                    return false;
                }
                if (!GameFileCache.IsInited)
                {
                    SetStatus("Game files still loading - try again once scanning completes.");
                    stateval = (int)ExternalMapPackState.Failed;
                    return false;
                }
                if (string.IsNullOrEmpty(FolderPath) || !Directory.Exists(FolderPath))
                {
                    SetStatus("Folder not found: " + FolderPath);
                    stateval = (int)ExternalMapPackState.Failed;
                    return false;
                }

                cancelSource = new CancellationTokenSource();
                var token = cancelSource.Token;
                stateval = (int)ExternalMapPackState.Loading;
                SetStatus("Starting...");
                loadTask = Task.Run(() => LoadCore(token));
                return true;
            }
        }


        /// <summary>
        /// Clears the visible set immediately (so the render thread stops referencing pack content
        /// on the very next frame), then finishes tearing down on a background thread.
        /// Never blocks the caller on file IO or on the loader thread.
        /// </summary>
        public bool BeginUnload(Action onDone)
        {
            Task waitfor = null;
            lock (loadSyncRoot)
            {
                if (stateval == (int)ExternalMapPackState.Unloading) return false;
                if (stateval == (int)ExternalMapPackState.Unloaded)
                {
                    if (onDone != null) onDone();
                    return false;
                }
                stateval = (int)ExternalMapPackState.Unloading;
                snapshot = ExternalMapPackSnapshot.Empty; //render thread sees nothing from here on (Publish won't overwrite it)
                if (cancelSource != null) cancelSource.Cancel();
                waitfor = loadTask;
            }

            SetStatus("Unloading...");
            var t = waitfor;
            Task.Run(() =>
            {
                try { if (t != null) t.Wait(); }
                catch { }
                try
                {
                    if (GameFileCache != null) GameFileCache.UnregisterExternalContent(this);
                }
                catch (Exception ex)
                {
                    LogError("Error unloading " + Name + ": " + ex.Message);
                }
                lock (loadSyncRoot)
                {
                    loadTask = null;
                    if (cancelSource != null) { cancelSource.Dispose(); cancelSource = null; }
                    ResetStats();
                    stateval = (int)ExternalMapPackState.Unloaded;
                }
                SetStatus("");
                if (onDone != null) onDone();
            });
            return true;
        }


        /// <summary>
        /// The actual load. Runs entirely off the render thread. Publishes partial snapshots as
        /// ymaps come in so content appears progressively instead of all at the end.
        /// </summary>
        private void LoadCore(CancellationToken token)
        {
            try
            {
                ResetStats();

                SetStatus("Indexing " + Name + "...");
                var index = IndexFolder(FolderPath);
                IndexedYmapCount = index.Ymap.Count;
                IndexedYtypCount = index.Ytyp.Count;
                IndexedAssetCount = index.AssetCount;
                if (token.IsCancellationRequested) return;

                //---- 1. register loose assets by path. NOTHING is read from disk here. ----
                SetStatus(Name + ": registering " + index.AssetCount + " assets...");
                RegisterAssets(index.Ydr, GameFileType.Ydr);
                RegisterAssets(index.Ydd, GameFileType.Ydd);
                RegisterAssets(index.Ytd, GameFileType.Ytd);
                RegisterAssets(index.Yft, GameFileType.Yft);
                if (token.IsCancellationRequested) return;

                //---- 2. ytyps, eagerly - archetypes must exist before any ymap is resolved ----
                for (int i = 0; i < index.Ytyp.Count; i++)
                {
                    if (token.IsCancellationRequested) return;
                    if ((i % 25) == 0) SetStatus(Name + ": ytyps " + i + " / " + index.Ytyp.Count);
                    LoadYtyp(index.Ytyp[i]);
                }
                if (token.IsCancellationRequested) return;

                //---- 3. collision report (short name hash is the only key anything uses) ----
                BuildCollisionReport(index);

                //---- 4. ymaps ----
                //Ones that replace a base-game ymap load first, so they're in the very first published snapshot and
                //the pack's own children are never linked to the vanilla parent in between. Stable, so which of two
                //same-named pack ymaps wins (the later one) doesn't change.
                var overrideHashes = new List<uint>();
                var ymapPaths = OrderBaseGameOverridesFirst(index.Ymap, overrideHashes);
                var overrideSet = new HashSet<uint>(overrideHashes);
                var unresolved = new Dictionary<uint, ExternalMapPackUnresolvedArchetype>();
                var bounds = new List<ExternalYmapBounds>(ymapPaths.Count);
                var byhash = new Dictionary<MetaHash, YmapFile>(ymapPaths.Count);
                int publishevery = 32;
                for (int i = 0; i < ymapPaths.Count; i++)
                {
                    if (token.IsCancellationRequested) return;
                    LoadYmap(ymapPaths[i], bounds, byhash, overrideSet, unresolved);
                    if (((i + 1) % publishevery) == 0)
                    {
                        Publish(bounds, byhash, overrideHashes);
                        SetStatus(Name + ": ymaps " + (i + 1) + " / " + ymapPaths.Count);
                    }
                }
                if (token.IsCancellationRequested) return;

                var finalsnap = Publish(bounds, byhash, overrideHashes);
                OverriddenBaseYmapCount = finalsnap.Overrides.Length;

                //---- 5. load report (load time only - content is already published) ----
                FinishUnresolvedArchetypes(unresolved);
                if (OverriddenBaseYmapCount > 0)
                {
                    LogError(Name + ": replaces " + OverriddenBaseYmapCount + " base-game ymap(s) of the same name while shown (FiveM behaviour; project ymaps still win).");
                }
                if (token.IsCancellationRequested) return;
                SetStatus(Name + ": writing load report...");
                WriteLoadReport(finalsnap, token);
                if (token.IsCancellationRequested) return;

                lock (loadSyncRoot)
                {
                    if (stateval == (int)ExternalMapPackState.Loading)
                    {
                        stateval = (int)ExternalMapPackState.Loaded;
                    }
                }
                var status = Name + ": " + LoadedYmapCount + " ymaps, " + RegisteredArchetypeCount + " archetypes, " + RegisteredAssetCount + " assets";
                if (UnresolvedArchetypeCount > 0)
                {
                    status += " - " + UnresolvedArchetypeCount.ToString("N0", CultureInfo.InvariantCulture) + " archetypes unresolved ("
                        + UnresolvedEntityCount.ToString("N0", CultureInfo.InvariantCulture) + " entities) - missing dependency resources?";
                }
                SetStatus(status);
            }
            catch (Exception ex)
            {
                LogError("Error loading map pack " + Name + ": " + ex.ToString());
                lock (loadSyncRoot)
                {
                    if (stateval == (int)ExternalMapPackState.Loading)
                    {
                        stateval = (int)ExternalMapPackState.Failed;
                    }
                }
                SetStatus(Name + ": load failed - " + ex.Message);
            }
        }


        private ExternalMapPackSnapshot Publish(List<ExternalYmapBounds> bounds, Dictionary<MetaHash, YmapFile> byhash, List<uint> overrideHashes)
        {
            //copies so the published snapshot is never touched again
            var items = bounds.ToArray();
            var dict = new Dictionary<MetaHash, YmapFile>(byhash);
            var overrides = new List<ExternalYmapBounds>(overrideHashes.Count);
            for (int i = 0; i < overrideHashes.Count; i++)
            {
                var hash = new MetaHash(overrideHashes[i]);
                YmapFile ymap;
                if (!dict.TryGetValue(hash, out ymap) || (ymap == null)) continue; //not loaded yet, or failed
                var ov = new ExternalYmapBounds();
                ov.Ymap = ymap;
                ov.Hash = hash;
                ov.Min = ymap._CMapData.streamingExtentsMin;
                ov.Max = ymap._CMapData.streamingExtentsMax;
                overrides.Add(ov);
            }
            var snap = new ExternalMapPackSnapshot(items, dict, overrides.ToArray());
            lock (loadSyncRoot)
            {
                //once BeginUnload has published Empty, a publish that was already in flight must not bring content back
                if (stateval == (int)ExternalMapPackState.Loading)
                {
                    snapshot = snap;
                }
            }
            return snap;
        }


        private void RegisterAssets(List<string> paths, GameFileType type)
        {
            //one call per file type, so the short name hash alone identifies a duplicate here
            var seen = new Dictionary<uint, string>(paths.Count);
            Dictionary<uint, ExternalMapPackDuplicateAsset> dupes = null;
            for (int i = 0; i < paths.Count; i++)
            {
                var path = paths[i];
                var shortname = Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrEmpty(shortname)) continue;
                shortname = shortname.ToLowerInvariant();
                JenkIndex.Ensure(shortname);
                var hash = JenkHash.GenHash(shortname);

                string prev;
                if (seen.TryGetValue(hash, out prev))
                {
                    //report only - the registration below still replaces the earlier one (last registered wins)
                    if (dupes == null) dupes = new Dictionary<uint, ExternalMapPackDuplicateAsset>();
                    ExternalMapPackDuplicateAsset dup;
                    if (!dupes.TryGetValue(hash, out dup))
                    {
                        dup = new ExternalMapPackDuplicateAsset();
                        dup.Type = type;
                        dup.Hash = hash;
                        dup.Name = shortname;
                        dup.Paths.Add(prev);
                        dupes[hash] = dup;
                        DuplicateAssets.Add(dup);
                    }
                    dup.Paths.Add(path);
                }
                seen[hash] = path;

                GameFileCache.RegisterExternalFile(type, hash, path, this);
                RegisteredAssetCount++;
            }
        }


        private void LoadYtyp(string path)
        {
            try
            {
                var data = File.ReadAllBytes(path);
                var ytyp = new YtypFile();
                var name = Path.GetFileName(path);
                var shortname = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                var entry = new RpfResourceFileEntry();
                entry.Name = name;
                entry.NameLower = name.ToLowerInvariant();
                entry.NameHash = JenkHash.GenHash(entry.NameLower);
                entry.ShortNameHash = JenkHash.GenHash(shortname);
                entry.Path = path;
                ytyp.RpfFileEntry = entry;
                ytyp.Name = name;
                ytyp.FilePath = path;
                JenkIndex.Ensure(name);
                JenkIndex.Ensure(shortname);

                ytyp.Load(data);
                LoadedYtypCount++;

                if (ytyp.AllArchetypes != null)
                {
                    for (int i = 0; i < ytyp.AllArchetypes.Length; i++)
                    {
                        var arch = ytyp.AllArchetypes[i];
                        if (arch == null) continue;
                        GameFileCache.RegisterExternalArchetype(arch, this);
                        RegisteredArchetypeCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                FailedYtypCount++;
                LogError("Error loading " + path + ": " + ex.Message);
            }
        }


        private void LoadYmap(string path, List<ExternalYmapBounds> bounds, Dictionary<MetaHash, YmapFile> byhash, HashSet<uint> overrideSet, Dictionary<uint, ExternalMapPackUnresolvedArchetype> unresolved)
        {
            YmapFile ymap = null;
            try
            {
                ymap = ParseYmap(path);
            }
            catch (Exception ex)
            {
                FailedYmapCount++;
                LogError("Error loading " + path + ": " + ex.Message);
                return;
            }
            if (ymap == null) { FailedYmapCount++; return; }

            ExternalYmapBounds b;
            bool fixedextents;
            bool renderable;
            try
            {
                renderable = PrepareYmap(ymap, GameFileCache, unresolved, out b, out fixedextents);
            }
            catch (Exception ex)
            {
                FailedYmapCount++;
                LogError("Error preparing " + path + ": " + ex.Message);
                return;
            }

            LoadedYmapCount++;
            if (fixedextents) DegenerateExtentsCount++;
            EntityCount += (ymap.AllEntities != null) ? ymap.AllEntities.Length : 0;

            if (!renderable)
            {
                //extents still inverted after CalcExtents => nothing in it at all
                EmptyYmapCount++;
                //...but an empty pack ymap with a base-game name still replaces (ie hides) the base-game one, as in
                //FiveM. It's reachable by name only (overrides/parent climb), never by the cull.
                uint hash = (ymap.RpfFileEntry != null) ? ymap.RpfFileEntry.ShortNameHash : 0;
                if ((hash != 0) && (overrideSet != null) && overrideSet.Contains(hash))
                {
                    byhash[new MetaHash(hash)] = ymap;
                }
                return;
            }

            bounds.Add(b);
            byhash[b.Hash] = ymap;
        }


        /// <summary>
        /// Marks the ymap as read-only backdrop, resolves its entity archetypes exactly once
        /// (when a cache is given), repairs degenerate streaming extents, and produces the
        /// cull AABB used by the per-frame visibility pass.
        /// Returns false when the ymap has no usable extents at all (ie nothing to render).
        /// </summary>
        public static bool PrepareYmap(YmapFile ymap, GameFileCache cache, out ExternalYmapBounds bounds, out bool fixedExtents)
        {
            return PrepareYmap(ymap, cache, null, out bounds, out fixedExtents);
        }

        /// <summary>
        /// As above; additionally, when <paramref name="unresolved"/> is given, counts the entities whose
        /// archetype couldn't be resolved into it (see CountUnresolvedArchetypes).
        /// </summary>
        public static bool PrepareYmap(YmapFile ymap, GameFileCache cache, Dictionary<uint, ExternalMapPackUnresolvedArchetype> unresolved, out ExternalYmapBounds bounds, out bool fixedExtents)
        {
            bounds = new ExternalYmapBounds();
            fixedExtents = false;
            if (ymap == null) return false;

            ymap.IsLockedBackdrop = true;

            if (cache != null)
            {
                ymap.InitYmapEntityArchetypes(cache); //done exactly once, here - never per frame
                if (unresolved != null)
                {
                    CountUnresolvedArchetypes(ymap, unresolved);
                }
            }

            if (HasDegenerateExtents(ymap))
            {
                ymap.CalcExtents();
                fixedExtents = true;
            }

            var min = ymap._CMapData.streamingExtentsMin;
            var max = ymap._CMapData.streamingExtentsMax;
            if (!IsFinite(min) || !IsFinite(max)) return false;
            if ((max.X < min.X) || (max.Y < min.Y) || (max.Z < min.Z)) return false;

            bounds.Ymap = ymap;
            bounds.Hash = new MetaHash((ymap.RpfFileEntry != null) ? ymap.RpfFileEntry.ShortNameHash : 0);
            bounds.Min = min;
            bounds.Max = max;
            return true;
        }


        /// <summary>
        /// Counts entities left with a null Archetype after InitYmapEntityArchetypes, grouped by archetype hash:
        /// placed entities, and entities inside MLO interiors whose MLO archetype did resolve (interior entities
        /// plus all entity sets). An MLO instance whose own archetype is missing counts once, as placed.
        /// Load time only.
        /// </summary>
        public static void CountUnresolvedArchetypes(YmapFile ymap, Dictionary<uint, ExternalMapPackUnresolvedArchetype> unresolved)
        {
            if ((ymap == null) || (unresolved == null)) return;
            var ents = ymap.AllEntities;
            if (ents == null) return;
            for (int i = 0; i < ents.Length; i++)
            {
                var ent = ents[i];
                if (ent == null) continue;
                if (ent.Archetype == null)
                {
                    AddUnresolved(unresolved, ent._CEntityDef.archetypeName.Hash, ymap, false);
                    continue;
                }
                if (!ent.IsMlo) continue;
                var mlo = ent.MloInstance;
                if (mlo == null) continue;

                var ients = mlo.Entities;
                if (ients != null)
                {
                    for (int j = 0; j < ients.Length; j++)
                    {
                        var ient = ients[j];
                        if ((ient != null) && (ient.Archetype == null))
                        {
                            AddUnresolved(unresolved, ient._CEntityDef.archetypeName.Hash, ymap, true);
                        }
                    }
                }
                var sets = mlo.EntitySets;
                if (sets != null)
                {
                    for (int s = 0; s < sets.Length; s++)
                    {
                        var sents = (sets[s] != null) ? sets[s].Entities : null;
                        if (sents == null) continue;
                        for (int j = 0; j < sents.Count; j++)
                        {
                            var ient = sents[j];
                            if ((ient != null) && (ient.Archetype == null))
                            {
                                AddUnresolved(unresolved, ient._CEntityDef.archetypeName.Hash, ymap, true);
                            }
                        }
                    }
                }
            }
        }

        private static void AddUnresolved(Dictionary<uint, ExternalMapPackUnresolvedArchetype> unresolved, uint hash, YmapFile ymap, bool interior)
        {
            ExternalMapPackUnresolvedArchetype u;
            if (!unresolved.TryGetValue(hash, out u))
            {
                u = new ExternalMapPackUnresolvedArchetype();
                u.Hash = hash;
                u.ExampleYmap = ymap.FilePath ?? ymap.Name;
                unresolved[hash] = u;
            }
            if (interior) u.InteriorCount++;
            else u.PlacedCount++;
            if (u.LastYmap != ymap)
            {
                u.LastYmap = ymap;
                u.YmapCount++;
            }
        }


        /// <summary>
        /// Parses a loose .ymap into a YmapFile with a synthetic RpfFileEntry carrying the
        /// short name hash everything else keys off. Does not resolve archetypes.
        /// </summary>
        public static YmapFile ParseYmap(string path)
        {
            var data = File.ReadAllBytes(path);
            var name = Path.GetFileName(path);
            var shortname = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();

            var entry = new RpfResourceFileEntry();
            entry.Name = name;
            entry.NameLower = name.ToLowerInvariant();
            entry.NameHash = JenkHash.GenHash(entry.NameLower);
            entry.ShortNameHash = JenkHash.GenHash(shortname);
            entry.Path = path;

            JenkIndex.Ensure(name);
            JenkIndex.Ensure(shortname);

            var ymap = new YmapFile();
            ymap.RpfFileEntry = entry;   //LoadResourceFile carries the name/hashes onto the real entry
            ymap.Name = name;
            ymap.FilePath = path;
            ymap.Load(data);

            if (ymap.RpfFileEntry != null)
            {
                ymap.RpfFileEntry.Path = path;
                if (ymap.RpfFileEntry.ShortNameHash == 0)
                {
                    ymap.RpfFileEntry.ShortNameHash = JenkHash.GenHash(shortname);
                }
            }
            ymap.Name = name;
            return ymap;
        }


        /// <summary>
        /// True when the ymap's streaming extents can't be used for culling: not finite,
        /// inverted, or a degenerate/zero-volume box. FiveM exporters often leave these blank.
        /// </summary>
        public static bool HasDegenerateExtents(YmapFile ymap)
        {
            var min = ymap._CMapData.streamingExtentsMin;
            var max = ymap._CMapData.streamingExtentsMax;
            if (!IsFinite(min) || !IsFinite(max)) return true;
            if ((max.X <= min.X) || (max.Y <= min.Y) || (max.Z <= min.Z)) return true;
            return false;
        }

        private static bool IsFinite(Vector3 v)
        {
            return !(float.IsNaN(v.X) || float.IsNaN(v.Y) || float.IsNaN(v.Z)
                  || float.IsInfinity(v.X) || float.IsInfinity(v.Y) || float.IsInfinity(v.Z));
        }


        /// <summary>
        /// Stable partition: ymaps whose short name is also a base-game ymap name first, then the rest, each group
        /// in its original order. Fills <paramref name="overrideHashes"/> with those names (distinct, in order).
        /// </summary>
        private List<string> OrderBaseGameOverridesFirst(List<string> paths, List<uint> overrideHashes)
        {
            var first = new List<string>();
            var rest = new List<string>(paths.Count);
            var seen = new HashSet<uint>();
            for (int i = 0; i < paths.Count; i++)
            {
                var path = paths[i];
                var hash = JenkHash.GenHash(Path.GetFileNameWithoutExtension(path).ToLowerInvariant());
                if (IsBaseGameYmapName(hash))
                {
                    first.Add(path);
                    if (seen.Add(hash)) overrideHashes.Add(hash);
                }
                else
                {
                    rest.Add(path);
                }
            }
            first.AddRange(rest);
            return first;
        }

        /// <summary>Same lookup GameFileCache.GetYmap uses (YmapDict, then AllYmapsDict), without touching the cache.</summary>
        private bool IsBaseGameYmapName(uint hash)
        {
            var cache = GameFileCache;
            if (cache == null) return false;
            var active = cache.YmapDict;
            if ((active != null) && active.ContainsKey(hash)) return true;
            var all = cache.AllYmapsDict;
            if ((all != null) && all.ContainsKey(hash)) return true;
            return false;
        }

        #endregion



        #region indexing

        /// <summary>
        /// Single recursive scan of the pack folder. Records paths only - nothing is parsed or read.
        /// Handles FiveM trees where directories are literally named "something.rpf".
        /// </summary>
        public static ExternalMapPackIndex IndexFolder(string folder)
        {
            var index = new ExternalMapPackIndex();
            index.FolderPath = folder;
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return index;

            var stack = new Stack<string>();
            stack.Push(folder);
            while (stack.Count > 0)
            {
                var dir = stack.Pop();
                string[] subdirs = null;
                string[] files = null;
                try
                {
                    subdirs = Directory.GetDirectories(dir);
                    files = Directory.GetFiles(dir);
                }
                catch
                {
                    index.DirectoryErrors++;
                    continue;
                }
                for (int i = 0; i < subdirs.Length; i++) stack.Push(subdirs[i]);
                for (int i = 0; i < files.Length; i++)
                {
                    var path = files[i];
                    var ext = Path.GetExtension(path);
                    if (string.IsNullOrEmpty(ext)) { index.OtherCount++; continue; }
                    switch (ext.ToLowerInvariant())
                    {
                        case ".ymap": index.Ymap.Add(path); break;
                        case ".ytyp": index.Ytyp.Add(path); break;
                        case ".ydr": index.Ydr.Add(path); break;
                        case ".ydd": index.Ydd.Add(path); break;
                        case ".ytd": index.Ytd.Add(path); break;
                        case ".yft": index.Yft.Add(path); break;
                        case ".ybn": index.YbnCount++; break; //visual only - collision deliberately ignored
                        default: index.OtherCount++; break;
                    }
                }
            }
            return index;
        }


        private void BuildCollisionReport(ExternalMapPackIndex index)
        {
            ICollection<uint> basegame = null;
            if ((GameFileCache != null) && (GameFileCache.YmapDict != null))
            {
                basegame = GameFileCache.YmapDict.Keys;
            }
            var list = FindYmapNameCollisions(index, basegame);
            Collisions = list;
            if (list.Count > 0)
            {
                LogError(Name + ": " + list.Count + " ymap short-name collision(s) - within the pack the later file replaces the earlier one; a base-game ymap is replaced by the pack's while the pack is shown.");
                for (int i = 0; i < list.Count; i++) LogError("  " + list[i].ToString());
            }
        }


        /// <summary>
        /// Everything (ymap dict keys, LOD parent links, external archetype/file registration)
        /// is keyed by the short name hash alone, so two files with the same short name are
        /// indistinguishable. Report them rather than silently picking a winner.
        /// </summary>
        public static List<ExternalMapPackCollision> FindYmapNameCollisions(ExternalMapPackIndex index, ICollection<uint> existingHashes)
        {
            var result = new List<ExternalMapPackCollision>();
            var seen = new Dictionary<uint, string>();
            HashSet<uint> existing = null;
            if (existingHashes != null) existing = new HashSet<uint>(existingHashes);

            for (int i = 0; i < index.Ymap.Count; i++)
            {
                var path = index.Ymap[i];
                var shortname = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                var hash = JenkHash.GenHash(shortname);
                string prev;
                if (seen.TryGetValue(hash, out prev))
                {
                    var c = new ExternalMapPackCollision();
                    c.Hash = hash;
                    c.Kind = "ymap/ymap (within pack)";
                    c.Name = shortname;
                    c.PathA = prev;
                    c.PathB = path;
                    result.Add(c);
                }
                else
                {
                    seen[hash] = path;
                    if ((existing != null) && existing.Contains(hash))
                    {
                        var c = new ExternalMapPackCollision();
                        c.Hash = hash;
                        c.Kind = "ymap/base game";
                        c.Name = shortname;
                        c.PathA = "(base game)";
                        c.PathB = path;
                        result.Add(c);
                    }
                }
            }
            return result;
        }

        #endregion



        #region load report (load time only)

        private void FinishUnresolvedArchetypes(Dictionary<uint, ExternalMapPackUnresolvedArchetype> unresolved)
        {
            var list = new List<ExternalMapPackUnresolvedArchetype>(unresolved.Values);
            int placed = 0;
            int interior = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var u = list[i];
                u.LastYmap = null; //don't keep ymaps alive through the stats
                u.Name = JenkIndex.TryGetString(u.Hash);
                placed += u.PlacedCount;
                interior += u.InteriorCount;
            }
            list.Sort((a, b) =>
            {
                int c = b.TotalCount.CompareTo(a.TotalCount);
                return (c != 0) ? c : a.Hash.CompareTo(b.Hash);
            });
            UnresolvedArchetypes = list;
            UnresolvedArchetypeCount = list.Count;
            UnresolvedPlacedEntityCount = placed;
            UnresolvedInteriorEntityCount = interior;
            if (list.Count > 0)
            {
                LogError(Name + ": " + list.Count + " archetypes unresolved (" + (placed + interior) + " entities: " + placed + " placed, " + interior + " in interiors) - missing dependency resources? See the load report.");
            }
        }


        /// <summary>
        /// Writes the full report to %TEMP%\SprunkMapper\&lt;pack&gt;_report.txt - deliberately outside the pack
        /// folder. Rewritten on every completed load. Failure to write is logged, never fatal.
        /// </summary>
        private void WriteLoadReport(ExternalMapPackSnapshot finalsnap, CancellationToken token)
        {
            var inv = CultureInfo.InvariantCulture;

            //---- duplicates first: byte-compare them (only same-size copies are actually read) so the summary can
            //say how many are harmless ----
            var dupsb = new StringBuilder();
            int identicalcount = 0;
            var dups = DuplicateAssets;
            for (int i = 0; i < dups.Count; i++)
            {
                if (token.IsCancellationRequested) return;
                var d = dups[i];
                var winner = d.WinningPath;
                bool all = true;
                bool unknown = false;
                var lines = new StringBuilder();
                for (int p = 0; p < d.Paths.Count - 1; p++)
                {
                    var other = d.Paths[p];
                    long sizeother, sizewinner;
                    var same = CompareFiles(other, winner, out sizeother, out sizewinner);
                    string note;
                    if (same == null) { unknown = true; all = false; note = "(couldn't compare)"; }
                    else if (same.Value) { note = "(byte-identical)"; }
                    else { all = false; note = "(DIFFERENT: " + sizeother.ToString(inv) + " bytes vs " + sizewinner.ToString(inv) + " bytes)"; }
                    lines.AppendLine("      unused  " + RelativeToPack(other) + "   " + note);
                }
                d.AllIdentical = unknown ? (bool?)null : all;
                if (all) identicalcount++;
                dupsb.AppendLine("  " + d.Type.ToString().ToLowerInvariant() + "  " + d.Name + "  0x" + d.Hash.ToString("X8") + (all ? "   - all copies identical, harmless" : ""));
                dupsb.AppendLine("      WINS    " + RelativeToPack(winner));
                dupsb.Append(lines.ToString());
            }
            DuplicateAssetIdenticalCount = identicalcount;

            string path = null;
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("SprunkMapper backdrop pack load report");
                sb.AppendLine("Pack:    " + Name);
                sb.AppendLine("Folder:  " + FolderPath);
                sb.AppendLine("Written: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", inv));
                sb.AppendLine("(Rewritten every time the pack finishes loading. Paths below are relative to the folder.)");
                sb.AppendLine();

                sb.AppendLine("SUMMARY");
                sb.AppendLine("  Ymaps:                    " + LoadedYmapCount + " loaded of " + IndexedYmapCount + " (" + FailedYmapCount + " failed, " + EmptyYmapCount + " empty, " + DegenerateExtentsCount + " with repaired extents)");
                sb.AppendLine("  Ytyps:                    " + LoadedYtypCount + " loaded of " + IndexedYtypCount + " (" + FailedYtypCount + " failed), " + RegisteredArchetypeCount + " archetypes");
                sb.AppendLine("  Assets registered:        " + RegisteredAssetCount);
                sb.AppendLine("  Entities:                 " + EntityCount);
                sb.AppendLine("  Base-game ymaps replaced: " + OverriddenBaseYmapCount);
                sb.AppendLine("  Unresolved archetypes:    " + UnresolvedArchetypeCount + " (" + UnresolvedPlacedEntityCount + " placed + " + UnresolvedInteriorEntityCount + " in interiors = " + UnresolvedEntityCount + " entities)");
                sb.AppendLine("  Duplicate model names:    " + dups.Count + " (" + identicalcount + " byte-identical)");
                sb.AppendLine("  Ymap name collisions:     " + Collisions.Count);
                sb.AppendLine();

                sb.AppendLine("BASE-GAME YMAPS REPLACED BY THIS PACK WHILE IT IS SHOWN");
                sb.AppendLine("  (FiveM behaviour: a streamed ymap replaces the base-game ymap with the same name. Ymaps in your");
                sb.AppendLine("   project are never replaced. Turn the pack off to see or edit the base-game versions.)");
                var overrides = (finalsnap != null) ? finalsnap.Overrides : new ExternalYmapBounds[0];
                if (overrides.Length == 0) sb.AppendLine("  (none)");
                for (int i = 0; i < overrides.Length; i++)
                {
                    var ymap = overrides[i].Ymap;
                    if (ymap == null) continue;
                    int ents = (ymap.AllEntities != null) ? ymap.AllEntities.Length : 0;
                    var name = JenkIndex.TryGetString(overrides[i].Hash.Hash);
                    if (string.IsNullOrEmpty(name)) name = ymap.Name;
                    sb.AppendLine(string.Format(inv, "  {0,-36} 0x{1:X8} {2,7} entities  {3}{4}",
                        name, overrides[i].Hash.Hash, ents, RelativeToPack(ymap.FilePath),
                        (ents == 0) ? "   (no entities - hides the base-game ymap)" : ""));
                }
                sb.AppendLine();

                sb.AppendLine("UNRESOLVED ARCHETYPES");
                sb.AppendLine("  (Neither this pack's ytyps nor the base game define these, so these entities are not drawn. Usually a");
                sb.AppendLine("   dependency resource missing from the pack folder, not a bug. \"interior\" = inside MLO interiors, incl. entity sets.)");
                if (UnresolvedArchetypes.Count == 0) sb.AppendLine("  (none)");
                else sb.AppendLine("   total  placed  interior  ymaps  hash        name                            example ymap");
                for (int i = 0; i < UnresolvedArchetypes.Count; i++)
                {
                    var u = UnresolvedArchetypes[i];
                    sb.AppendLine(string.Format(inv, "  {0,6}  {1,6}  {2,8}  {3,5}  0x{4:X8}  {5,-30}  {6}",
                        u.TotalCount, u.PlacedCount, u.InteriorCount, u.YmapCount, u.Hash, u.DisplayName, RelativeToPack(u.ExampleYmap)));
                }
                sb.AppendLine();

                sb.AppendLine("DUPLICATE MODEL NAMES");
                sb.AppendLine("  (Same type and short name at different paths. Models are looked up by short name alone, so only one");
                sb.AppendLine("   copy is ever used: the LAST one registered, marked WINS. The others are never loaded.)");
                if (dups.Count == 0) sb.AppendLine("  (none)");
                sb.Append(dupsb.ToString());
                sb.AppendLine();

                sb.AppendLine("YMAP SHORT-NAME COLLISIONS");
                sb.AppendLine("  (within pack: the later file wins. base game: the pack's ymap replaces it while shown - see above.)");
                if (Collisions.Count == 0) sb.AppendLine("  (none)");
                for (int i = 0; i < Collisions.Count; i++)
                {
                    sb.AppendLine("  " + Collisions[i].ToString());
                }

                var dir = Path.Combine(Path.GetTempPath(), "SprunkMapper");
                Directory.CreateDirectory(dir);
                path = Path.Combine(dir, SafeFileName(Name) + "_report.txt");
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                ReportPath = path;
                LogError(Name + ": load report written to " + path);
            }
            catch (Exception ex)
            {
                LogError(Name + ": couldn't write the load report" + ((path != null) ? (" to " + path) : "") + ": " + ex.Message);
            }
        }


        /// <summary>True/false when both files could be read, null otherwise. Only reads the contents when the sizes match.</summary>
        private static bool? CompareFiles(string a, string b, out long sizea, out long sizeb)
        {
            sizea = -1;
            sizeb = -1;
            try
            {
                var fa = new FileInfo(a);
                var fb = new FileInfo(b);
                if (!fa.Exists || !fb.Exists) return null;
                sizea = fa.Length;
                sizeb = fb.Length;
                if (sizea != sizeb) return false;
                if (string.Equals(fa.FullName, fb.FullName, StringComparison.OrdinalIgnoreCase)) return true;

                const int bufsize = 1 << 16;
                var bufa = new byte[bufsize];
                var bufb = new byte[bufsize];
                using (var sa = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.Read, bufsize, FileOptions.SequentialScan))
                using (var sbs = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.Read, bufsize, FileOptions.SequentialScan))
                {
                    while (true)
                    {
                        int na = ReadFully(sa, bufa);
                        int nb = ReadFully(sbs, bufb);
                        if (na != nb) return false;
                        if (na == 0) return true;
                        for (int i = 0; i < na; i++)
                        {
                            if (bufa[i] != bufb[i]) return false;
                        }
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        private static int ReadFully(Stream s, byte[] buf)
        {
            int total = 0;
            while (total < buf.Length)
            {
                int n = s.Read(buf, total, buf.Length - total);
                if (n <= 0) break;
                total += n;
            }
            return total;
        }

        private string RelativeToPack(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            var root = FolderPath;
            if (!string.IsNullOrEmpty(root) && path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                var rel = path.Substring(root.Length).TrimStart('\\', '/');
                if (rel.Length > 0) return rel;
            }
            return path;
        }

        private static string SafeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "pack";
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                var c = name[i];
                sb.Append(((Array.IndexOf(invalid, c) >= 0) || char.IsWhiteSpace(c)) ? '_' : c);
            }
            return sb.ToString();
        }

        #endregion



        #region helpers

        private void ResetStats()
        {
            IndexedYmapCount = 0;
            IndexedYtypCount = 0;
            IndexedAssetCount = 0;
            RegisteredAssetCount = 0;
            LoadedYtypCount = 0;
            RegisteredArchetypeCount = 0;
            LoadedYmapCount = 0;
            FailedYmapCount = 0;
            FailedYtypCount = 0;
            EntityCount = 0;
            DegenerateExtentsCount = 0;
            EmptyYmapCount = 0;
            Collisions = new List<ExternalMapPackCollision>();
            Errors = new List<string>();
            OverriddenBaseYmapCount = 0;
            UnresolvedArchetypeCount = 0;
            UnresolvedPlacedEntityCount = 0;
            UnresolvedInteriorEntityCount = 0;
            UnresolvedArchetypes = new List<ExternalMapPackUnresolvedArchetype>();
            DuplicateAssets = new List<ExternalMapPackDuplicateAsset>();
            DuplicateAssetIdenticalCount = 0;
            ReportPath = null;
        }

        private void SetStatus(string text)
        {
            statusText = text;
            var cb = StatusChanged;
            if (cb != null)
            {
                try { cb(this); }
                catch { }
            }
        }

        private void LogError(string text)
        {
            var errors = Errors;
            if (errors != null)
            {
                lock (errors) { if (errors.Count < 500) errors.Add(text); }
            }
            var cb = ErrorLog;
            if (cb != null)
            {
                try { cb(text); }
                catch { }
            }
        }

        #endregion

    }

}
