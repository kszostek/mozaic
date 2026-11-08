using System.Globalization;

namespace Mozaic;

/// <summary>
/// Tiny localization: English text is the key, translations live in a table.
/// The UI language follows Windows (Polish if the display language is Polish); override with MOZAIC_LANG=en|pl.
/// </summary>
internal static partial class L
{
    public static bool Polish { get; } = Detect();

    private static bool Detect()
    {
        var env = Environment.GetEnvironmentVariable("MOZAIC_LANG");
        if (!string.IsNullOrEmpty(env)) return env.StartsWith("pl", StringComparison.OrdinalIgnoreCase);
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pl";
    }

    static L() => AddTranslations(Pl);

    /// <summary>Implemented by apps that need more strings (the settings app).</summary>
    static partial void AddTranslations(Dictionary<string, string> pl);

    public static string T(string en) => Polish && Pl.TryGetValue(en, out var pl) ? pl : en;

    public static string F(string en, params object[] args) => string.Format(CultureInfo.CurrentCulture, T(en), args);

    private static readonly Dictionary<string, string> Pl = new(StringComparer.Ordinal)
    {
        // ---- Mozaic (tray, bar, notifications, launcher)
        ["Settings…"] = "Ustawienia…",
        ["Launch application…"] = "Uruchom aplikację…",
        ["Pause tiling"] = "Wstrzymaj kafelkowanie",
        ["Reload configuration"] = "Przeładuj konfigurację",
        ["Edit configuration file"] = "Edytuj plik konfiguracji",
        ["Open log"] = "Otwórz log",
        ["Start with Windows"] = "Uruchamiaj z Windows",
        ["Restart"] = "Uruchom ponownie",
        ["Exit"] = "Zakończ",
        ["{0} window(s)"] = "okna: {0}",
        ["tiling paused"] = "kafelkowanie wstrzymane",
        ["tiling resumed"] = "kafelkowanie wznowione",
        ["config error"] = "błąd konfiguracji",
        ["{0} config errors"] = "błędy konfiguracji: {0}",
        ["Mozaic config"] = "Konfiguracja Mozaic",
        ["{0} errors. First: {1}"] = "Błędów: {0}. Pierwszy: {1}",
        ["Could not run: {0}"] = "Nie można uruchomić: {0}",
        ["Could not start {0}"] = "Nie można uruchomić {0}",
        ["Mozaic (tiling paused)"] = "Mozaic (kafelkowanie wstrzymane)",
        ["The settings app (MozaicSettings.exe) was not found next to Mozaic.exe."] = "Nie znaleziono aplikacji ustawień (MozaicSettings.exe) obok Mozaic.exe.",
        ["run › "] = "uruchom › ",
        ["window › "] = "okno › ",
        ["command › "] = "polecenie › ",
        ["indexing applications…"] = "indeksowanie aplikacji…",
        ["no matches"] = "brak wyników",
        ["run as a command"] = "uruchom jako polecenie",
        ["Run: "] = "Uruchom: ",
        ["Run command: "] = "Wykonaj polecenie: ",
        ["minimized"] = "zminimalizowane",
        ["app"] = "aplikacja",
        ["start menu"] = "menu Start",
        ["settings"] = "ustawienia",
        ["command"] = "polecenie",
        ["Mozaic is already running.\n\nUse the tray icon, \"mozaic-msg exit\", or start with --replace."] =
            "Mozaic już działa.\n\nUżyj ikony w zasobniku, \"mozaic-msg exit\" albo uruchom z --replace.",

        // ---- Keyboard pictures (settings guide and the cheat sheet): categories and labels
        ["Workspaces"] = "Obszary robocze",
        ["Focus"] = "Fokus",
        ["Move window"] = "Przesuwanie okna",
        ["Move to workspace"] = "Na obszar roboczy",
        ["Layout & size"] = "Układ i rozmiar",
        ["Window"] = "Okno",
        ["Launch"] = "Uruchamianie",
        ["Modes"] = "Tryby",
        ["Other"] = "Inne",
        ["focus"] = "fokus",
        ["parent"] = "rodzic",
        ["child"] = "dziecko",
        ["tiled ↔ floating"] = "kafel ↔ pływające",
        ["last window"] = "ostatnie okno",
        ["monitor"] = "monitor",
        ["move"] = "przesuń",
        ["ws to"] = "obszar na",
        ["ws"] = "obszar",
        ["scratchpad"] = "schowek",
        ["center"] = "wyśrodkuj",
        ["next ws"] = "nast. obszar",
        ["prev ws"] = "poprz. obszar",
        ["ws ⇄"] = "obszar ⇄",
        ["split ⇆"] = "podział ⇆",
        ["split ⇅"] = "podział ⇅",
        ["split"] = "podział",
        ["stacked"] = "stos",
        ["tabbed"] = "karty",
        ["next layout"] = "nast. układ",
        ["toggle split"] = "zmień podział",
        ["layout"] = "układ",
        ["balance"] = "wyrównaj",
        ["full screen"] = "pełny ekran",
        ["floating"] = "pływające",
        ["sticky"] = "przypięte",
        ["close"] = "zamknij",
        ["wider"] = "szersze",
        ["narrower"] = "węższe",
        ["taller"] = "wyższe",
        ["shorter"] = "niższe",
        ["resize"] = "rozmiar",
        ["gaps +"] = "odstępy +",
        ["gaps −"] = "odstępy −",
        ["gaps"] = "odstępy",
        ["terminal"] = "terminal",
        ["files"] = "pliki",
        ["run"] = "uruchom",
        ["windows"] = "okna",
        ["shortcuts"] = "skróty",
        ["launcher"] = "programy",
        ["exit mode"] = "wyjdź z trybu",
        ["mode"] = "tryb",
        ["reload"] = "przeładuj",
        ["restart"] = "restart",
        ["exit"] = "zakończ",
        ["confirm"] = "potwierdź",
        ["settings"] = "ustawienia",
        ["pause"] = "wstrzymaj",
        ["Space"] = "Spacja",
        ["cheat sheet"] = "ściągawka",

        // ---- Cheat sheet overlay
        ["release to hide"] = "puść, aby ukryć",
        ["{0} mode"] = "tryb {0}",
        ["no shortcuts on this layer"] = "brak skrótów na tej warstwie",
    };
}
