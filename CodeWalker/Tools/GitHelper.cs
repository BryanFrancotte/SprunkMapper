using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace CodeWalker.Tools
{
    //small helpers to run git for files that live in a git repository (used by the Git panel and file saving).
    public static class GitHelper
    {
        public static string FindRepoFolder(string filepath)
        {
            if (string.IsNullOrEmpty(filepath)) return null;
            try
            {
                var dir = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(filepath)));
                while (dir != null)
                {
                    var gitpath = Path.Combine(dir.FullName, ".git");
                    if (Directory.Exists(gitpath) || File.Exists(gitpath)) return dir.FullName;
                    dir = dir.Parent;
                }
            }
            catch { } //invalid path
            return null;
        }

        public static string GetRelativePath(string repo, string filepath)
        {
            if (string.IsNullOrEmpty(repo) || string.IsNullOrEmpty(filepath)) return null;
            var full = Path.GetFullPath(filepath);
            var root = repo.TrimEnd('\\', '/') + "\\";
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
            return full.Substring(root.Length).Replace('\\', '/');
        }

        public static int Run(string folder, string args, StringBuilder output)
        {
            var psi = new ProcessStartInfo("git", args);
            psi.WorkingDirectory = folder;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0"; //never hang waiting for console input

            using (var proc = new Process())
            {
                DataReceivedEventHandler onData = (s, e) =>
                {
                    if (e.Data == null) return;
                    lock (output) { output.AppendLine(e.Data); }
                };
                proc.StartInfo = psi;
                proc.OutputDataReceived += onData;
                proc.ErrorDataReceived += onData;
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                proc.WaitForExit();
                return proc.ExitCode;
            }
        }
    }
}
