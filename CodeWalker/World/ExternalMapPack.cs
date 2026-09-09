using CodeWalker.GameFiles;
using SharpDX;
using System;
using System.Collections.Generic;
using System.IO;
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
            new ExternalYmapBounds[0], new Dictionary<MetaHash, YmapFile>());

        public readonly ExternalYmapBounds[] Items;
        public readonly Dictionary<MetaHash, YmapFile> ByHash;

        public ExternalMapPackSnapshot(ExternalYmapBounds[] items, Dictionary<MetaHash, YmapFile> byhash)
        {
            Items = items;
            ByHash = byhash;
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
    /// Loads a large loose-file (FiveM-style) map pack folder as read-only backdrop scenery.
    ///
    /// Drawables/dictionaries/textures are NOT loaded here - they are registered with the
    /// GameFileCache by path and pulled in lazily by the normal streaming path on first request.
    /// Only ytyps (needed for archetypes) and ymaps (needed for placement) are parsed up front,
    /// on a background thread.
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
        }



        #region per-frame visibility (render thread)

        /// <summary>
        /// Per-frame visibility provider. Cost is a linear scan of the precomputed AABB array
        /// (one array element per ymap) plus a dictionary insert per hit, plus a short parent
        /// chain climb for LOD parents. It performs NO entity iteration, NO archetype resolution,
        /// NO allocation and NO file IO - all of that happened once at load time.
        /// Safe to call while the pack is still loading, and while it is being unloaded.
        /// </summary>
        public void GetVisibleYmaps(Vector3 campos, Dictionary<MetaHash, YmapFile> ymaps)
        {
            //single volatile read - the snapshot is immutable
            GetVisibleYmaps(snapshot, campos, ExtraRange, GameFileCache, ymaps);
        }

        /// <summary>Cull + parent climb against a given snapshot. See GetVisibleYmaps above.</summary>
        public static void GetVisibleYmaps(ExternalMapPackSnapshot snap, Vector3 campos, float range, GameFileCache cache, Dictionary<MetaHash, YmapFile> ymaps)
        {
            if (snap == null) return;
            var items = snap.Items;
            if (items.Length == 0) return;
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

                var hash = item.Hash;
                if (ymaps.ContainsKey(hash)) continue;

                //climb the LOD parent chain - RenderLodManager drops any ymap whose parent
                //hash is nonzero but absent from the same dictionary.
                var ymap = item.Ymap;
                while ((ymap != null) && ymap.Loaded)
                {
                    ymaps[hash] = ymap;
                    hash = ymap._CMapData.parent;
                    if (hash.Hash == 0) break;
                    if (ymaps.ContainsKey(hash)) break;
                    if (!byhash.TryGetValue(hash, out ymap))
                    {
                        //parent might be a base game ymap
                        ymap = (cache != null) ? cache.GetYmap(hash) : null;
                    }
                }
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
                snapshot = ExternalMapPackSnapshot.Empty; //render thread sees nothing from here on
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
                var bounds = new List<ExternalYmapBounds>(index.Ymap.Count);
                var byhash = new Dictionary<MetaHash, YmapFile>(index.Ymap.Count);
                int publishevery = 32;
                for (int i = 0; i < index.Ymap.Count; i++)
                {
                    if (token.IsCancellationRequested) return;
                    LoadYmap(index.Ymap[i], bounds, byhash);
                    if (((i + 1) % publishevery) == 0)
                    {
                        Publish(bounds, byhash);
                        SetStatus(Name + ": ymaps " + (i + 1) + " / " + index.Ymap.Count);
                    }
                }
                if (token.IsCancellationRequested) return;

                Publish(bounds, byhash);

                lock (loadSyncRoot)
                {
                    if (stateval == (int)ExternalMapPackState.Loading)
                    {
                        stateval = (int)ExternalMapPackState.Loaded;
                    }
                }
                SetStatus(Name + ": " + LoadedYmapCount + " ymaps, " + RegisteredArchetypeCount + " archetypes, " + RegisteredAssetCount + " assets");
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


        private void Publish(List<ExternalYmapBounds> bounds, Dictionary<MetaHash, YmapFile> byhash)
        {
            //copies so the published snapshot is never touched again
            var items = bounds.ToArray();
            var dict = new Dictionary<MetaHash, YmapFile>(byhash);
            snapshot = new ExternalMapPackSnapshot(items, dict);
        }


        private void RegisterAssets(List<string> paths, GameFileType type)
        {
            for (int i = 0; i < paths.Count; i++)
            {
                var path = paths[i];
                var shortname = Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrEmpty(shortname)) continue;
                shortname = shortname.ToLowerInvariant();
                JenkIndex.Ensure(shortname);
                var hash = JenkHash.GenHash(shortname);
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


        private void LoadYmap(string path, List<ExternalYmapBounds> bounds, Dictionary<MetaHash, YmapFile> byhash)
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
                renderable = PrepareYmap(ymap, GameFileCache, out b, out fixedextents);
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
            bounds = new ExternalYmapBounds();
            fixedExtents = false;
            if (ymap == null) return false;

            ymap.IsLockedBackdrop = true;

            if (cache != null)
            {
                ymap.InitYmapEntityArchetypes(cache); //done exactly once, here - never per frame
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
                LogError(Name + ": " + list.Count + " ymap short-name collision(s) - later files silently replace earlier ones.");
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
