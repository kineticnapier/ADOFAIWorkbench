using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace KineticNapier.ADOFAIWorkbench
{
    internal static class WpfWorkbenchWindowHost
    {
        private static readonly object Gate = new object();
        private static readonly Queue<Action> Pending = new Queue<Action>();
        private static Thread thread;
        private static Dispatcher dispatcher;
        private static WorkbenchWindow window;

        internal static void ShowWindow()
        {
            EnsureStarted();
            Invoke(delegate
            {
                if (window == null) return;
                window.Show();
                if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                window.Activate();
            });
        }

        internal static void HideWindow()
        {
            Invoke(delegate { if (window != null) window.Hide(); });
        }

        internal static void OpenPane(string id)
        {
            EnsureStarted();
            Invoke(delegate { if (window != null) window.OpenPane(id); });
        }

        internal static void NotifyRegistryChanged()
        {
            Invoke(delegate { if (window != null) window.RefreshRegistry(); });
        }

        internal static void Invoke(Action action)
        {
            if (action == null) return;
            Dispatcher target;
            lock (Gate)
            {
                target = dispatcher;
                if (target == null)
                {
                    Pending.Enqueue(action);
                    return;
                }
            }
            target.BeginInvoke(action);
        }

        private static void EnsureStarted()
        {
            lock (Gate)
            {
                if (thread != null) return;
                thread = new Thread(ThreadMain);
                thread.IsBackground = true;
                thread.Name = "ADOFAI Workbench WPF";
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
            }
        }

        private static void ThreadMain()
        {
            Dispatcher current = Dispatcher.CurrentDispatcher;
            WorkbenchWindow created = new WorkbenchWindow();
            Action[] pending;
            lock (Gate)
            {
                dispatcher = current;
                window = created;
                pending = Pending.ToArray();
                Pending.Clear();
            }

            created.Show();
            for (int i = 0; i < pending.Length; i++) current.BeginInvoke(pending[i]);
            Dispatcher.Run();
        }
    }

    internal sealed class WorkbenchWindow : Window
    {
        private readonly DockPanel root = new DockPanel();
        private readonly StackPanel launcherPanel = new StackPanel { Orientation = Orientation.Horizontal };
        private readonly Grid contentGrid = new Grid();
        private readonly TabControl leftTabs = new TabControl();
        private readonly TabControl rightTabs = new TabControl();
        private readonly GridSplitter splitter = new GridSplitter();
        private readonly TextBlock status = new TextBlock();
        private readonly Dictionary<string, OpenPaneState> openPanes = new Dictionary<string, OpenPaneState>(StringComparer.Ordinal);
        private TabControl focusedTabs;

        private static readonly Brush WindowBackground = new SolidColorBrush(Color.FromRgb(24, 26, 31));
        private static readonly Brush ChromeBackground = new SolidColorBrush(Color.FromRgb(35, 38, 46));
        private static readonly Brush PaneBackground = new SolidColorBrush(Color.FromRgb(19, 21, 26));
        private static readonly Brush TextBrush = new SolidColorBrush(Color.FromRgb(225, 228, 235));

        internal WorkbenchWindow()
        {
            Title = "ADOFAI Workbench";
            Width = 1100;
            Height = 720;
            MinWidth = 640;
            MinHeight = 420;
            Background = WindowBackground;
            Foreground = TextBrush;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            Closing += OnClosing;
            Content = root;

            BuildToolbar();
            BuildStatusBar();
            BuildContent();
            focusedTabs = leftTabs;
            RefreshRegistry();

            Loaded += delegate
            {
                if (Workbench.FindPane("workbench.welcome") != null)
                    OpenPane("workbench.welcome");
            };
        }

        internal void RefreshRegistry()
        {
            launcherPanel.Children.Clear();
            IList<IDockablePane> panes = Workbench.GetPanesSnapshot();
            for (int i = 0; i < panes.Count; i++)
            {
                IDockablePane pane = panes[i];
                Button button = MakeButton("+ " + pane.Title);
                string id = pane.Id;
                button.Click += delegate { OpenPane(id); };
                launcherPanel.Children.Add(button);
            }

            var stale = new List<string>();
            foreach (KeyValuePair<string, OpenPaneState> pair in openPanes)
                if (Workbench.FindPane(pair.Key) == null) stale.Add(pair.Key);
            for (int i = 0; i < stale.Count; i++) ClosePane(stale[i], true);
        }

        internal void OpenPane(string id)
        {
            IDockablePane pane = Workbench.FindPane(id);
            if (pane == null) return;

            OpenPaneState existing;
            if (openPanes.TryGetValue(id, out existing))
            {
                existing.Owner.SelectedItem = existing.Tab;
                focusedTabs = existing.Owner;
                status.Text = "Focused " + pane.Title;
                return;
            }

            TabControl owner = focusedTabs ?? leftTabs;
            FrameworkElement view;
            try { view = pane.CreateView(); }
            catch (Exception ex)
            {
                view = new TextBlock
                {
                    Text = "Pane failed to create:\n" + ex,
                    Foreground = TextBrush,
                    Margin = new Thickness(12),
                    TextWrapping = TextWrapping.Wrap
                };
            }

            Border host = new Border
            {
                Background = PaneBackground,
                Child = view,
                Padding = new Thickness(0)
            };
            TabItem tab = new TabItem { Content = host };
            tab.Header = BuildTabHeader(pane);
            owner.Items.Add(tab);
            owner.SelectedItem = tab;
            openPanes[id] = new OpenPaneState(pane, tab, owner);
            try { pane.OnOpened(); } catch { }
            status.Text = "Opened " + pane.Title;
        }

        private FrameworkElement BuildTabHeader(IDockablePane pane)
        {
            StackPanel panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new TextBlock
            {
                Text = pane.Title,
                Foreground = TextBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 6, 0)
            });
            if (pane.CanClose)
            {
                Button close = MakeButton("×");
                close.Padding = new Thickness(5, 0, 5, 0);
                close.Margin = new Thickness(0);
                string id = pane.Id;
                close.Click += delegate(object sender, RoutedEventArgs e)
                {
                    e.Handled = true;
                    ClosePane(id, false);
                };
                panel.Children.Add(close);
            }
            return panel;
        }

        private void ClosePane(string id, bool force)
        {
            OpenPaneState state;
            if (!openPanes.TryGetValue(id, out state)) return;
            if (!force && !state.Pane.CanClose) return;
            state.Owner.Items.Remove(state.Tab);
            openPanes.Remove(id);
            try { state.Pane.OnClosed(); } catch { }
            status.Text = "Closed " + state.Pane.Title;
            CollapseRightIfEmpty();
        }

        private void BuildToolbar()
        {
            Border chrome = new Border { Background = ChromeBackground, Padding = new Thickness(6, 4, 6, 4) };
            DockPanel.SetDock(chrome, Dock.Top);
            root.Children.Add(chrome);

            DockPanel bar = new DockPanel();
            chrome.Child = bar;

            StackPanel fixedButtons = new StackPanel { Orientation = Orientation.Horizontal };
            Button split = MakeButton("Split Right");
            split.Click += delegate { ShowRightGroup(); focusedTabs = rightTabs; status.Text = "Focused right group"; };
            fixedButtons.Children.Add(split);
            Button left = MakeButton("Left");
            left.Click += delegate { focusedTabs = leftTabs; status.Text = "Focused left group"; };
            fixedButtons.Children.Add(left);
            Button right = MakeButton("Right");
            right.Click += delegate { ShowRightGroup(); focusedTabs = rightTabs; status.Text = "Focused right group"; };
            fixedButtons.Children.Add(right);
            DockPanel.SetDock(fixedButtons, Dock.Left);
            bar.Children.Add(fixedButtons);

            ScrollViewer launchScroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = launcherPanel,
                Margin = new Thickness(8, 0, 0, 0)
            };
            bar.Children.Add(launchScroll);
        }

        private void BuildStatusBar()
        {
            Border bar = new Border { Background = ChromeBackground, Padding = new Thickness(8, 3, 8, 3) };
            DockPanel.SetDock(bar, Dock.Bottom);
            status.Text = "Workbench ready";
            status.Foreground = TextBrush;
            bar.Child = status;
            root.Children.Add(bar);
        }

        private void BuildContent()
        {
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });

            ConfigureTabs(leftTabs);
            ConfigureTabs(rightTabs);
            leftTabs.PreviewMouseDown += delegate { focusedTabs = leftTabs; };
            rightTabs.PreviewMouseDown += delegate { focusedTabs = rightTabs; };

            splitter.Width = 5;
            splitter.HorizontalAlignment = HorizontalAlignment.Stretch;
            splitter.VerticalAlignment = VerticalAlignment.Stretch;
            splitter.Background = ChromeBackground;
            splitter.ResizeDirection = GridResizeDirection.Columns;
            splitter.ResizeBehavior = GridResizeBehavior.PreviousAndNext;

            Grid.SetColumn(leftTabs, 0);
            Grid.SetColumn(splitter, 1);
            Grid.SetColumn(rightTabs, 2);
            contentGrid.Children.Add(leftTabs);
            contentGrid.Children.Add(splitter);
            contentGrid.Children.Add(rightTabs);
            root.Children.Add(contentGrid);
        }

        private static void ConfigureTabs(TabControl tabs)
        {
            tabs.Background = PaneBackground;
            tabs.Foreground = TextBrush;
            tabs.BorderThickness = new Thickness(0);
        }

        private void ShowRightGroup()
        {
            contentGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            contentGrid.ColumnDefinitions[1].Width = new GridLength(5);
            contentGrid.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
        }

        private void CollapseRightIfEmpty()
        {
            if (rightTabs.Items.Count != 0) return;
            contentGrid.ColumnDefinitions[1].Width = new GridLength(0);
            contentGrid.ColumnDefinitions[2].Width = new GridLength(0);
            focusedTabs = leftTabs;
        }

        private static Button MakeButton(string text)
        {
            return new Button
            {
                Content = text,
                Margin = new Thickness(2, 0, 2, 0),
                Padding = new Thickness(8, 4, 8, 4),
                Background = new SolidColorBrush(Color.FromRgb(50, 54, 64)),
                Foreground = TextBrush,
                BorderBrush = new SolidColorBrush(Color.FromRgb(72, 76, 88)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }

        private sealed class OpenPaneState
        {
            internal readonly IDockablePane Pane;
            internal readonly TabItem Tab;
            internal readonly TabControl Owner;

            internal OpenPaneState(IDockablePane pane, TabItem tab, TabControl owner)
            {
                Pane = pane;
                Tab = tab;
                Owner = owner;
            }
        }
    }
}
