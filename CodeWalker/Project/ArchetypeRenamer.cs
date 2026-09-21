using CodeWalker.GameFiles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CodeWalker.Project
{
    /// <summary>
    /// What to rename, and everything the rename is allowed to look at.
    /// </summary>
    public class ArchetypeRenameRequest
    {
        public Archetype Archetype;
        public string NewName;

        /// <summary>The FiveM resource folder to scan for files that aren't open in the project. Null = project only.</summary>
        public string ResourceRoot;

        public List<YtypFile> ProjectYtyps = new List<YtypFile>();
        public List<YmapFile> ProjectYmaps = new List<YmapFile>();
        /// <summary>ydr/yft/ydd/ytd files loaded in the project.</summary>
        public List<GameFile> ProjectAssets = new List<GameFile>();

        /// <summary>True if the hash is already an archetype somewhere else (base game, backdrop pack, project).</summary>
        public Func<uint, bool> IsNameTakenElsewhere;

        /// <summary>Where backups go. Must NOT be inside the resource: FiveM streams everything under stream/.</summary>
        public string BackupRoot;

        //only matter when a file with the old name exists - see ArchetypeRenamer's class comment
        public bool RenameTextureDictionary = true;
        public bool RenamePhysicsDictionary = true;
        public bool RenameDrawableDictionary = true;

        /// <summary>Disk files already read by a previous BuildPlan, so re-planning (eg. on each keystroke) doesn't re-read them.</summary>
        public Dictionary<string, GameFile> DiskCache = new Dictionary<string, GameFile>(StringComparer.OrdinalIgnoreCase);
    }

    public class ArchetypeRenameFileMove
    {
        public string OldPath;
        public string NewPath;
        /// <summary>Null if the file isn't loaded in the project.</summary>
        public GameFile ProjectFile;
    }

    public class ArchetypeRenameFileEdit
    {
        public string Path;
        public GameFile File; //YtypFile or YmapFile
        public bool IsProjectFile;
        /// <summary>A project file that already had unsaved edits - saving it as part of the rename saves those too.</summary>
        public bool HadUnsavedChanges;
        /// <summary>False for a project file that has never been saved to disk: it's patched in memory only.</summary>
        public bool CanSave;
        public int EntityRefs;
        public int ArchetypeRefs;
        public int OtherRefs;
    }

    public class ArchetypeRenamePlan
    {
        public ArchetypeRenameRequest Request;
        public string OldName;
        public string NewName;
        public uint OldHash;
        public uint NewHash;

        public List<string> Errors = new List<string>();
        public List<string> Warnings = new List<string>();
        public bool CanApply { get { return Errors.Count == 0; } }

        /// <summary>The drawable (assetName, .ydr/.yft) is named after the archetype, so it follows the rename.</summary>
        public bool DrawableFollows;
        public bool UpdateTextureDict;
        public bool UpdatePhysicsDict;
        public bool UpdateDrawableDict;

        //for the dialog: only offer a checkbox when there's actually a file of that kind to rename
        public bool HasTextureDictFile;
        public bool HasPhysicsDictFile;
        public bool HasDrawableDictFile;

        public List<ArchetypeRenameFileMove> Moves = new List<ArchetypeRenameFileMove>();
        public List<ArchetypeRenameFileEdit> Edits = new List<ArchetypeRenameFileEdit>();

        /// <summary>Set by Apply.</summary>
        public string BackupFolder;

        public int TotalEntityRefs { get { return Edits.Sum(e => e.EntityRefs); } }
    }

    /// <summary>
    /// Renames a custom prop everywhere it's referenced: the archetype in its ytyp, every ymap entity that places
    /// it, the MLO interiors that contain it, and the .ydr/.ytd/... files named after it - in the project and in
    /// the resource folder on disk.
    ///
    /// The rule is "references follow the files": a hash field equal to the old name moves to the new name when
    /// the file it points at is renamed, or when no such file exists at all (CodeWalker's "Embedded" convention,
    /// eg. textureDictionary == name with the textures inside the ydr). A same-named .ytd/.ybn/.ydd the user
    /// chooses to keep keeps its references too, so nothing ends up pointing at a file that isn't there.
    ///
    /// UI-free on purpose so it can be tested headless against real resources.
    /// </summary>
    public static class ArchetypeRenamer
    {
        private static readonly string[] DrawableExtensions = { ".ydr", ".yft" };
        private const string TextureDictExtension = ".ytd";
        private const string PhysicsDictExtension = ".ybn";
        private const string DrawableDictExtension = ".ydd";

        private static bool IsAssetExtension(string ext)
        {
            return DrawableExtensions.Contains(ext) || (ext == TextureDictExtension) || (ext == PhysicsDictExtension) || (ext == DrawableDictExtension);
        }

        /// <summary>
        /// The folder holding the fxmanifest.lua / __resource.lua above the file, or the file's own folder if there isn't one.
        /// </summary>
        public static string FindResourceRoot(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return null;
            var dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
            var d = dir;
            while (!string.IsNullOrEmpty(d))
            {
                if (File.Exists(Path.Combine(d, "fxmanifest.lua")) || File.Exists(Path.Combine(d, "__resource.lua")))
                {
                    return d;
                }
                d = Path.GetDirectoryName(d);
            }
            return dir;
        }

        public static bool IsValidName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            foreach (var c in name)
            {
                bool ok = ((c >= 'a') && (c <= 'z')) || ((c >= '0') && (c <= '9')) || (c == '_') || (c == '-');
                if (!ok) return false;
            }
            return true;
        }

        private static uint ShortNameHash(string path)
        {
            return JenkHash.GenHash(Path.GetFileNameWithoutExtension(path).ToLowerInvariant());
        }

        private static string FullPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try { return Path.GetFullPath(path); }
            catch { return null; }
        }



        public static ArchetypeRenamePlan BuildPlan(ArchetypeRenameRequest req)
        {
            var plan = new ArchetypeRenamePlan();
            plan.Request = req;

            var arch = req.Archetype;
            if (arch == null)
            {
                plan.Errors.Add("No archetype selected.");
                return plan;
            }

            plan.OldHash = arch._BaseArchetypeDef.name.Hash;
            plan.OldName = JenkIndex.TryGetString(plan.OldHash);
            if (string.IsNullOrEmpty(plan.OldName)) plan.OldName = arch.Name;
            plan.NewName = (req.NewName ?? string.Empty).Trim().ToLowerInvariant();

            //a bad name is an error, but the scan still runs (with NewHash 0, which no real name hashes to) so the
            //dialog can show what the rename will touch before a usable name has been typed
            if (string.IsNullOrEmpty(plan.NewName))
            {
                plan.Errors.Add("Type a new name.");
            }
            else if (!IsValidName(plan.NewName))
            {
                plan.Errors.Add("The new name can only use a-z, 0-9, _ and -.");
            }
            else if (JenkHash.GenHash(plan.NewName) == plan.OldHash)
            {
                plan.Errors.Add("Type a new name - that's the current one.");
            }
            else
            {
                plan.NewHash = JenkHash.GenHash(plan.NewName);
            }

            var assetName = arch._BaseArchetypeDef.assetName.Hash;
            plan.DrawableFollows = (assetName == plan.OldHash) || (assetName == 0);
            if (!plan.DrawableFollows)
            {
                var an = JenkIndex.TryGetString(assetName);
                plan.Warnings.Add("Its drawable '" + (string.IsNullOrEmpty(an) ? arch.AssetName : an) + "' isn't named after the archetype, so it keeps its name.");
            }


            //1. every file that is named after the old or the new name
            var oldNamed = new List<Tuple<string, GameFile>>(); //full path, project file (or null)
            var projectPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var f in req.ProjectAssets)
            {
                var p = FullPath(f?.FilePath);
                if (p == null) continue;
                projectPaths.Add(p);
                CheckAssetName(plan, p, f, oldNamed);
            }
            foreach (var f in req.ProjectYtyps)
            {
                var p = FullPath(f?.FilePath);
                if (p != null) projectPaths.Add(p);
            }
            foreach (var f in req.ProjectYmaps)
            {
                var p = FullPath(f?.FilePath);
                if (p != null) projectPaths.Add(p);
            }

            var diskContent = new List<string>();
            if (!string.IsNullOrEmpty(req.ResourceRoot))
            {
                if (!Directory.Exists(req.ResourceRoot))
                {
                    plan.Errors.Add("Resource folder not found: " + req.ResourceRoot);
                    return plan;
                }
                string[] files;
                try
                {
                    files = Directory.GetFiles(req.ResourceRoot, "*", SearchOption.AllDirectories);
                }
                catch (Exception ex)
                {
                    plan.Errors.Add("Couldn't scan the resource folder: " + ex.Message);
                    return plan;
                }
                foreach (var file in files)
                {
                    var p = FullPath(file);
                    if ((p == null) || projectPaths.Contains(p)) continue; //handled through the project object
                    var ext = Path.GetExtension(p).ToLowerInvariant();
                    if ((ext == ".ymap") || (ext == ".ytyp"))
                    {
                        diskContent.Add(p);
                    }
                    else if (IsAssetExtension(ext))
                    {
                        CheckAssetName(plan, p, null, oldNamed);
                    }
                }
            }


            //2. decide which references follow, from which files exist
            plan.HasTextureDictFile = oldNamed.Any(t => HasExt(t.Item1, TextureDictExtension));
            plan.HasPhysicsDictFile = oldNamed.Any(t => HasExt(t.Item1, PhysicsDictExtension));
            plan.HasDrawableDictFile = oldNamed.Any(t => HasExt(t.Item1, DrawableDictExtension));
            plan.UpdateTextureDict = !plan.HasTextureDictFile || req.RenameTextureDictionary;
            plan.UpdatePhysicsDict = !plan.HasPhysicsDictFile || req.RenamePhysicsDictionary;
            plan.UpdateDrawableDict = !plan.HasDrawableDictFile || req.RenameDrawableDictionary;
            if (!plan.UpdateTextureDict) plan.Warnings.Add(plan.OldName + ".ytd keeps its name, and so do the texture dictionary references to it.");
            if (!plan.UpdatePhysicsDict) plan.Warnings.Add(plan.OldName + ".ybn keeps its name, and so do the physics dictionary references to it.");
            if (!plan.UpdateDrawableDict) plan.Warnings.Add(plan.OldName + ".ydd keeps its name, and so do the drawable dictionary references to it.");

            var seenMoveNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in oldNamed)
            {
                var ext = Path.GetExtension(t.Item1).ToLowerInvariant();
                bool move =
                    (DrawableExtensions.Contains(ext) && plan.DrawableFollows) ||
                    ((ext == TextureDictExtension) && plan.UpdateTextureDict) ||
                    ((ext == PhysicsDictExtension) && plan.UpdatePhysicsDict) ||
                    ((ext == DrawableDictExtension) && plan.UpdateDrawableDict);
                if (!move) continue;

                var newPath = Path.Combine(Path.GetDirectoryName(t.Item1), plan.NewName + ext);
                plan.Moves.Add(new ArchetypeRenameFileMove() { OldPath = t.Item1, NewPath = newPath, ProjectFile = t.Item2 });
                if (!seenMoveNames.Add(plan.OldName + ext))
                {
                    plan.Warnings.Add("There's more than one " + plan.OldName + ext + " - all of them get renamed. FiveM only streams one; check which one is live.");
                }
            }


            //3. content that references the old name
            foreach (var ytyp in req.ProjectYtyps)
            {
                if (ytyp == null) continue;
                AddEdit(plan, ytyp, true);
                CheckNewNameTaken(plan, ytyp);
            }
            foreach (var ymap in req.ProjectYmaps)
            {
                if ((ymap == null) || ymap.IsLockedBackdrop) continue;
                AddEdit(plan, ymap, true);
            }
            foreach (var p in diskContent)
            {
                GameFile gf;
                if (!req.DiskCache.TryGetValue(p, out gf))
                {
                    try
                    {
                        gf = LoadDiskFile(p);
                    }
                    catch (Exception ex)
                    {
                        plan.Warnings.Add("Couldn't read " + Path.GetFileName(p) + " (" + ex.Message + ") - it won't be updated.");
                        gf = null;
                    }
                    req.DiskCache[p] = gf;
                }
                if (gf == null) continue;
                AddEdit(plan, gf, false);
                if (gf is YtypFile dytyp) CheckNewNameTaken(plan, dytyp);
            }

            if ((plan.NewHash != 0) && (req.IsNameTakenElsewhere?.Invoke(plan.NewHash) ?? false))
            {
                plan.Errors.Add("'" + plan.NewName + "' is already an archetype (base game, a map pack or another project ytyp).");
            }

            int decls = plan.Edits.Where(e => e.File is YtypFile).Sum(e => CountDeclarations((YtypFile)e.File, plan.OldHash));
            if (decls > 1)
            {
                plan.Warnings.Add("'" + plan.OldName + "' is defined in " + decls + " places - every definition gets renamed.");
            }

            foreach (var e in plan.Edits)
            {
                if (e.IsProjectFile && !e.CanSave)
                {
                    plan.Warnings.Add(Path.GetFileName(e.Path ?? e.File.Name ?? "?") + " has never been saved, so it's only updated in memory - save it yourself.");
                }
            }

            //refuse up front rather than fail half-way through Apply
            var toWrite = plan.Moves.Select(m => m.OldPath).Concat(plan.Edits.Where(e => e.CanSave).Select(e => e.Path));
            foreach (var p in toWrite)
            {
                if ((p != null) && File.Exists(p) && ((File.GetAttributes(p) & FileAttributes.ReadOnly) != 0))
                {
                    plan.Errors.Add(Path.GetFileName(p) + " is read-only - clear that first (" + p + ").");
                }
            }

            return plan;
        }

        private static bool HasExt(string path, string ext)
        {
            return string.Equals(Path.GetExtension(path), ext, StringComparison.OrdinalIgnoreCase);
        }

        private static void CheckAssetName(ArchetypeRenamePlan plan, string path, GameFile projectFile, List<Tuple<string, GameFile>> oldNamed)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (!IsAssetExtension(ext)) return;
            var h = ShortNameHash(path);
            if (h == plan.OldHash)
            {
                oldNamed.Add(new Tuple<string, GameFile>(path, projectFile));
            }
            else if ((plan.NewHash != 0) && (h == plan.NewHash)) //0 while no valid name is typed - and it's the hash of "", eg. a file called ".ydr"
            {
                plan.Errors.Add(Path.GetFileName(path) + " already exists (" + path + ").");
            }
        }

        private static void CheckNewNameTaken(ArchetypeRenamePlan plan, YtypFile ytyp)
        {
            if ((plan.NewHash == 0) || (ytyp.AllArchetypes == null)) return;
            foreach (var a in ytyp.AllArchetypes)
            {
                if ((a._BaseArchetypeDef.name.Hash == plan.NewHash) || (a._BaseArchetypeDef.assetName.Hash == plan.NewHash))
                {
                    plan.Errors.Add("'" + plan.NewName + "' is already an archetype in " + (ytyp.Name ?? "a ytyp") + ".");
                    return;
                }
            }
        }

        private static int CountDeclarations(YtypFile ytyp, uint hash)
        {
            if (ytyp.AllArchetypes == null) return 0;
            return ytyp.AllArchetypes.Count(a => a._BaseArchetypeDef.name.Hash == hash);
        }

        private static GameFile LoadDiskFile(string path)
        {
            var data = File.ReadAllBytes(path);
            var ext = Path.GetExtension(path).ToLowerInvariant();
            GameFile gf;
            if (ext == ".ymap")
            {
                var ymap = new YmapFile();
                ymap.Load(data);
                gf = ymap;
            }
            else
            {
                var ytyp = new YtypFile();
                ytyp.Load(data);
                gf = ytyp;
            }
            gf.FilePath = path;
            gf.Name = Path.GetFileName(path);
            return gf;
        }

        private static void AddEdit(ArchetypeRenamePlan plan, GameFile gf, bool isProject)
        {
            var edit = new ArchetypeRenameFileEdit();
            edit.File = gf;
            edit.IsProjectFile = isProject;
            edit.Path = FullPath(gf.FilePath);
            edit.CanSave = !isProject || ((edit.Path != null) && File.Exists(edit.Path));
            if (gf is YtypFile ytyp)
            {
                edit.HadUnsavedChanges = isProject && ytyp.HasChanged;
                PatchYtyp(ytyp, plan, false, edit);
            }
            else if (gf is YmapFile ymap)
            {
                edit.HadUnsavedChanges = isProject && ymap.HasChanged;
                PatchYmap(ymap, plan, false, edit);
            }
            if ((edit.EntityRefs + edit.ArchetypeRefs + edit.OtherRefs) > 0)
            {
                plan.Edits.Add(edit);
            }
        }



        /// <summary>
        /// Counts (apply=false) or rewrites (apply=true) every reference in the ytyp. Counting and applying share
        /// this one code path so the preview can't disagree with what's written.
        /// </summary>
        private static void PatchYtyp(YtypFile ytyp, ArchetypeRenamePlan plan, bool apply, ArchetypeRenameFileEdit counts)
        {
            if (ytyp.AllArchetypes == null) return;
            foreach (var a in ytyp.AllArchetypes)
            {
                int n = PatchBaseDef(ref a._BaseArchetypeDef, plan, apply);
                if (n > 0) counts.ArchetypeRefs++;
                if (apply && (n > 0))
                {
                    //time and MLO archetypes are saved from their OWN embedded copy of the base def (YtypFile.Save),
                    //not from Archetype._BaseArchetypeDef - both copies have to change
                    if (a is TimeArchetype t)
                    {
                        PatchBaseDef(ref t._TimeArchetypeDef._BaseArchetypeDef, plan, true);
                    }
                    else if (a is MloArchetype mlo)
                    {
                        PatchBaseDef(ref mlo._MloArchetypeDef._BaseArchetypeDef, plan, true);
                    }
                    //cached copies the renderer and the archetype lookup use
                    var bd = a._BaseArchetypeDef;
                    a.Hash = (bd.assetName.Hash != 0) ? bd.assetName : bd.name;
                    a.TextureDict = bd.textureDictionary;
                    a.DrawableDict = bd.drawableDictionary;
                }

                if (a is MloArchetype m)
                {
                    if (m.entities != null)
                    {
                        foreach (var e in m.entities)
                        {
                            if ((e != null) && (e._Data.archetypeName.Hash == plan.OldHash))
                            {
                                counts.EntityRefs++;
                                if (apply) e._Data.archetypeName = new MetaHash(plan.NewHash);
                            }
                        }
                    }
                    if (m.entitySets != null)
                    {
                        foreach (var es in m.entitySets)
                        {
                            if (es?.Entities == null) continue;
                            foreach (var e in es.Entities)
                            {
                                if ((e != null) && (e._Data.archetypeName.Hash == plan.OldHash))
                                {
                                    counts.EntityRefs++;
                                    if (apply) e._Data.archetypeName = new MetaHash(plan.NewHash);
                                }
                            }
                        }
                    }
                }
            }
        }

        private static int PatchBaseDef(ref CBaseArchetypeDef d, ArchetypeRenamePlan plan, bool apply)
        {
            var nh = new MetaHash(plan.NewHash);
            int n = 0;
            if (d.name.Hash == plan.OldHash)
            {
                n++;
                if (apply) d.name = nh;
            }
            if (plan.DrawableFollows && (d.assetName.Hash == plan.OldHash))
            {
                n++;
                if (apply) d.assetName = nh;
            }
            if (plan.UpdateTextureDict && (d.textureDictionary.Hash == plan.OldHash))
            {
                n++;
                if (apply) d.textureDictionary = nh;
            }
            if (plan.UpdatePhysicsDict && (d.physicsDictionary.Hash == plan.OldHash))
            {
                n++;
                if (apply) d.physicsDictionary = nh;
            }
            if (plan.UpdateDrawableDict && (d.drawableDictionary.Hash == plan.OldHash))
            {
                n++;
                if (apply) d.drawableDictionary = nh;
            }
            return n;
        }

        private static void PatchYmap(YmapFile ymap, ArchetypeRenamePlan plan, bool apply, ArchetypeRenameFileEdit counts)
        {
            var nh = new MetaHash(plan.NewHash);
            if (ymap.AllEntities != null)
            {
                foreach (var ent in ymap.AllEntities)
                {
                    if (ent == null) continue;
                    if (ent._CEntityDef.archetypeName.Hash == plan.OldHash)
                    {
                        counts.EntityRefs++;
                        if (apply) ent._CEntityDef.archetypeName = nh;
                    }
                    //in-memory interior children of a placed MLO - they mirror the ytyp and aren't saved
                    //with the ymap, so they're not counted, just kept consistent for the UI
                    if (apply && (ent.MloInstance != null))
                    {
                        PatchEntities(ent.MloInstance.Entities, plan);
                        if (ent.MloInstance.EntitySets != null)
                        {
                            foreach (var es in ent.MloInstance.EntitySets)
                            {
                                PatchEntities(es?.Entities, plan);
                            }
                        }
                    }
                }
            }
            if (ymap.GrassInstanceBatches != null)
            {
                foreach (var gb in ymap.GrassInstanceBatches)
                {
                    if (gb == null) continue;
                    var b = gb.Batch; //a struct property - copy, change, assign back
                    if (b.archetypeName.Hash == plan.OldHash)
                    {
                        counts.EntityRefs++;
                        if (apply)
                        {
                            b.archetypeName = nh;
                            gb.Batch = b;
                        }
                    }
                }
            }
            if (plan.UpdatePhysicsDict && (ymap.physicsDictionaries != null))
            {
                for (int i = 0; i < ymap.physicsDictionaries.Length; i++)
                {
                    if (ymap.physicsDictionaries[i].Hash == plan.OldHash)
                    {
                        counts.OtherRefs++;
                        if (apply) ymap.physicsDictionaries[i] = nh;
                    }
                }
            }
        }

        private static void PatchEntities(IEnumerable<YmapEntityDef> ents, ArchetypeRenamePlan plan)
        {
            if (ents == null) return;
            foreach (var e in ents)
            {
                if ((e != null) && (e._CEntityDef.archetypeName.Hash == plan.OldHash))
                {
                    e._CEntityDef.archetypeName = new MetaHash(plan.NewHash);
                }
            }
        }



        /// <summary>
        /// Backs up, renames files, patches and saves everything in the plan. Disk work happens first and is rolled
        /// back from the backup if any of it fails, before the project objects in memory are touched.
        /// saveProjectFile is called for each project ytyp/ymap that can be saved; null writes them directly.
        /// </summary>
        public static void Apply(ArchetypeRenamePlan plan, Action<GameFile> saveProjectFile = null)
        {
            if ((plan == null) || !plan.CanApply) throw new InvalidOperationException("The rename plan has errors.");
            var req = plan.Request;
            if (string.IsNullOrEmpty(req.BackupRoot)) throw new InvalidOperationException("No backup folder set.");

            JenkIndex.Ensure(plan.NewName);

            //1. back up everything that is about to be written or renamed
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var folder = Path.Combine(req.BackupRoot, stamp + "_" + plan.OldName + "_to_" + plan.NewName);
            plan.BackupFolder = folder;
            for (int i = 2; Directory.Exists(plan.BackupFolder); i++)
            {
                plan.BackupFolder = folder + "_" + i; //a retry within the same second
            }
            var backups = new List<Tuple<string, string>>(); //original, backup
            var toBackup = new List<string>();
            toBackup.AddRange(plan.Moves.Select(m => m.OldPath));
            toBackup.AddRange(plan.Edits.Where(e => e.CanSave && (e.Path != null) && File.Exists(e.Path)).Select(e => e.Path));
            var manifest = new StringBuilder();
            manifest.AppendLine("Rename " + plan.OldName + " -> " + plan.NewName + ", " + DateTime.Now.ToString("u"));
            manifest.AppendLine("Resource: " + (req.ResourceRoot ?? "(project only)"));
            manifest.AppendLine("To undo: copy each backup over its original, then delete the renamed files listed under 'moved'.");
            manifest.AppendLine();
            foreach (var p in toBackup.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var bp = Path.Combine(plan.BackupFolder, BackupRelativePath(p, req.ResourceRoot));
                Directory.CreateDirectory(Path.GetDirectoryName(bp));
                File.Copy(p, bp, false);
                File.SetAttributes(bp, FileAttributes.Normal); //File.Copy keeps read-only, which would make the backup folder undeletable
                backups.Add(new Tuple<string, string>(p, bp));
                manifest.AppendLine("backup: " + p + " <- " + bp);
            }
            foreach (var m in plan.Moves)
            {
                manifest.AppendLine("moved: " + m.OldPath + " -> " + m.NewPath);
            }
            File.WriteAllText(Path.Combine(plan.BackupFolder, "rename.txt"), manifest.ToString());


            //2. disk work: file renames, then files that aren't open in the project
            var moved = new List<ArchetypeRenameFileMove>();
            var written = new List<string>(); //only these get restored on failure - never overwrite a file we didn't touch
            try
            {
                foreach (var m in plan.Moves)
                {
                    File.Move(m.OldPath, m.NewPath);
                    moved.Add(m);
                }
                foreach (var e in plan.Edits)
                {
                    if (e.IsProjectFile) continue;
                    var data = SaveFile(Patched(e, plan));
                    written.Add(e.Path); //before the write: a half-written file needs restoring too
                    File.WriteAllBytes(e.Path, data);
                }
            }
            catch (Exception ex)
            {
                var rollbackErrors = Rollback(moved, written, backups);
                throw new IOException("Rename failed, nothing was changed: " + ex.Message + rollbackErrors, ex);
            }


            //3. the project: file entries, then content in memory, then save
            foreach (var m in plan.Moves)
            {
                if (m.ProjectFile == null) continue;
                var gf = m.ProjectFile;
                var fname = Path.GetFileName(m.NewPath);
                JenkIndex.Ensure(fname);
                gf.FilePath = m.NewPath;
                gf.Name = fname;
                if (gf.RpfFileEntry != null)
                {
                    gf.RpfFileEntry.Name = fname;
                    gf.RpfFileEntry.NameLower = fname.ToLowerInvariant();
                    gf.RpfFileEntry.NameHash = JenkHash.GenHash(gf.RpfFileEntry.NameLower);
                    gf.RpfFileEntry.ShortNameHash = plan.NewHash;
                }
            }
            foreach (var e in plan.Edits)
            {
                if (!e.IsProjectFile) continue;
                Patch(e, plan);
                if (e.File is YtypFile y) y.HasChanged = true;
                else if (e.File is YmapFile ym) ym.HasChanged = true;
            }
            try
            {
                foreach (var e in plan.Edits)
                {
                    if (!e.IsProjectFile || !e.CanSave) continue;
                    written.Add(e.Path);
                    if (saveProjectFile != null)
                    {
                        saveProjectFile(e.File);
                    }
                    else
                    {
                        File.WriteAllBytes(e.Path, SaveFile(e.File));
                    }
                }
            }
            catch (Exception ex)
            {
                var rollbackErrors = Rollback(moved, written, backups);
                throw new IOException("Saving the project files failed, so the files on disk were restored: " + ex.Message +
                    "\nThe project in memory already uses the new name - close it without saving, then reopen it." + rollbackErrors, ex);
            }
        }

        private static void Patch(ArchetypeRenameFileEdit e, ArchetypeRenamePlan plan)
        {
            var dummy = new ArchetypeRenameFileEdit();
            if (e.File is YtypFile ytyp) PatchYtyp(ytyp, plan, true, dummy);
            else if (e.File is YmapFile ymap) PatchYmap(ymap, plan, true, dummy);
        }

        /// <summary>
        /// A disk-only file patched on a fresh load, never on the cached copy the plan counted from - so a failed
        /// Apply leaves the cache as it was on disk and the same plan can be retried.
        /// </summary>
        private static GameFile Patched(ArchetypeRenameFileEdit e, ArchetypeRenamePlan plan)
        {
            var fresh = LoadDiskFile(e.Path);
            var dummy = new ArchetypeRenameFileEdit();
            if (fresh is YtypFile ytyp) PatchYtyp(ytyp, plan, true, dummy);
            else if (fresh is YmapFile ymap) PatchYmap(ymap, plan, true, dummy);
            return fresh;
        }

        private static byte[] SaveFile(GameFile gf)
        {
            if (gf is YtypFile ytyp) return ytyp.Save();
            if (gf is YmapFile ymap) return ymap.Save();
            throw new InvalidOperationException("Can't save " + gf?.GetType().Name);
        }

        private static string Rollback(List<ArchetypeRenameFileMove> moved, List<string> written, List<Tuple<string, string>> backups)
        {
            var errors = new StringBuilder();
            for (int i = moved.Count - 1; i >= 0; i--)
            {
                try { File.Move(moved[i].NewPath, moved[i].OldPath); }
                catch (Exception ex) { errors.Append("\nCouldn't move back " + moved[i].NewPath + ": " + ex.Message); }
            }
            var writtenSet = new HashSet<string>(written, StringComparer.OrdinalIgnoreCase);
            foreach (var b in backups)
            {
                if (!writtenSet.Contains(b.Item1)) continue; //never touched, or moved back above
                try
                {
                    //the write that failed usually never opened the file (locked / read-only) - it's still intact,
                    //and "restoring" it would just hit the same lock
                    if (File.Exists(b.Item1) && File.ReadAllBytes(b.Item1).SequenceEqual(File.ReadAllBytes(b.Item2))) continue;
                }
                catch { } //can't even read it: fall through and try the restore
                try { File.Copy(b.Item2, b.Item1, true); }
                catch (Exception ex) { errors.Append("\nCouldn't restore " + b.Item1 + ": " + ex.Message); }
            }
            if (errors.Length > 0) errors.Insert(0, "\nRollback problems (the backups are still there):");
            return errors.ToString();
        }

        private static string BackupRelativePath(string path, string root)
        {
            if (!string.IsNullOrEmpty(root))
            {
                var r = Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                if (path.StartsWith(r, StringComparison.OrdinalIgnoreCase))
                {
                    return path.Substring(r.Length);
                }
            }
            //outside the resource (eg. a project file elsewhere): keep its full path, minus the drive colon
            return Path.Combine("_outside", path.Replace(":", string.Empty).TrimStart('\\', '/'));
        }
    }
}
