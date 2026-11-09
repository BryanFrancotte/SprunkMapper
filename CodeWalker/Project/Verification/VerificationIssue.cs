using CodeWalker.GameFiles;
using SharpDX;
using System;

namespace CodeWalker.Project.Verification
{
    public enum VerificationSeverity
    {
        Warning,
        Error
    }

    public class VerificationIssue
    {
        public VerificationSeverity Severity { get; set; }
        public string RuleName { get; set; }
        public string FileName { get; set; }
        public string Message { get; set; }

        //the props the issue is about - double-clicking the result selects them in the world
        public YmapEntityDef[] Entities { get; set; }
        //area to frame in the world when there are no props to select (eg. a whole .ybn)
        public BoundingBox? FocusBox { get; set; }
    }
}
