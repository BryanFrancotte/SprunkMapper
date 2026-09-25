using CodeWalker.GameFiles;
using System.Collections.Generic;

namespace CodeWalker.Project.Verification
{
    public class VerificationService
    {
        private readonly List<IVerificationRule> rules = new List<IVerificationRule>
        {
            new ArchetypeBaseGameConflictRule(),
            new YdrYtdBaseGameConflictRule(),
            new YbnBaseGameConflictRule(),
            new YmapBaseGameConflictRule(),
            new YmapEntityRadiusRule(),
        };

        public List<VerificationIssue> RunAll(ProjectFile project, GameFileCache gameFileCache)
        {
            var issues = new List<VerificationIssue>();
            foreach (var rule in rules)
            {
                var ruleIssues = rule.Run(project, gameFileCache);
                if (ruleIssues != null) issues.AddRange(ruleIssues);
            }
            return issues;
        }
    }
}
