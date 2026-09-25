using CodeWalker.GameFiles;
using System.Collections.Generic;

namespace CodeWalker.Project.Verification
{
    public class YbnBaseGameConflictRule : IVerificationRule
    {
        public string Name => "Ybn base game conflict";

        public IEnumerable<VerificationIssue> Run(ProjectFile project, GameFileCache gameFileCache)
        {
            var issues = new List<VerificationIssue>();
            var rpfMan = gameFileCache?.RpfMan;
            if (project?.YbnFiles == null || rpfMan == null) return issues;

            var ybnNames = BaseGameFileNameIndex.GetBaseGameFileNames(rpfMan, ".ybn");

            foreach (var ybn in project.YbnFiles)
            {
                var fileName = ybn?.RpfFileEntry?.NameLower ?? ybn?.Name;
                if (fileName != null && ybnNames.Contains(fileName))
                {
                    issues.Add(new VerificationIssue
                    {
                        Severity = VerificationSeverity.Error,
                        RuleName = Name,
                        FileName = fileName,
                        Message = $"'{fileName}' conflicts with a base game bounds file (.ybn) of the same name."
                    });
                }
            }

            return issues;
        }
    }
}
