using CodeWalker.GameFiles;
using System.Collections.Generic;

namespace CodeWalker.Project.Verification
{
    public interface IVerificationRule
    {
        string Name { get; }
        IEnumerable<VerificationIssue> Run(ProjectFile project, GameFileCache gameFileCache);
    }
}
