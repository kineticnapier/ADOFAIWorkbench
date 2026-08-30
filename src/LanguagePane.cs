using System;
using System.Collections.Generic;

namespace KineticNapier.ADOFAIWorkbench
{
    internal sealed class LanguagePaneProvider : IDockablePaneProvider
    {
        internal static readonly LanguagePaneProvider Instance = new LanguagePaneProvider();
        private readonly LanguagePane pane = new LanguagePane();

        private LanguagePaneProvider() { }

        public IEnumerable<IDockablePane> CreatePanes()
        {
            yield return pane;
        }
    }

    internal sealed class LanguagePane : IDockablePane
    {
        public string Id { get { return "workbench.language"; } }
        public string Title { get { return WorkbenchLocalization.T("workbench", "language.title", "Language"); } }
        public bool CanClose { get { return true; } }

        public WorkbenchPaneView BuildView()
        {
            var view = new WorkbenchPaneView()
                .Text(WorkbenchLocalization.T("workbench", "language.heading", "Workbench Language"), 16f, true)
                .Text(WorkbenchLocalization.T("workbench", "language.description", "Choose the language used by Workbench and localization-aware panes."), 10f, false)
                .Spacer(8);

            string current = WorkbenchLocalization.CurrentLanguage;
            IList<WorkbenchLanguageInfo> languages = WorkbenchLocalization.AvailableLanguages;
            for (int i = 0; i < languages.Count; i++)
            {
                WorkbenchLanguageInfo language = languages[i];
                bool selected = string.Equals(language.Locale, current, StringComparison.OrdinalIgnoreCase);
                view.Button((selected ? "✓ " : "") + language.DisplayName, "language", language.Locale, selected, !selected);
            }

            if (languages.Count == 0)
                view.Text(WorkbenchLocalization.T("workbench", "language.none", "No languages are registered."), 10f, false);

            view.Spacer(8)
                .Text(WorkbenchLocalization.Format("workbench", "language.current", "Current: {0}", current), 9f, false);
            return view;
        }

        public void HandleAction(string actionId, string argument)
        {
            if (!string.Equals(actionId, "language", StringComparison.Ordinal)) return;
            WorkbenchLocalization.SetLanguage(argument);
            Workbench.PublishPane(Id);
        }
    }
}
