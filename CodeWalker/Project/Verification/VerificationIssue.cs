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
    }
}
