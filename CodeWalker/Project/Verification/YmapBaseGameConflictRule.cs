using CodeWalker.GameFiles;
using System.Collections.Generic;

namespace CodeWalker.Project.Verification
{
    public class YmapBaseGameConflictRule : IVerificationRule
    {
        public string Name => "Ymap base game conflict";

        public IEnumerable<VerificationIssue> Run(ProjectFile project, GameFileCache gameFileCache)
        {
            var issues = new List<VerificationIssue>();
            var rpfMan = gameFileCache?.RpfMan;
            if (project?.YmapFiles == null || rpfMan == null) return issues;

            var ymapNames = BaseGameFileNameIndex.GetBaseGameFileNames(rpfMan, ".ymap");

            foreach (var ymap in project.YmapFiles)
            {
                var fileName = ymap?.RpfFileEntry?.NameLower ?? ymap?.Name;
                if (fileName != null && ymapNames.Contains(fileName))
                {
                    issues.Add(new VerificationIssue
                    {
                        Severity = VerificationSeverity.Error,
                        RuleName = Name,
                        FileName = fileName,
                        Message = $"'{fileName}' conflicts with a base game map container (.ymap) of the same name."
                    });
                }
            }

            return issues;
        }
    }
}
