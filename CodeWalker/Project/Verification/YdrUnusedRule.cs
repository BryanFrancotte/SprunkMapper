using CodeWalker.GameFiles;
using System.Collections.Generic;

namespace CodeWalker.Project.Verification
{
    public class YdrUnusedRule : IVerificationRule
    {
        public string Name => "Unused ydr";

        public IEnumerable<VerificationIssue> Run(ProjectFile project, GameFileCache gameFileCache)
        {
            var issues = new List<VerificationIssue>();
            if (project?.YdrFiles == null || project.YdrFiles.Count == 0) return issues;

            var used = GetUsedNameHashes(project);

            foreach (var ydr in project.YdrFiles)
            {
                var fileName = ydr?.RpfFileEntry?.NameLower ?? ydr?.Name;
                if (fileName == null) continue;
                if (!used.Contains(VerificationTargets.FileNameHash(fileName)))
                {
                    issues.Add(new VerificationIssue
                    {
                        Severity = VerificationSeverity.Warning,
                        RuleName = Name,
                        FileName = fileName,
                        Message = $"'{fileName}' isn't used by any archetype in the project's ytyps or any entity in its ymaps."
                    });
                }
            }

            return issues;
        }

        //every name a .ydr can be referenced by: ytyp archetypes (name/asset name), MLO interior entities, and ymap entities
        private static HashSet<uint> GetUsedNameHashes(ProjectFile project)
        {
            var used = new HashSet<uint>();

            if (project.YtypFiles != null)
            {
                foreach (var ytyp in project.YtypFiles)
                {
                    if (ytyp?.AllArchetypes == null) continue;
                    foreach (var arch in ytyp.AllArchetypes)
                    {
                        if (arch == null) continue;
                        used.Add(arch.Hash);
                        used.Add(arch._BaseArchetypeDef.assetName);

                        if (arch is MloArchetype mlo)
                        {
                            AddEntities(used, mlo.entities);
                            if (mlo.entitySets != null)
                            {
                                foreach (var set in mlo.entitySets)
                                {
                                    AddEntities(used, set?.Entities);
                                }
                            }
                        }
                    }
                }
            }

            if (project.YmapFiles != null)
            {
                foreach (var ymap in project.YmapFiles)
                {
                    var entities = ymap?.AllEntities;
                    if (entities == null) continue;
                    foreach (var ent in entities)
                    {
                        if (ent != null) used.Add(ent._CEntityDef.archetypeName);
                    }
                }
            }

            return used;
        }

        private static void AddEntities(HashSet<uint> used, MCEntityDef[] entities)
        {
            if (entities == null) return;
            foreach (var ent in entities)
            {
                if (ent != null) used.Add(ent._Data.archetypeName);
            }
        }
    }
}
