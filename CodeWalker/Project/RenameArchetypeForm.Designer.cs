namespace CodeWalker.Project
{
    partial class RenameArchetypeForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.NameLabel = new System.Windows.Forms.Label();
            this.NewNameTextBox = new System.Windows.Forms.TextBox();
            this.ScanFolderCheckBox = new System.Windows.Forms.CheckBox();
            this.FolderTextBox = new System.Windows.Forms.TextBox();
            this.FolderBrowseButton = new System.Windows.Forms.Button();
            this.OptionsFlowPanel = new System.Windows.Forms.FlowLayoutPanel();
            this.TextureDictCheckBox = new System.Windows.Forms.CheckBox();
            this.PhysicsDictCheckBox = new System.Windows.Forms.CheckBox();
            this.DrawableDictCheckBox = new System.Windows.Forms.CheckBox();
            this.PreviewLabel = new System.Windows.Forms.Label();
            this.PreviewTextBox = new System.Windows.Forms.RichTextBox();
            this.BackupLabel = new System.Windows.Forms.Label();
            this.RenameButton = new System.Windows.Forms.Button();
            this.CancelRenameButton = new System.Windows.Forms.Button();
            this.PlanTimer = new System.Windows.Forms.Timer(this.components);
            this.OptionsFlowPanel.SuspendLayout();
            this.SuspendLayout();
            //
            // NameLabel
            //
            this.NameLabel.AutoSize = true;
            this.NameLabel.Location = new System.Drawing.Point(12, 12);
            this.NameLabel.Name = "NameLabel";
            this.NameLabel.Size = new System.Drawing.Size(80, 13);
            this.NameLabel.TabIndex = 0;
            this.NameLabel.Text = "Rename  to:";
            //
            // NewNameTextBox
            //
            this.NewNameTextBox.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.NewNameTextBox.Location = new System.Drawing.Point(15, 30);
            this.NewNameTextBox.Name = "NewNameTextBox";
            this.NewNameTextBox.Size = new System.Drawing.Size(537, 20);
            this.NewNameTextBox.TabIndex = 1;
            this.NewNameTextBox.TextChanged += new System.EventHandler(this.NewNameTextBox_TextChanged);
            //
            // ScanFolderCheckBox
            //
            this.ScanFolderCheckBox.AutoSize = true;
            this.ScanFolderCheckBox.Checked = true;
            this.ScanFolderCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            this.ScanFolderCheckBox.Location = new System.Drawing.Point(15, 62);
            this.ScanFolderCheckBox.Name = "ScanFolderCheckBox";
            this.ScanFolderCheckBox.Size = new System.Drawing.Size(380, 17);
            this.ScanFolderCheckBox.TabIndex = 2;
            this.ScanFolderCheckBox.Text = "Also update the files in this resource folder that aren\'t open in the project:";
            this.ScanFolderCheckBox.UseVisualStyleBackColor = true;
            this.ScanFolderCheckBox.CheckedChanged += new System.EventHandler(this.OptionChanged);
            //
            // FolderTextBox
            //
            this.FolderTextBox.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.FolderTextBox.Location = new System.Drawing.Point(15, 84);
            this.FolderTextBox.Name = "FolderTextBox";
            this.FolderTextBox.ReadOnly = true;
            this.FolderTextBox.Size = new System.Drawing.Size(456, 20);
            this.FolderTextBox.TabIndex = 3;
            //
            // FolderBrowseButton
            //
            this.FolderBrowseButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.FolderBrowseButton.Location = new System.Drawing.Point(477, 82);
            this.FolderBrowseButton.Name = "FolderBrowseButton";
            this.FolderBrowseButton.Size = new System.Drawing.Size(75, 23);
            this.FolderBrowseButton.TabIndex = 4;
            this.FolderBrowseButton.Text = "Browse...";
            this.FolderBrowseButton.UseVisualStyleBackColor = true;
            this.FolderBrowseButton.Click += new System.EventHandler(this.FolderBrowseButton_Click);
            //
            // OptionsFlowPanel
            //
            this.OptionsFlowPanel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.OptionsFlowPanel.Controls.Add(this.TextureDictCheckBox);
            this.OptionsFlowPanel.Controls.Add(this.PhysicsDictCheckBox);
            this.OptionsFlowPanel.Controls.Add(this.DrawableDictCheckBox);
            this.OptionsFlowPanel.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.OptionsFlowPanel.Location = new System.Drawing.Point(12, 112);
            this.OptionsFlowPanel.Name = "OptionsFlowPanel";
            this.OptionsFlowPanel.Size = new System.Drawing.Size(540, 70);
            this.OptionsFlowPanel.TabIndex = 5;
            this.OptionsFlowPanel.WrapContents = false;
            //
            // TextureDictCheckBox
            //
            this.TextureDictCheckBox.AutoSize = true;
            this.TextureDictCheckBox.Checked = true;
            this.TextureDictCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            this.TextureDictCheckBox.Location = new System.Drawing.Point(3, 3);
            this.TextureDictCheckBox.Name = "TextureDictCheckBox";
            this.TextureDictCheckBox.Size = new System.Drawing.Size(200, 17);
            this.TextureDictCheckBox.TabIndex = 0;
            this.TextureDictCheckBox.Text = "Also rename the .ytd";
            this.TextureDictCheckBox.UseVisualStyleBackColor = true;
            this.TextureDictCheckBox.Visible = false;
            this.TextureDictCheckBox.CheckedChanged += new System.EventHandler(this.OptionChanged);
            //
            // PhysicsDictCheckBox
            //
            this.PhysicsDictCheckBox.AutoSize = true;
            this.PhysicsDictCheckBox.Checked = true;
            this.PhysicsDictCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            this.PhysicsDictCheckBox.Location = new System.Drawing.Point(3, 26);
            this.PhysicsDictCheckBox.Name = "PhysicsDictCheckBox";
            this.PhysicsDictCheckBox.Size = new System.Drawing.Size(200, 17);
            this.PhysicsDictCheckBox.TabIndex = 1;
            this.PhysicsDictCheckBox.Text = "Also rename the .ybn";
            this.PhysicsDictCheckBox.UseVisualStyleBackColor = true;
            this.PhysicsDictCheckBox.Visible = false;
            this.PhysicsDictCheckBox.CheckedChanged += new System.EventHandler(this.OptionChanged);
            //
            // DrawableDictCheckBox
            //
            this.DrawableDictCheckBox.AutoSize = true;
            this.DrawableDictCheckBox.Checked = true;
            this.DrawableDictCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            this.DrawableDictCheckBox.Location = new System.Drawing.Point(3, 49);
            this.DrawableDictCheckBox.Name = "DrawableDictCheckBox";
            this.DrawableDictCheckBox.Size = new System.Drawing.Size(200, 17);
            this.DrawableDictCheckBox.TabIndex = 2;
            this.DrawableDictCheckBox.Text = "Also rename the .ydd";
            this.DrawableDictCheckBox.UseVisualStyleBackColor = true;
            this.DrawableDictCheckBox.Visible = false;
            this.DrawableDictCheckBox.CheckedChanged += new System.EventHandler(this.OptionChanged);
            //
            // PreviewLabel
            //
            this.PreviewLabel.AutoSize = true;
            this.PreviewLabel.Location = new System.Drawing.Point(12, 186);
            this.PreviewLabel.Name = "PreviewLabel";
            this.PreviewLabel.Size = new System.Drawing.Size(117, 13);
            this.PreviewLabel.TabIndex = 6;
            this.PreviewLabel.Text = "What the rename does:";
            //
            // PreviewTextBox
            //
            this.PreviewTextBox.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.PreviewTextBox.BackColor = System.Drawing.SystemColors.Window;
            this.PreviewTextBox.DetectUrls = false;
            this.PreviewTextBox.Font = new System.Drawing.Font("Consolas", 9F);
            this.PreviewTextBox.Location = new System.Drawing.Point(15, 204);
            this.PreviewTextBox.Name = "PreviewTextBox";
            this.PreviewTextBox.ReadOnly = true;
            this.PreviewTextBox.Size = new System.Drawing.Size(537, 244);
            this.PreviewTextBox.TabIndex = 7;
            this.PreviewTextBox.Text = "";
            this.PreviewTextBox.WordWrap = false;
            //
            // BackupLabel
            //
            this.BackupLabel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.BackupLabel.ForeColor = System.Drawing.SystemColors.GrayText;
            this.BackupLabel.Location = new System.Drawing.Point(12, 455);
            this.BackupLabel.Name = "BackupLabel";
            this.BackupLabel.Size = new System.Drawing.Size(378, 30);
            this.BackupLabel.TabIndex = 8;
            this.BackupLabel.Text = "Every file is backed up first.";
            //
            // RenameButton
            //
            this.RenameButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.RenameButton.Enabled = false;
            this.RenameButton.Location = new System.Drawing.Point(396, 460);
            this.RenameButton.Name = "RenameButton";
            this.RenameButton.Size = new System.Drawing.Size(75, 23);
            this.RenameButton.TabIndex = 9;
            this.RenameButton.Text = "Rename";
            this.RenameButton.UseVisualStyleBackColor = true;
            this.RenameButton.Click += new System.EventHandler(this.RenameButton_Click);
            //
            // CancelRenameButton
            //
            this.CancelRenameButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.CancelRenameButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CancelRenameButton.Location = new System.Drawing.Point(477, 460);
            this.CancelRenameButton.Name = "CancelRenameButton";
            this.CancelRenameButton.Size = new System.Drawing.Size(75, 23);
            this.CancelRenameButton.TabIndex = 10;
            this.CancelRenameButton.Text = "Cancel";
            this.CancelRenameButton.UseVisualStyleBackColor = true;
            //
            // PlanTimer
            //
            this.PlanTimer.Interval = 250;
            this.PlanTimer.Tick += new System.EventHandler(this.PlanTimer_Tick);
            //
            // RenameArchetypeForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.CancelRenameButton;
            this.ClientSize = new System.Drawing.Size(564, 495);
            this.Controls.Add(this.CancelRenameButton);
            this.Controls.Add(this.RenameButton);
            this.Controls.Add(this.BackupLabel);
            this.Controls.Add(this.PreviewTextBox);
            this.Controls.Add(this.PreviewLabel);
            this.Controls.Add(this.OptionsFlowPanel);
            this.Controls.Add(this.FolderBrowseButton);
            this.Controls.Add(this.FolderTextBox);
            this.Controls.Add(this.ScanFolderCheckBox);
            this.Controls.Add(this.NewNameTextBox);
            this.Controls.Add(this.NameLabel);
            this.MinimizeBox = false;
            this.MinimumSize = new System.Drawing.Size(480, 420);
            this.Name = "RenameArchetypeForm";
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Rename Archetype";
            this.OptionsFlowPanel.ResumeLayout(false);
            this.OptionsFlowPanel.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label NameLabel;
        private System.Windows.Forms.TextBox NewNameTextBox;
        private System.Windows.Forms.CheckBox ScanFolderCheckBox;
        private System.Windows.Forms.TextBox FolderTextBox;
        private System.Windows.Forms.Button FolderBrowseButton;
        private System.Windows.Forms.FlowLayoutPanel OptionsFlowPanel;
        private System.Windows.Forms.CheckBox TextureDictCheckBox;
        private System.Windows.Forms.CheckBox PhysicsDictCheckBox;
        private System.Windows.Forms.CheckBox DrawableDictCheckBox;
        private System.Windows.Forms.Label PreviewLabel;
        private System.Windows.Forms.RichTextBox PreviewTextBox;
        private System.Windows.Forms.Label BackupLabel;
        private System.Windows.Forms.Button RenameButton;
        private System.Windows.Forms.Button CancelRenameButton;
        private System.Windows.Forms.Timer PlanTimer;
    }
}
