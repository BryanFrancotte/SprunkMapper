using CodeWalker.Project.Verification;
using System;
using System.Drawing;
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
                        if (issue.Severity == VerificationSeverity.Error)
                        {
                            item.ForeColor = Color.Red;
                        }
                        ResultsListView.Items.Add(item);
                    }

                    UpdateStatus(issues.Count == 0
                        ? "No conflicts found."
                        : $"{issues.Count} issue(s) found.");
                    RunButton.Enabled = true;
                }));
            });
        }
    }
}
