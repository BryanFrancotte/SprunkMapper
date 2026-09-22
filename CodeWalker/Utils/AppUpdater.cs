using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Velopack;
using Velopack.Sources;

namespace CodeWalker.Utils
{
    /// <summary>
    /// Checks the project's GitHub releases for a newer version at startup and offers to install it.
    /// Releases are made with Velopack (see release.ps1 at the repo root).
    /// </summary>
    public static class AppUpdater
    {
        const string RepoUrl = "https://github.com/BryanFrancotte/SprunkMapper";

        /// <summary>
        /// Call at startup, before any window is open (so there is no unsaved work to lose on restart).
        /// Never throws: being offline or rate-limited just means no update this time.
        /// </summary>
        public static void CheckForUpdates(string[] args)
        {
            UpdateManager mgr;
            UpdateInfo update;
            try
            {
                //releases marked "pre-release" on GitHub are skipped (prerelease: false)
                mgr = new UpdateManager(new GithubSource(RepoUrl, null, false));
                if (!mgr.IsInstalled) return; //running from a build folder, not a Velopack install

                var check = Task.Run(() => mgr.CheckForUpdates());
                if (!check.Wait(TimeSpan.FromSeconds(5))) return; //slow connection: don't hold up startup
                update = check.Result;
            }
            catch
            {
                return;
            }
            if (update == null) return;

            var newver = update.TargetFullRelease.Version;
            var msg = "SprunkMapper " + newver + " is available (you have " + mgr.CurrentVersion + ").\n\n" +
                      "Update now? It downloads in a few seconds, then SprunkMapper restarts.";
            if (MessageBox.Show(msg, "Update available", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
            {
                return; //asked again on the next launch
            }

            var error = Download(mgr, update, newver.ToString());
            if (error != null)
            {
                MessageBox.Show("The update could not be downloaded:\n" + error.Message + "\n\nSprunkMapper will start with the current version.",
                    "Update failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            mgr.ApplyUpdatesAndRestart(update.TargetFullRelease, args); //exits this process
        }

        private static Exception Download(UpdateManager mgr, UpdateInfo update, string version)
        {
            Exception error = null;
            using (var form = new Form())
            {
                form.Text = "Updating SprunkMapper";
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterScreen;
                form.ControlBox = false;
                form.ClientSize = new Size(360, 64);
                try { form.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

                var label = new Label { Left = 12, Top = 10, Width = 336, Text = "Downloading SprunkMapper " + version + "..." };
                var bar = new ProgressBar { Left = 12, Top = 32, Width = 336, Height = 20 };
                form.Controls.Add(label);
                form.Controls.Add(bar);

                form.Shown += (s, e) =>
                {
                    Task.Run(() => mgr.DownloadUpdates(update, p => form.BeginInvoke(new Action(() => bar.Value = Math.Max(0, Math.Min(100, p))))))
                        .ContinueWith(t => form.BeginInvoke(new Action(() =>
                        {
                            error = t.Exception?.GetBaseException();
                            form.Close();
                        })));
                };
                form.ShowDialog();
            }
            return error;
        }
    }
}
