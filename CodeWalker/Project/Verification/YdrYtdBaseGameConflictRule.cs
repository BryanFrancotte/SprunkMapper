using CodeWalker.GameFiles;
using System.Collections.Generic;

namespace CodeWalker.Project.Verification
{
    public class YdrYtdBaseGameConflictRule : IVerificationRule
    {
        public string Name => "Ydr/Ytd base game conflict";

        public IEnumerable<VerificationIssue> Run(ProjectFile project, GameFileCache gameFileCache)
        {
            var issues = new List<VerificationIssue>();
            var rpfMan = gameFileCache?.RpfMan;
            if (project == null || rpfMan == null) return issues;

            var ydrNames = BaseGameFileNameIndex.GetBaseGameFileNames(rpfMan, ".ydr");
            if (project.YdrFiles != null)
            {
                foreach (var ydr in project.YdrFiles)
                {
                    var fileName = ydr?.RpfFileEntry?.NameLower ?? ydr?.Name;
                    if (fileName != null && ydrNames.Contains(fileName))
                    {
                        issues.Add(new VerificationIssue
                        {
                            Severity = VerificationSeverity.Error,
                            RuleName = Name,
                            FileName = fileName,
                            Message = $"'{fileName}' conflicts with a base game drawable (.ydr) of the same name."
                        });
                    }
                }
            }

            var ytdNames = BaseGameFileNameIndex.GetBaseGameFileNames(rpfMan, ".ytd");
            if (project.YtdFiles != null)
            {
                foreach (var ytd in project.YtdFiles)
                {
                    var fileName = ytd?.RpfFileEntry?.NameLower ?? ytd?.Name;
                    if (fileName != null && ytdNames.Contains(fileName))
                    {
                        issues.Add(new VerificationIssue
                        {
                            Severity = VerificationSeverity.Error,
                            RuleName = Name,
                            FileName = fileName,
                            Message = $"'{fileName}' conflicts with a base game texture dictionary (.ytd) of the same name."
                        });
                    }
                }
            }

            return issues;
        }
    }
}
