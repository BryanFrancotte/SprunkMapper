using CodeWalker.GameFiles;
using System.Collections.Generic;

namespace CodeWalker.Project.Verification
{
    public class ArchetypeBaseGameConflictRule : IVerificationRule
    {
        public string Name => "Ytyp base game conflict";

        public IEnumerable<VerificationIssue> Run(ProjectFile project, GameFileCache gameFileCache)
        {
            var issues = new List<VerificationIssue>();
            if (project?.YtypFiles == null || gameFileCache == null) return issues;

            foreach (var ytyp in project.YtypFiles)
            {
                if (ytyp?.AllArchetypes == null) continue;
                var fileName = ytyp.RpfFileEntry?.NameLower ?? ytyp.Name;

                foreach (var arch in ytyp.AllArchetypes)
                {
                    if (arch == null) continue;
                    if (gameFileCache.TryGetBaseGameArchetype(arch.Hash, out var baseArch) && baseArch != null)
                    {
                        issues.Add(new VerificationIssue
                        {
                            Severity = VerificationSeverity.Error,
                            RuleName = Name,
                            FileName = fileName,
                            Message = $"Archetype '{arch.Name}' conflicts with a base game archetype of the same name."
                        });
                    }
                }
            }
            return issues;
        }
    }
}
