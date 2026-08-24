using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace KineticNapier.ADOFAIWorkbench
{
    internal sealed class WelcomePaneProvider : IDockablePaneProvider
    {
        private readonly WelcomePane pane = new WelcomePane();
        public IEnumerable<IDockablePane> CreatePanes()
        {
            yield return pane;
        }
    }

    internal sealed class WelcomePane : IDockablePane
    {
        public string Id { get { return "workbench.welcome"; } }
        public string Title { get { return "Welcome"; } }
        public bool CanClose { get { return false; } }

        public Control CreateView()
        {
            Panel root = new Panel
            {
                BackColor = Color.FromArgb(19, 21, 26),
                Dock = DockStyle.Fill
            };

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = root.BackColor,
                Padding = new Padding(24)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

            FlowLayoutPanel center = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                Anchor = AnchorStyles.None,
                BackColor = root.BackColor,
                MaximumSize = new Size(760, 0)
            };
            center.Controls.Add(new Label
            {
                Text = "ADOFAI Workbench",
                AutoSize = true,
                ForeColor = Color.White,
                Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 24f, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 12)
            });
            center.Controls.Add(new Label
            {
                Text = "Standalone dockable tool window. ADOFAI itself stays untouched; consumer mods can add panes and queue commands back to Unity's main thread.",
                AutoSize = true,
                MaximumSize = new Size(740, 0),
                ForeColor = Color.FromArgb(200, 204, 214),
                Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 11f),
                Margin = new Padding(0)
            });

            layout.Controls.Add(new Panel(), 0, 0);
            layout.Controls.Add(center, 0, 1);
            layout.Controls.Add(new Panel(), 0, 2);
            root.Controls.Add(layout);
            return root;
        }

        public void OnOpened() { }
        public void OnClosed() { }
    }
}
