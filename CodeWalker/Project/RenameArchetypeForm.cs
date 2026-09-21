using CodeWalker.GameFiles;
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CodeWalker.Project
{
    /// <summary>
    /// Rename Archetype: previews everything ArchetypeRenamer will touch, then applies it in one go.
    /// </summary>
    public partial class RenameArchetypeForm : Form
    {
        private const int MaxFolderFiles = 20000; //a resource is hundreds of files - more means the wrong folder was picked

        private readonly ProjectForm ProjectForm;
        private readonly ArchetypeRenameRequest Request;
        private ArchetypeRenamePlan Plan;

        public RenameArchetypeForm(ProjectForm projectForm, Archetype archetype)
        {
            InitializeComponent();

            ProjectForm = projectForm;
            Request = projectForm.CreateArchetypeRenameRequest(archetype);

            var oldName = JenkIndex.TryGetString(archetype._BaseArchetypeDef.name.Hash);
            if (string.IsNullOrEmpty(oldName)) oldName = archetype.Name;
            Text = "Rename Archetype - " + oldName;
            NameLabel.Text = "Rename '" + oldName + "' to:";
            FolderTextBox.Text = Request.ResourceRoot ?? string.Empty;
            ScanFolderCheckBox.Checked = !string.IsNullOrEmpty(Request.ResourceRoot);
            BackupLabel.Text = "Every file is backed up before it's changed, to " + Request.BackupRoot;
            NewNameTextBox.Text = oldName;
            PlanTimer.Stop(); //setting the text above started it - OnShown plans straight away instead
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            NewNameTextBox.SelectAll();
            NewNameTextBox.Focus();
            Replan();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            PlanTimer.Stop();
            base.OnFormClosed(e);
        }


        private void Replan()
        {
            PlanTimer.Stop();

            Request.NewName = NewNameTextBox.Text;
            Request.ResourceRoot = (ScanFolderCheckBox.Checked && !string.IsNullOrEmpty(FolderTextBox.Text)) ? FolderTextBox.Text : null;
            Request.RenameTextureDictionary = TextureDictCheckBox.Checked;
            Request.RenamePhysicsDictionary = PhysicsDictCheckBox.Checked;
            Request.RenameDrawableDictionary = DrawableDictCheckBox.Checked;

            Cursor = Cursors.WaitCursor;
            try
            {
                Plan = ArchetypeRenamer.BuildPlan(Request);
            }
            catch (Exception ex)
            {
                Plan = null;
                PreviewTextBox.Clear();
                Add("Couldn't build the preview: " + ex.Message, Color.Firebrick);
                RenameButton.Enabled = false;
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            TextureDictCheckBox.Text = "Also rename " + Plan.OldName + ".ytd (its texture dictionary)";
            PhysicsDictCheckBox.Text = "Also rename " + Plan.OldName + ".ybn (its collision / physics dictionary)";
            DrawableDictCheckBox.Text = "Also rename " + Plan.OldName + ".ydd (its drawable dictionary)";
            TextureDictCheckBox.Visible = Plan.HasTextureDictFile;
            PhysicsDictCheckBox.Visible = Plan.HasPhysicsDictFile;
            DrawableDictCheckBox.Visible = Plan.HasDrawableDictFile;

            RenderPreview();
            RenameButton.Enabled = Plan.CanApply;
        }

        private void RenderPreview()
        {
            var normal = SystemColors.WindowText;
            var error = Color.Firebrick;
            var warning = Color.DarkOrange;
            var newName = (Plan.NewHash != 0) ? Plan.NewName : "<new name>";

            PreviewTextBox.SuspendLayout();
            PreviewTextBox.Clear();

            foreach (var e in Plan.Errors)
            {
                Add(e, error, true);
            }
            if (Plan.Errors.Count > 0) Add("", normal);

            int placementFiles = Plan.Edits.Count(e => e.EntityRefs > 0);
            Add(Plan.OldName + "  ->  " + newName, normal, true);
            Add(Plural(Plan.TotalEntityRefs, "placement") + " in " + Plural(placementFiles, "file") + ", " + Plural(Plan.Moves.Count, "file") + " renamed.", normal);

            if (Plan.Moves.Count > 0)
            {
                Add("", normal);
                Add("Files renamed", normal, true);
                foreach (var m in Plan.Moves)
                {
                    Add("  " + Display(m.OldPath) + "  ->  " + newName + Path.GetExtension(m.OldPath).ToLowerInvariant() + ((m.ProjectFile != null) ? "   (in project)" : ""), normal);
                }
            }

            if (Plan.Edits.Count > 0)
            {
                Add("", normal);
                Add("Files updated", normal, true);
                foreach (var e in Plan.Edits)
                {
                    var what = new System.Collections.Generic.List<string>();
                    bool isYtyp = e.File is YtypFile;
                    if (e.ArchetypeRefs > 0) what.Add(isYtyp && (e.ArchetypeRefs == 1) ? "the definition" : Plural(e.ArchetypeRefs, "definition"));
                    if (e.EntityRefs > 0) what.Add(Plural(e.EntityRefs, "placement") + (isYtyp ? " inside interiors" : ""));
                    if (e.OtherRefs > 0) what.Add(Plural(e.OtherRefs, "physics dictionary ref"));

                    string where;
                    var color = normal;
                    if (!e.IsProjectFile) where = "on disk";
                    else if (!e.CanSave) { where = "in project - never saved, so NOT saved now"; color = warning; }
                    else if (e.HadUnsavedChanges) { where = "in project - its other unsaved edits get saved too"; color = warning; }
                    else where = "in project";

                    Add("  " + Display(e.Path ?? e.File?.Name) + "  -  " + string.Join(", ", what) + "   (" + where + ")", color);
                }
            }

            if (Plan.Warnings.Count > 0)
            {
                Add("", normal);
                foreach (var w in Plan.Warnings)
                {
                    Add(w, warning);
                }
            }

            PreviewTextBox.SelectionStart = 0;
            PreviewTextBox.ScrollToCaret();
            PreviewTextBox.ResumeLayout();
        }

        private void Add(string text, Color color, bool bold = false)
        {
            PreviewTextBox.SelectionStart = PreviewTextBox.TextLength;
            PreviewTextBox.SelectionLength = 0;
            PreviewTextBox.SelectionColor = color;
            PreviewTextBox.SelectionFont = new Font(PreviewTextBox.Font, bold ? FontStyle.Bold : FontStyle.Regular);
            PreviewTextBox.AppendText(text + "\n");
        }

        private string Display(string path)
        {
            if (string.IsNullOrEmpty(path)) return "?";
            var root = FolderTextBox.Text;
            if (!string.IsNullOrEmpty(root))
            {
                var r = root.TrimEnd('\\', '/') + "\\";
                if (path.StartsWith(r, StringComparison.OrdinalIgnoreCase)) return path.Substring(r.Length);
            }
            return path;
        }

        private static string Plural(int n, string word)
        {
            return n.ToString() + " " + word + ((n == 1) ? "" : "s");
        }


        private void NewNameTextBox_TextChanged(object sender, EventArgs e)
        {
            //re-plan once typing pauses - each plan re-scans the resource folder
            PlanTimer.Stop();
            PlanTimer.Start();
        }

        private void PlanTimer_Tick(object sender, EventArgs e)
        {
            Replan();
        }

        private void OptionChanged(object sender, EventArgs e)
        {
            if (!Visible) return; //still being set up in the constructor
            Replan();
        }

        private void FolderBrowseButton_Click(object sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.SelectedPath = FolderTextBox.Text;
                if (fbd.ShowDialogNew() != DialogResult.OK) return;

                int count = 0;
                try
                {
                    count = Directory.EnumerateFiles(fbd.SelectedPath, "*", SearchOption.AllDirectories).Take(MaxFolderFiles + 1).Count();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Couldn't read that folder: " + ex.Message, "Rename Archetype");
                    return;
                }
                if (count > MaxFolderFiles)
                {
                    MessageBox.Show(this, "That folder has more than " + MaxFolderFiles + " files. Pick the resource folder itself (the one with fxmanifest.lua).", "Rename Archetype");
                    return;
                }

                FolderTextBox.Text = fbd.SelectedPath;
                ScanFolderCheckBox.Checked = true; //replans via OptionChanged if it was unchecked
                Replan();
            }
        }

        private void RenameButton_Click(object sender, EventArgs e)
        {
            Replan(); //the files may have changed since the preview was built
            if ((Plan == null) || !Plan.CanApply) return;

            try
            {
                Cursor = Cursors.WaitCursor;
                ProjectForm.ApplyArchetypeRename(Plan);
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                MessageBox.Show(this, ex.Message, "Rename failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Request.DiskCache.Clear();
                Replan();
                return;
            }
            Cursor = Cursors.Default;

            MessageBox.Show(this,
                "Renamed '" + Plan.OldName + "' to '" + Plan.NewName + "':\n" +
                Plural(Plan.TotalEntityRefs, "placement") + " updated, " + Plural(Plan.Moves.Count, "file") + " renamed.\n\n" +
                "Backups of every changed file:\n" + Plan.BackupFolder,
                "Rename Archetype", MessageBoxButtons.OK, MessageBoxIcon.Information);

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
