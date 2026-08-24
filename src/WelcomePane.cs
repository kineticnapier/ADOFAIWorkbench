using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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

        public FrameworkElement CreateView()
        {
            Grid root = new Grid { Background = new SolidColorBrush(Color.FromRgb(19, 21, 26)) };
            StackPanel panel = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 760
            };
            panel.Children.Add(new TextBlock
            {
                Text = "ADOFAI Workbench",
                FontSize = 30,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 14)
            });
            panel.Children.Add(new TextBlock
            {
                Text = "Standalone dockable tool window. ADOFAI itself stays untouched; consumer mods can add panes and queue commands back to Unity's main thread.",
                FontSize = 15,
                Foreground = new SolidColorBrush(Color.FromRgb(200, 204, 214)),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center
            });
            root.Children.Add(panel);
            return root;
        }

        public void OnOpened() { }
        public void OnClosed() { }
    }
}
