using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

        public static int Run(string folder, string args, StringBuilder output, string input = null)
        {
            var psi = new ProcessStartInfo("git", args);
            psi.WorkingDirectory = folder;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.RedirectStandardInput = (input != null);
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
                if (input != null)
                {
                    //write raw UTF-8: on .NET Framework StandardInput uses the console code page, which breaks accents
                    var bytes = new UTF8Encoding(false).GetBytes(input);
                    proc.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
                    proc.StandardInput.Close();
                }
                proc.WaitForExit();
                return proc.ExitCode;
            }
        }

        //returns the paths (relative to the repo, forward slashes) that .gitattributes marks as "lockable".
        public static List<string> GetLockablePaths(string repo, IEnumerable<string> relpaths)
        {
            var result = new List<string>();
            var list = relpaths.Where(p => !string.IsNullOrEmpty(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if ((repo == null) || (list.Count == 0)) return result;

            //.NET Framework can write an encoding preamble (BOM) to stdin before our bytes, so the first
            //entry is a throwaway that absorbs it. only paths matching the input exactly are returned.
            var input = "_\0" + string.Join("\0", list) + "\0";
            var output = new StringBuilder();
            int code;
            try
            {
                code = Run(repo, "-c core.quotepath=false check-attr -z --stdin lockable", output, input);
            }
            catch
            {
                return result; //git not installed
            }
            if (code != 0) return result;

            //-z output: "path\0lockable\0value\0" for each path
            var wanted = new HashSet<string>(list, StringComparer.Ordinal);
            var parts = output.ToString().Split('\0');
            for (int i = 0; i + 2 < parts.Length; i += 3)
            {
                var path = parts[i].Trim('\r', '\n');
                var value = parts[i + 2].Trim('\r', '\n');
                if ((value == "set") && wanted.Contains(path)) result.Add(path);
            }
            return result;
        }
    }
}
