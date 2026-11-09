using CodeWalker.Project.Verification;
using BoundingBox = SharpDX.BoundingBox;
using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodeWalker.Project.Panels
{
    public partial class VerificationServicePanel : ProjectPanel
    {
        public ProjectForm ProjectForm { get; set; }
        public ProjectFile CurrentProjectFile { get; set; }

        private readonly VerificationService verificationService = new VerificationService();

        public VerificationServicePanel(ProjectForm projectForm)
        {
            ProjectForm = projectForm;
            InitializeComponent();
        }

        public void SetProject(ProjectFile project)
        {
            CurrentProjectFile = project;
        }

        private void UpdateStatus(string text)
        {
            try
            {
                if (InvokeRequired)
                {
                    Invoke(new Action(() => { UpdateStatus(text); }));
                }
                else
                {
                    StatusLabel.Text = text;
                }
            }
            catch { }
        }

        private void RunButton_Click(object sender, EventArgs e)
        {
            var project = CurrentProjectFile;
            var gameFileCache = ProjectForm?.GameFileCache;
            if (project == null)
            {
                MessageBox.Show("No project is open!");
                return;
            }
            if (gameFileCache == null)
            {
                MessageBox.Show("Game file cache is not available!");
                return;
            }

            RunButton.Enabled = false;
            UpdateStatus("Running verification...");
            ResultsListView.Items.Clear();

            Task.Run(() =>
            {
                var issues = verificationService.RunAll(project, gameFileCache);

                Invoke(new Action(() =>
                {
                    foreach (var issue in issues)
                    {
                        var item = new ListViewItem(issue.Severity.ToString());
                        item.SubItems.Add(issue.RuleName);
                        item.SubItems.Add(issue.FileName);
                        item.SubItems.Add(issue.Message);
                        item.Tag = issue;
                        if (issue.Severity == VerificationSeverity.Error)
                        {
                            item.ForeColor = Color.Red;
                        }
                        ResultsListView.Items.Add(item);
                    }

                    UpdateStatus(issues.Count == 0
                        ? "No conflicts found."
                        : $"{issues.Count} issue(s) found. Double-click one to show it in the world.");
                    RunButton.Enabled = true;
                }));
            });
        }

        private void ResultsListView_DoubleClick(object sender, EventArgs e)
        {
            if (ResultsListView.SelectedItems.Count == 0) return;
            var issue = ResultsListView.SelectedItems[0].Tag as VerificationIssue;
            if (issue == null) return;

            var worldForm = ProjectForm?.WorldForm;
            if (worldForm == null)
            {
                UpdateStatus("The world view isn't open.");
                return;
            }

            //skip props deleted (or whose ymap was removed) since the verification ran
            var entities = issue.Entities?.Where(ent =>
                (ent?.Ymap != null) &&
                (CurrentProjectFile?.YmapFiles?.Contains(ent.Ymap) ?? false) &&
                (ent.Ymap.AllEntities?.Contains(ent) ?? false)).ToArray();

            if ((entities != null) && (entities.Length > 0))
            {
                //selecting props opens their edit panel - keep these results in their own tab rather than letting it replace them
                ProjectForm.PromoteIfPreviewPanel(this);
                FocusBox(worldForm, VerificationTargets.GetBounds(entities).Value);
                if (entities.Length == 1)
                {
                    worldForm.SelectObject(entities[0]);
                }
                else
                {
                    worldForm.SelectMulti(Array.ConvertAll(entities, ent => MapSelection.FromProjectObject(worldForm, ent)));
                }
                Activate();
                UpdateStatus((entities.Length == 1)
                    ? $"Showing '{entities[0].Name}' in the world."
                    : $"Showing {entities.Length} props in the world.");
            }
            else if (issue.FocusBox.HasValue)
            {
                FocusBox(worldForm, issue.FocusBox.Value);
                UpdateStatus($"Showing '{issue.FileName}' in the world.");
            }
            else
            {
                UpdateStatus("This issue has no props placed in the world to show.");
            }
        }

        private static void FocusBox(WorldForm worldForm, BoundingBox box)
        {
            worldForm.GoToPosition((box.Minimum + box.Maximum) * 0.5f, box.Maximum - box.Minimum);
        }
    }
}
