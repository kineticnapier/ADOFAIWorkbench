using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace KineticNapier.ADOFAIWorkbench
{
    public sealed class WorkbenchLanguageInfo
    {
        public string Locale { get; private set; }
        public string DisplayName { get; private set; }

        internal WorkbenchLanguageInfo(string locale, string displayName)
        {
            Locale = locale ?? string.Empty;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? Locale : displayName;
        }
    }

    public static class WorkbenchLocalization
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, Dictionary<string, Dictionary<string, string>>> Bundles =
            new Dictionary<string, Dictionary<string, Dictionary<string, string>>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> LocaleDisplayNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly string StateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ADOFAIWorkbench");
        private static readonly string LanguagePath = Path.Combine(StateDirectory, "language.txt");

        private static string currentLanguage = NormalizeLocale(CultureInfo.CurrentUICulture.Name);
        private static bool initialized;

        public static event EventHandler LanguageChanged;

        public static string CurrentLanguage
        {
            get
            {
                EnsureInitialized();
                lock (Gate) return currentLanguage;
            }
        }

        public static IList<WorkbenchLanguageInfo> AvailableLanguages
        {
            get
            {
                EnsureInitialized();
                lock (Gate)
                {
                    var result = new List<WorkbenchLanguageInfo>();
                    foreach (KeyValuePair<string, string> pair in LocaleDisplayNames)
                        result.Add(new WorkbenchLanguageInfo(pair.Key, pair.Value));
                    result.Sort(delegate(WorkbenchLanguageInfo a, WorkbenchLanguageInfo b)
                    {
                        return string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase);
                    });
                    return result;
                }
            }
        }

        public static void Register(
            string ownerId,
            string locale,
            string displayName,
            IDictionary<string, string> translations)
        {
            if (string.IsNullOrWhiteSpace(ownerId)) throw new ArgumentException("ownerId is required.", "ownerId");
            locale = NormalizeLocale(locale);
            if (string.IsNullOrWhiteSpace(locale)) throw new ArgumentException("locale is required.", "locale");

            EnsureInitialized();
            lock (Gate)
            {
                Dictionary<string, Dictionary<string, string>> owner;
                if (!Bundles.TryGetValue(ownerId, out owner))
                    Bundles[ownerId] = owner = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

                var copy = new Dictionary<string, string>(StringComparer.Ordinal);
                if (translations != null)
                {
                    foreach (KeyValuePair<string, string> pair in translations)
                    {
                        if (string.IsNullOrWhiteSpace(pair.Key)) continue;
                        copy[pair.Key] = pair.Value ?? string.Empty;
                    }
                }
                owner[locale] = copy;

                string existing;
                if (!LocaleDisplayNames.TryGetValue(locale, out existing) || string.IsNullOrWhiteSpace(existing))
                    LocaleDisplayNames[locale] = string.IsNullOrWhiteSpace(displayName) ? locale : displayName;
            }
            ExternalWorkbenchHost.LocalizationChanged();
        }

        public static void UnregisterOwner(string ownerId)
        {
            if (string.IsNullOrWhiteSpace(ownerId)) return;
            EnsureInitialized();
            bool changed;
            lock (Gate)
            {
                changed = Bundles.Remove(ownerId);
                if (changed) RebuildLocaleDisplayNamesLocked();
            }
            if (changed) ExternalWorkbenchHost.LocalizationChanged();
        }

        public static string T(string ownerId, string key, string fallback)
        {
            EnsureInitialized();
            if (string.IsNullOrWhiteSpace(key)) return fallback ?? string.Empty;
            if (string.IsNullOrWhiteSpace(ownerId)) return fallback ?? key;

            lock (Gate)
            {
                Dictionary<string, Dictionary<string, string>> owner;
                if (!Bundles.TryGetValue(ownerId, out owner)) return fallback ?? key;

                string translated;
                if (TryResolve(owner, currentLanguage, key, out translated)) return translated;
                if (TryResolve(owner, "en-US", key, out translated)) return translated;
                if (TryResolve(owner, "en", key, out translated)) return translated;
                return fallback ?? key;
            }
        }

        public static string Format(string ownerId, string key, string fallback, params object[] args)
        {
            string format = T(ownerId, key, fallback);
            try { return string.Format(CultureInfo.CurrentCulture, format, args ?? new object[0]); }
            catch (FormatException) { return format; }
        }

        public static bool SetLanguage(string locale)
        {
            return SetLanguageCore(locale, true);
        }

        internal static void InitializeWorkbenchBundle()
        {
            EnsureInitialized();
            Register("workbench", "en-US", "English", new Dictionary<string, string>
            {
                { "chrome.panes", "Panes" },
                { "chrome.language", "Language" },
                { "chrome.saveLayout", "Save Layout" },
                { "chrome.resetLayout", "Reset Layout" },
                { "chrome.openPane", "Open a pane" },
                { "chrome.waiting", "Waiting for ADOFAI..." },
                { "chrome.noPanes", "(No panes received)" },
                { "chrome.layoutSaved", "Layout saved" },
                { "chrome.layoutReset", "Layout reset" },
                { "chrome.syncing", "Connected | syncing panes..." },
                { "chrome.connected", "Connected | Panes={0}" },
                { "chrome.unknownPane", "Unknown pane: {0}" },
                { "chrome.layoutSaveFailed", "Layout save failed: {0}" }
            });
            Register("workbench", "ja-JP", "日本語", new Dictionary<string, string>
            {
                { "chrome.panes", "パネル" },
                { "chrome.language", "言語" },
                { "chrome.saveLayout", "レイアウト保存" },
                { "chrome.resetLayout", "レイアウトをリセット" },
                { "chrome.openPane", "パネルを開く" },
                { "chrome.waiting", "ADOFAIを待機中..." },
                { "chrome.noPanes", "(パネルがありません)" },
                { "chrome.layoutSaved", "レイアウトを保存しました" },
                { "chrome.layoutReset", "レイアウトをリセットしました" },
                { "chrome.syncing", "接続済み | パネル同期中..." },
                { "chrome.connected", "接続済み | パネル={0}" },
                { "chrome.unknownPane", "不明なパネル: {0}" },
                { "chrome.layoutSaveFailed", "レイアウト保存失敗: {0}" }
            });
        }

        internal static void SetLanguageFromHost(string locale)
        {
            SetLanguageCore(locale, true);
        }

        internal static IList<WorkbenchLanguageInfo> GetLanguagesSnapshot()
        {
            return AvailableLanguages;
        }

        private static bool SetLanguageCore(string locale, bool persist)
        {
            locale = NormalizeLocale(locale);
            if (string.IsNullOrWhiteSpace(locale)) return false;
            EnsureInitialized();

            EventHandler handler = null;
            lock (Gate)
            {
                if (string.Equals(currentLanguage, locale, StringComparison.OrdinalIgnoreCase)) return false;
                currentLanguage = locale;
                handler = LanguageChanged;
            }

            if (persist) SaveLanguage(locale);
            ExternalWorkbenchHost.LocalizationChanged();
            if (handler != null)
            {
                try { handler(null, EventArgs.Empty); } catch (Exception ex) { Main.LogError("LanguageChanged subscriber failed", ex); }
            }
            return true;
        }

        private static bool TryResolve(
            Dictionary<string, Dictionary<string, string>> owner,
            string locale,
            string key,
            out string value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(locale)) return false;

            Dictionary<string, string> bundle;
            if (owner.TryGetValue(locale, out bundle) && bundle.TryGetValue(key, out value)) return true;

            int dash = locale.IndexOf('-');
            string neutral = dash > 0 ? locale.Substring(0, dash) : locale;
            if (!string.Equals(neutral, locale, StringComparison.OrdinalIgnoreCase)
                && owner.TryGetValue(neutral, out bundle) && bundle.TryGetValue(key, out value)) return true;

            foreach (KeyValuePair<string, Dictionary<string, string>> pair in owner)
            {
                string registered = pair.Key;
                if (!registered.StartsWith(neutral + "-", StringComparison.OrdinalIgnoreCase)) continue;
                if (pair.Value.TryGetValue(key, out value)) return true;
            }
            return false;
        }

        private static void EnsureInitialized()
        {
            lock (Gate)
            {
                if (initialized) return;
                initialized = true;
                string saved = LoadLanguage();
                if (!string.IsNullOrWhiteSpace(saved)) currentLanguage = NormalizeLocale(saved);
                if (string.IsNullOrWhiteSpace(currentLanguage)) currentLanguage = "en-US";
            }
        }

        private static string NormalizeLocale(string locale)
        {
            if (string.IsNullOrWhiteSpace(locale)) return string.Empty;
            locale = locale.Trim().Replace('_', '-');
            try { return CultureInfo.GetCultureInfo(locale).Name; }
            catch { return locale; }
        }

        private static string LoadLanguage()
        {
            try { return File.Exists(LanguagePath) ? File.ReadAllText(LanguagePath).Trim() : string.Empty; }
            catch { return string.Empty; }
        }

        private static void SaveLanguage(string locale)
        {
            try
            {
                Directory.CreateDirectory(StateDirectory);
                File.WriteAllText(LanguagePath, locale ?? string.Empty);
            }
            catch { }
        }

        private static void RebuildLocaleDisplayNamesLocked()
        {
            LocaleDisplayNames.Clear();
            foreach (KeyValuePair<string, Dictionary<string, Dictionary<string, string>>> owner in Bundles)
            {
                foreach (string locale in owner.Value.Keys)
                    if (!LocaleDisplayNames.ContainsKey(locale)) LocaleDisplayNames[locale] = locale;
            }
        }
    }
}
