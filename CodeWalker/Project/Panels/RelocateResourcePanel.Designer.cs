namespace CodeWalker.Project.Panels
{
    partial class RelocateResourcePanel
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
            this.LoadFolderButton = new System.Windows.Forms.Button();
            this.StageProjectButton = new System.Windows.Forms.Button();
            this.ClearStagedButton = new System.Windows.Forms.Button();
            this.MoveGroupBox = new System.Windows.Forms.GroupBox();
            this.RotateBareRootCheckBox = new System.Windows.Forms.CheckBox();
            this.YawAngleTextBox = new System.Windows.Forms.TextBox();
            this.YawLabel = new System.Windows.Forms.Label();
            this.PivotTextBox = new System.Windows.Forms.TextBox();
            this.PivotLabel = new System.Windows.Forms.Label();
            this.OffsetTextBox = new System.Windows.Forms.TextBox();
            this.OffsetLabel = new System.Windows.Forms.Label();
            this.ApplyButton = new System.Windows.Forms.Button();
            this.ResetButton = new System.Windows.Forms.Button();
            this.PreviewGroupBox = new System.Windows.Forms.GroupBox();
            this.PreviewCheckBox = new System.Windows.Forms.CheckBox();
            this.MoveWidgetButton = new System.Windows.Forms.Button();
            this.RotateWidgetButton = new System.Windows.Forms.Button();
            this.GoToButton = new System.Windows.Forms.Button();
            this.StatusLabel = new System.Windows.Forms.Label();
            this.MoveGroupBox.SuspendLayout();
            this.PreviewGroupBox.SuspendLayout();
            this.SuspendLayout();
            //
            // LoadFolderButton
            //
            this.LoadFolderButton.Location = new System.Drawing.Point(12, 12);
            this.LoadFolderButton.Name = "LoadFolderButton";
            this.LoadFolderButton.Size = new System.Drawing.Size(150, 23);
            this.LoadFolderButton.TabIndex = 0;
            this.LoadFolderButton.Text = "Load Resource Folder...";
            this.LoadFolderButton.UseVisualStyleBackColor = true;
            this.LoadFolderButton.Click += new System.EventHandler(this.LoadFolderButton_Click);
            //
            // StageProjectButton
            //
            this.StageProjectButton.Location = new System.Drawing.Point(168, 12);
            this.StageProjectButton.Name = "StageProjectButton";
            this.StageProjectButton.Size = new System.Drawing.Size(150, 23);
            this.StageProjectButton.TabIndex = 1;
            this.StageProjectButton.Text = "Stage Project Files";
            this.StageProjectButton.UseVisualStyleBackColor = true;
            this.StageProjectButton.Click += new System.EventHandler(this.StageProjectButton_Click);
            //
            // ClearStagedButton
            //
            this.ClearStagedButton.Location = new System.Drawing.Point(324, 12);
            this.ClearStagedButton.Name = "ClearStagedButton";
            this.ClearStagedButton.Size = new System.Drawing.Size(124, 23);
            this.ClearStagedButton.TabIndex = 2;
            this.ClearStagedButton.Text = "Clear Staged";
            this.ClearStagedButton.UseVisualStyleBackColor = true;
            this.ClearStagedButton.Click += new System.EventHandler(this.ClearStagedButton_Click);
            //
            // MoveGroupBox
            //
            this.MoveGroupBox.Controls.Add(this.RotateBareRootCheckBox);
            this.MoveGroupBox.Controls.Add(this.YawAngleTextBox);
            this.MoveGroupBox.Controls.Add(this.YawLabel);
            this.MoveGroupBox.Controls.Add(this.PivotTextBox);
            this.MoveGroupBox.Controls.Add(this.PivotLabel);
            this.MoveGroupBox.Controls.Add(this.OffsetTextBox);
            this.MoveGroupBox.Controls.Add(this.OffsetLabel);
            this.MoveGroupBox.Location = new System.Drawing.Point(12, 50);
            this.MoveGroupBox.Name = "MoveGroupBox";
            this.MoveGroupBox.Size = new System.Drawing.Size(436, 156);
            this.MoveGroupBox.TabIndex = 1;
            this.MoveGroupBox.TabStop = false;
            this.MoveGroupBox.Text = "Relocate";
            //
            // RotateBareRootCheckBox
            //
            this.RotateBareRootCheckBox.Location = new System.Drawing.Point(12, 106);
            this.RotateBareRootCheckBox.Name = "RotateBareRootCheckBox";
            this.RotateBareRootCheckBox.Size = new System.Drawing.Size(412, 40);
            this.RotateBareRootCheckBox.TabIndex = 6;
            this.RotateBareRootCheckBox.Text = "Rotate standalone (non-composite) collision bounds - experimental";
            this.RotateBareRootCheckBox.UseVisualStyleBackColor = true;
            this.RotateBareRootCheckBox.CheckedChanged += new System.EventHandler(this.RotateBareRootCheckBox_CheckedChanged);
            //
            // YawAngleTextBox
            //
            this.YawAngleTextBox.Location = new System.Drawing.Point(80, 77);
            this.YawAngleTextBox.Name = "YawAngleTextBox";
            this.YawAngleTextBox.Size = new System.Drawing.Size(150, 20);
            this.YawAngleTextBox.TabIndex = 5;
            this.YawAngleTextBox.Text = "0";
            this.YawAngleTextBox.TextChanged += new System.EventHandler(this.YawAngleTextBox_TextChanged);
            //
            // YawLabel
            //
            this.YawLabel.AutoSize = true;
            this.YawLabel.Location = new System.Drawing.Point(12, 80);
            this.YawLabel.Name = "YawLabel";
            this.YawLabel.Size = new System.Drawing.Size(58, 13);
            this.YawLabel.TabIndex = 4;
            this.YawLabel.Text = "Yaw (deg)";
            //
            // PivotTextBox
            //
            this.PivotTextBox.Location = new System.Drawing.Point(80, 49);
            this.PivotTextBox.Name = "PivotTextBox";
            this.PivotTextBox.Size = new System.Drawing.Size(150, 20);
            this.PivotTextBox.TabIndex = 3;
            this.PivotTextBox.Text = "0, 0, 0";
            this.PivotTextBox.TextChanged += new System.EventHandler(this.PivotTextBox_TextChanged);
            //
            // PivotLabel
            //
            this.PivotLabel.AutoSize = true;
            this.PivotLabel.Location = new System.Drawing.Point(12, 52);
            this.PivotLabel.Name = "PivotLabel";
            this.PivotLabel.Size = new System.Drawing.Size(30, 13);
            this.PivotLabel.TabIndex = 2;
            this.PivotLabel.Text = "Pivot";
            //
            // OffsetTextBox
            //
            this.OffsetTextBox.Location = new System.Drawing.Point(80, 21);
            this.OffsetTextBox.Name = "OffsetTextBox";
            this.OffsetTextBox.Size = new System.Drawing.Size(150, 20);
            this.OffsetTextBox.TabIndex = 1;
            this.OffsetTextBox.Text = "0, 0, 0";
            this.OffsetTextBox.TextChanged += new System.EventHandler(this.OffsetTextBox_TextChanged);
            //
            // OffsetLabel
            //
            this.OffsetLabel.AutoSize = true;
            this.OffsetLabel.Location = new System.Drawing.Point(12, 24);
            this.OffsetLabel.Name = "OffsetLabel";
            this.OffsetLabel.Size = new System.Drawing.Size(36, 13);
            this.OffsetLabel.TabIndex = 0;
            this.OffsetLabel.Text = "Offset";
            //
            // PreviewGroupBox
            //
            this.PreviewGroupBox.Controls.Add(this.GoToButton);
            this.PreviewGroupBox.Controls.Add(this.RotateWidgetButton);
            this.PreviewGroupBox.Controls.Add(this.MoveWidgetButton);
            this.PreviewGroupBox.Controls.Add(this.PreviewCheckBox);
            this.PreviewGroupBox.Location = new System.Drawing.Point(12, 212);
            this.PreviewGroupBox.Name = "PreviewGroupBox";
            this.PreviewGroupBox.Size = new System.Drawing.Size(436, 80);
            this.PreviewGroupBox.TabIndex = 2;
            this.PreviewGroupBox.TabStop = false;
            this.PreviewGroupBox.Text = "Viewport preview";
            //
            // PreviewCheckBox
            //
            this.PreviewCheckBox.AutoSize = true;
            this.PreviewCheckBox.Location = new System.Drawing.Point(12, 24);
            this.PreviewCheckBox.Name = "PreviewCheckBox";
            this.PreviewCheckBox.Size = new System.Drawing.Size(291, 17);
            this.PreviewCheckBox.TabIndex = 0;
            this.PreviewCheckBox.Text = "Show group widget in World View (drag it to move the group)";
            this.PreviewCheckBox.UseVisualStyleBackColor = true;
            this.PreviewCheckBox.CheckedChanged += new System.EventHandler(this.PreviewCheckBox_CheckedChanged);
            //
            // MoveWidgetButton
            //
            this.MoveWidgetButton.Enabled = false;
            this.MoveWidgetButton.Location = new System.Drawing.Point(12, 47);
            this.MoveWidgetButton.Name = "MoveWidgetButton";
            this.MoveWidgetButton.Size = new System.Drawing.Size(90, 23);
            this.MoveWidgetButton.TabIndex = 1;
            this.MoveWidgetButton.Text = "Move widget";
            this.MoveWidgetButton.UseVisualStyleBackColor = true;
            this.MoveWidgetButton.Click += new System.EventHandler(this.MoveWidgetButton_Click);
            //
            // RotateWidgetButton
            //
            this.RotateWidgetButton.Enabled = false;
            this.RotateWidgetButton.Location = new System.Drawing.Point(108, 47);
            this.RotateWidgetButton.Name = "RotateWidgetButton";
            this.RotateWidgetButton.Size = new System.Drawing.Size(90, 23);
            this.RotateWidgetButton.TabIndex = 2;
            this.RotateWidgetButton.Text = "Rotate widget";
            this.RotateWidgetButton.UseVisualStyleBackColor = true;
            this.RotateWidgetButton.Click += new System.EventHandler(this.RotateWidgetButton_Click);
            //
            // GoToButton
            //
            this.GoToButton.Location = new System.Drawing.Point(204, 47);
            this.GoToButton.Name = "GoToButton";
            this.GoToButton.Size = new System.Drawing.Size(120, 23);
            this.GoToButton.TabIndex = 3;
            this.GoToButton.Text = "Go to / select group";
            this.GoToButton.UseVisualStyleBackColor = true;
            this.GoToButton.Click += new System.EventHandler(this.GoToButton_Click);
            //
            // ApplyButton
            //
            this.ApplyButton.Location = new System.Drawing.Point(12, 300);
            this.ApplyButton.Name = "ApplyButton";
            this.ApplyButton.Size = new System.Drawing.Size(120, 23);
            this.ApplyButton.TabIndex = 3;
            this.ApplyButton.Text = "Apply";
            this.ApplyButton.UseVisualStyleBackColor = true;
            this.ApplyButton.Click += new System.EventHandler(this.ApplyButton_Click);
            //
            // ResetButton
            //
            this.ResetButton.Location = new System.Drawing.Point(138, 300);
            this.ResetButton.Name = "ResetButton";
            this.ResetButton.Size = new System.Drawing.Size(120, 23);
            this.ResetButton.TabIndex = 4;
            this.ResetButton.Text = "Reset";
            this.ResetButton.UseVisualStyleBackColor = true;
            this.ResetButton.Click += new System.EventHandler(this.ResetButton_Click);
            //
            // StatusLabel
            //
            this.StatusLabel.Location = new System.Drawing.Point(12, 332);
            this.StatusLabel.Name = "StatusLabel";
            this.StatusLabel.Size = new System.Drawing.Size(436, 64);
            this.StatusLabel.TabIndex = 5;
            this.StatusLabel.Text = "";
            //
            // RelocateResourcePanel
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(460, 408);
            this.Controls.Add(this.StatusLabel);
            this.Controls.Add(this.ResetButton);
            this.Controls.Add(this.ApplyButton);
            this.Controls.Add(this.PreviewGroupBox);
            this.Controls.Add(this.MoveGroupBox);
            this.Controls.Add(this.ClearStagedButton);
            this.Controls.Add(this.StageProjectButton);
            this.Controls.Add(this.LoadFolderButton);
            this.Name = "RelocateResourcePanel";
            this.Text = "Relocate Resource";
            this.MoveGroupBox.ResumeLayout(false);
            this.MoveGroupBox.PerformLayout();
            this.PreviewGroupBox.ResumeLayout(false);
            this.PreviewGroupBox.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Button LoadFolderButton;
        private System.Windows.Forms.Button StageProjectButton;
        private System.Windows.Forms.Button ClearStagedButton;
        private System.Windows.Forms.GroupBox MoveGroupBox;
        private System.Windows.Forms.CheckBox RotateBareRootCheckBox;
        private System.Windows.Forms.TextBox YawAngleTextBox;
        private System.Windows.Forms.Label YawLabel;
        private System.Windows.Forms.TextBox PivotTextBox;
        private System.Windows.Forms.Label PivotLabel;
        private System.Windows.Forms.TextBox OffsetTextBox;
        private System.Windows.Forms.Label OffsetLabel;
        private System.Windows.Forms.Button ApplyButton;
        private System.Windows.Forms.Button ResetButton;
        private System.Windows.Forms.GroupBox PreviewGroupBox;
        private System.Windows.Forms.CheckBox PreviewCheckBox;
        private System.Windows.Forms.Button MoveWidgetButton;
        private System.Windows.Forms.Button RotateWidgetButton;
        private System.Windows.Forms.Button GoToButton;
        private System.Windows.Forms.Label StatusLabel;
    }
}
