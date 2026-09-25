namespace CodeWalker.Project.Panels
{
    partial class VerificationServicePanel
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
            this.RunButton = new System.Windows.Forms.Button();
            this.StatusLabel = new System.Windows.Forms.Label();
            this.ResultsListView = new System.Windows.Forms.ListView();
            this.SeverityColumnHeader = new System.Windows.Forms.ColumnHeader();
            this.RuleColumnHeader = new System.Windows.Forms.ColumnHeader();
            this.FileColumnHeader = new System.Windows.Forms.ColumnHeader();
            this.MessageColumnHeader = new System.Windows.Forms.ColumnHeader();
            this.SuspendLayout();
            //
            // RunButton
            //
            this.RunButton.Location = new System.Drawing.Point(12, 12);
            this.RunButton.Name = "RunButton";
            this.RunButton.Size = new System.Drawing.Size(120, 23);
            this.RunButton.TabIndex = 0;
            this.RunButton.Text = "Run Verification";
            this.RunButton.UseVisualStyleBackColor = true;
            this.RunButton.Click += new System.EventHandler(this.RunButton_Click);
            //
            // StatusLabel
            //
            this.StatusLabel.AutoSize = true;
            this.StatusLabel.Location = new System.Drawing.Point(144, 17);
            this.StatusLabel.Name = "StatusLabel";
            this.StatusLabel.Size = new System.Drawing.Size(83, 13);
            this.StatusLabel.TabIndex = 1;
            this.StatusLabel.Text = "Ready to verify";
            //
            // ResultsListView
            //
            this.ResultsListView.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.ResultsListView.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.SeverityColumnHeader,
            this.RuleColumnHeader,
            this.FileColumnHeader,
            this.MessageColumnHeader});
            this.ResultsListView.FullRowSelect = true;
            this.ResultsListView.GridLines = true;
            this.ResultsListView.HideSelection = false;
            this.ResultsListView.Location = new System.Drawing.Point(12, 44);
            this.ResultsListView.Name = "ResultsListView";
            this.ResultsListView.Size = new System.Drawing.Size(776, 394);
            this.ResultsListView.TabIndex = 2;
            this.ResultsListView.UseCompatibleStateImageBehavior = false;
            this.ResultsListView.View = System.Windows.Forms.View.Details;
            //
            // SeverityColumnHeader
            //
            this.SeverityColumnHeader.Text = "Severity";
            this.SeverityColumnHeader.Width = 70;
            //
            // RuleColumnHeader
            //
            this.RuleColumnHeader.Text = "Rule";
            this.RuleColumnHeader.Width = 170;
            //
            // FileColumnHeader
            //
            this.FileColumnHeader.Text = "File";
            this.FileColumnHeader.Width = 170;
            //
            // MessageColumnHeader
            //
            this.MessageColumnHeader.Text = "Message";
            this.MessageColumnHeader.Width = 350;
            //
            // VerificationServicePanel
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(800, 450);
            this.Controls.Add(this.ResultsListView);
            this.Controls.Add(this.StatusLabel);
            this.Controls.Add(this.RunButton);
            this.Name = "VerificationServicePanel";
            this.Text = "Verification Service";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button RunButton;
        private System.Windows.Forms.Label StatusLabel;
        private System.Windows.Forms.ListView ResultsListView;
        private System.Windows.Forms.ColumnHeader SeverityColumnHeader;
        private System.Windows.Forms.ColumnHeader RuleColumnHeader;
        private System.Windows.Forms.ColumnHeader FileColumnHeader;
        private System.Windows.Forms.ColumnHeader MessageColumnHeader;
    }
}
