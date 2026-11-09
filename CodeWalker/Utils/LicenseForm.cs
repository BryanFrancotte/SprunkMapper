using System;
using System.Drawing;
using System.Windows.Forms;

namespace CodeWalker.Utils
{
    /// <summary>
    /// "Activate SprunkMapper": paste a licence key, see straight away whether it's valid.
    /// Built in code (no designer file), like AppUpdater's progress window.
    /// </summary>
    public class LicenseForm : Form
    {
        readonly Func<string, Tuple<LicenseInfo, string>> validate;
        readonly TextBox keyBox;
        readonly Label status;
        readonly Button activate;

        public LicenseInfo License { get; private set; }
        public string KeyText { get { return keyBox.Text; } }

        /// <param name="validate">Returns (info, null) for a usable key, or (null, reason).</param>
        /// <param name="savedKeyProblem">Why the saved key was refused, or null when there was none.</param>
        public LicenseForm(Func<string, Tuple<LicenseInfo, string>> validate, string savedKeyProblem)
        {
            this.validate = validate;

            Text = "Activate SprunkMapper";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            ClientSize = new Size(460, 214);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var intro = new Label
            {
                Left = 12, Top = 12, Width = 436, Height = 34,
                Text = "SprunkMapper needs a personal licence key. Paste the key you were given below " +
                       "(it starts with " + LicenseToken.Prefix + ").",
            };
            keyBox = new TextBox
            {
                Left = 12, Top = 52, Width = 436, Height = 72,
                Multiline = true,
                WordWrap = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font(FontFamily.GenericMonospace, 9f),
            };
            status = new Label { Left = 12, Top = 132, Width = 436, Height = 36 };
            activate = new Button { Text = "Activate", Left = 292, Top = 176, Width = 75, Enabled = false };
            var quit = new Button { Text = "Quit", Left = 373, Top = 176, Width = 75, DialogResult = DialogResult.Cancel };

            Controls.Add(intro);
            Controls.Add(keyBox);
            Controls.Add(status);
            Controls.Add(activate);
            Controls.Add(quit);
            AcceptButton = activate;
            CancelButton = quit;

            keyBox.TextChanged += (s, e) => Revalidate();
            activate.Click += (s, e) =>
            {
                if (Revalidate())
                {
                    DialogResult = DialogResult.OK;
                    Close();
                }
            };

            if (savedKeyProblem != null)
            {
                SetStatus("Your saved key can't be used: " + savedKeyProblem, true);
            }
        }

        bool Revalidate()
        {
            License = null;
            if (LicenseToken.Normalize(keyBox.Text).Length == 0)
            {
                status.Text = "";
                activate.Enabled = false;
                return false;
            }
            var result = validate(keyBox.Text);
            if (result.Item2 != null)
            {
                SetStatus(result.Item2, true);
                activate.Enabled = false;
                return false;
            }
            License = result.Item1;
            SetStatus("✔ Licensed to " + License.Name, false);
            activate.Enabled = true;
            return true;
        }

        void SetStatus(string text, bool error)
        {
            status.Text = text;
            status.ForeColor = error ? Color.Firebrick : Color.ForestGreen;
        }
    }
}
