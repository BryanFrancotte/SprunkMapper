using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CodeWalker.Tools
{
    //dialog for the Git panel's "Save to GitHub": pick the files to commit and a message.
    public class GitSaveForm : Form
    {
        public class GitChange
        {
            public string Path; //relative to the repo root, forward slashes
            public string Status; //two letter porcelain status, eg " M", "??"
            public bool IsNew { get { return Status == "??"; } }
            public override string ToString()
            {
                var kind = IsNew ? "new" : (Status.Contains("D") ? "deleted" : "modified");
                return Path + "  (" + kind + ")";
            }
        }

        //new files with these extensions are ticked by default, anything else (backups, exports...) is not.
        private static readonly HashSet<string> MappingExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".ymap", ".ytyp", ".ydr", ".ydd", ".yft", ".ybn", ".ytd", ".ycd", ".ymf", ".ymt", ".ynv", ".ynd",
            ".lua", ".meta", ".xml", ".cwproj",
        };

        private CheckedListBox FilesListBox;
        private TextBox MessageTextBox;
        private Button SaveButton;

        public List<GitChange> SelectedChanges
        {
            get { return FilesListBox.CheckedItems.Cast<GitChange>().ToList(); }
        }
        public string CommitMessage
        {
            get { return MessageTextBox.Text.Trim(); }
        }

        public GitSaveForm(List<GitChange> changes)
        {
            Text = "Save to GitHub";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            ShowInTaskbar = false;
            MinimumSize = new Size(360, 300);
            ClientSize = new Size(480, 400);

            var filesLabel = new Label() { Text = "Files to save:", AutoSize = true, Location = new Point(9, 9) };

            FilesListBox = new CheckedListBox();
            FilesListBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            FilesListBox.Location = new Point(12, 28);
            FilesListBox.Size = new Size(456, 262);
            FilesListBox.CheckOnClick = true;
            FilesListBox.HorizontalScrollbar = true;
            foreach (var change in changes)
            {
                var ticked = !change.IsNew || MappingExtensions.Contains(System.IO.Path.GetExtension(change.Path));
                FilesListBox.Items.Add(change, ticked);
            }
            FilesListBox.ItemCheck += (s, e) => BeginInvoke(new Action(UpdateSaveButton));

            var messageLabel = new Label() { Text = "Message:", AutoSize = true, Anchor = AnchorStyles.Bottom | AnchorStyles.Left, Location = new Point(9, 300) };

            MessageTextBox = new TextBox();
            MessageTextBox.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            MessageTextBox.Location = new Point(12, 319);
            MessageTextBox.Size = new Size(456, 20);
            MessageTextBox.Text = DefaultMessage(changes);
            MessageTextBox.TextChanged += (s, e) => UpdateSaveButton();

            SaveButton = new Button() { Text = "Save to GitHub", DialogResult = DialogResult.OK };
            SaveButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            SaveButton.Location = new Point(282, 360);
            SaveButton.Size = new Size(105, 28);

            var cancelButton = new Button() { Text = "Cancel", DialogResult = DialogResult.Cancel };
            cancelButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            cancelButton.Location = new Point(393, 360);
            cancelButton.Size = new Size(75, 28);

            Controls.Add(filesLabel);
            Controls.Add(FilesListBox);
            Controls.Add(messageLabel);
            Controls.Add(MessageTextBox);
            Controls.Add(SaveButton);
            Controls.Add(cancelButton);
            AcceptButton = SaveButton;
            CancelButton = cancelButton;

            UpdateSaveButton();
        }

        private void UpdateSaveButton()
        {
            SaveButton.Enabled = (FilesListBox.CheckedItems.Count > 0) && !string.IsNullOrWhiteSpace(MessageTextBox.Text);
        }

        private static string DefaultMessage(List<GitChange> changes)
        {
            var names = changes.Select(c => System.IO.Path.GetFileName(c.Path)).Distinct().ToList();
            var msg = "Update " + string.Join(", ", names.Take(3));
            if (names.Count > 3) msg += " and " + (names.Count - 3) + " more";
            return msg;
        }
    }
}
