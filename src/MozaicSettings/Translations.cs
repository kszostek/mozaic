namespace Mozaic;

/// <summary>Polish texts of the settings app (English text is the key).</summary>
internal static partial class L
{
    static partial void AddTranslations(Dictionary<string, string> pl)
    {
        void A(string en, string text) => pl[en] = text;

        // ---- Window, navigation, save bar
        A("Mozaic Settings", "Ustawienia Mozaic");
        A("Tiling window manager", "Kafelkowy menedżer okien");
        A("Guide", "Przewodnik");
        A("General", "Ogólne");
        A("Appearance", "Wygląd");
        A("Bar", "Pasek");
        A("Keyboard shortcuts", "Skróty klawiszowe");
        A("Window rules", "Reguły okien");
        A("Workspaces", "Obszary robocze");
        A("Startup", "Uruchamianie");
        A("Config file", "Plik konfiguracji");
        A("Diagnostics", "Diagnostyka");
        A("Discard changes", "Odrzuć zmiany");
        A("Save and apply", "Zapisz i zastosuj");
        A("Saved and applied.", "Zapisano i zastosowano.");
        A("Saved. Mozaic is not running.", "Zapisano. Mozaic nie jest uruchomiony.");
        A("Could not save: ", "Nie można zapisać: ");
        A("Changes discarded.", "Zmiany odrzucone.");
        A("Save the changes before closing?", "Zapisać zmiany przed zamknięciem?");
        A("1 problem in the configuration", "1 problem w konfiguracji");
        A("{0} problems in the configuration", "Problemy w konfiguracji: {0}");
        A("Mozaic is running ({0} windows)", "Mozaic działa (okna: {0})");
        A("Mozaic is paused", "Mozaic jest wstrzymany");
        A("Mozaic is not running", "Mozaic nie jest uruchomiony");
        A("Start Mozaic", "Uruchom Mozaic");
        A("On", "Wł.");
        A("Off", "Wył.");
        A("OK", "OK");
        A("Cancel", "Anuluj");
        A("Edit", "Edytuj");
        A("Delete", "Usuń");
        A("Refresh", "Odśwież");

        // ---- Guide
        A("Welcome to Mozaic", "Witaj w Mozaic");
        A("Mozaic arranges your windows automatically, side by side and without overlapping, the way the i3 window manager does on Linux. Everything works from the keyboard, and the mouse works too.",
            "Mozaic sam układa okna obok siebie, bez nakładania się — tak jak menedżer okien i3 w Linuksie. Wszystko zrobisz z klawiatury, a mysz też działa.");
        A("Mozaic is not running. The shortcuts below work while it runs.", "Mozaic nie jest uruchomiony. Poniższe skróty działają, gdy Mozaic działa.");
        A("Hold {0} and press a key:", "Przytrzymaj {0} i naciśnij klawisz:");
        A("After {0} the keys below do this (Esc leaves the mode):", "Po {0} poniższe klawisze działają tak (Esc kończy tryb):");
        A("Hover a key to see the exact command.", "Najedź na klawisz, aby zobaczyć dokładne polecenie.");
        A("{0} mode", "tryb {0}");
        A("The basics", "Podstawy");
        A("Tiling", "Kafelkowanie");
        A("Layouts", "Układy");
        A("Floating windows", "Okna pływające");
        A("Mouse", "Mysz");
        A("Launcher", "Uruchamianie programów");
        A("New windows open next to the focused one and share the space. {split h} and {split v} decide whether the next window opens beside or below. {focus left} {focus down} {focus up} {focus right} move the focus; add Shift to move the window itself.",
            "Nowe okna otwierają się obok aktywnego i dzielą się miejscem. {split h} i {split v} decydują, czy następne okno otworzy się obok, czy pod spodem. {focus left} {focus down} {focus up} {focus right} przenoszą fokus; z Shiftem przesuwają samo okno.");
        A("{workspace number 1} … {workspace number 10} switch between workspaces, {move container to workspace number 1} … moves the window there. Windows on hidden workspaces leave the taskbar until you come back.",
            "{workspace number 1} … {workspace number 10} przełączają obszary robocze, {move container to workspace number 1} … przenosi tam okno. Okna z ukrytych obszarów znikają z paska zadań, dopóki nie wrócisz.");
        A("{layout tabbed} shows windows as tabs, {layout stacking} stacks their titles, {layout toggle split} goes back to side by side. {focus parent} selects the whole container, so you can move several windows at once.",
            "{layout tabbed} pokazuje okna jako karty, {layout stacking} układa ich tytuły w stos, {layout toggle split} wraca do układu obok siebie. {focus parent} zaznacza cały kontener, więc możesz przenieść kilka okien naraz.");
        A("{floating toggle} lets a window float above the others and {focus mode_toggle} switches between floating and tiled windows. Dialogs float automatically. {move scratchpad} hides a window in the scratchpad, {scratchpad show} brings it back.",
            "{floating toggle} sprawia, że okno pływa nad innymi, a {focus mode_toggle} przełącza fokus między oknami pływającymi i kafelkami. Okna dialogowe pływają automatycznie. {move scratchpad} chowa okno do schowka, {scratchpad show} je przywraca.");
        A("Drag a window by its title bar onto another window to place it there; a blue preview shows where it will land. Drag an edge to resize. Hold $mod and drag with the left button to move a window, with the right button to resize it.",
            "Przeciągnij okno za pasek tytułu na inne okno, aby je tam umieścić — niebieski podgląd pokazuje, gdzie wyląduje. Przeciągnij krawędź, aby zmienić rozmiar. Przytrzymaj $mod i przeciągnij lewym przyciskiem, aby przesunąć okno, prawym — aby zmienić rozmiar.");
        A("{launcher} starts any application, {launcher windows} jumps to any window, {launcher commands} lists every shortcut. {exec $term} opens a terminal and {kill} closes the focused window.",
            "{launcher} uruchamia dowolny program, {launcher windows} przeskakuje do dowolnego okna, {launcher commands} wyświetla wszystkie skróty. {exec $term} otwiera terminal, a {kill} zamyka aktywne okno.");
        A("Show this guide when Mozaic starts", "Pokazuj ten przewodnik przy starcie Mozaic");
        A("Takes effect after saving. You can always open it from the tray icon.", "Działa po zapisaniu. Zawsze możesz go otworzyć z ikony w zasobniku.");
        A("Change shortcuts", "Zmień skróty");
        A("Documentation on GitHub", "Dokumentacja na GitHubie");
        A("i3 User Guide", "Podręcznik i3");
        A("Resize mode", "Tryb zmiany rozmiaru");
        A("Cheat sheet", "Ściągawka");
        A("Hold {cheatsheet} anywhere to see this keyboard on top of your windows; press Shift as well for the $mod + Shift keys. It disappears as soon as you let go.",
            "Przytrzymaj {cheatsheet} w dowolnym miejscu, a ta klawiatura pojawi się nad oknami; wciśnij też Shift, aby zobaczyć skróty $mod + Shift. Znika, gdy tylko puścisz klawisze.");
        A("Groups and focus", "Grupy i fokus");
        A("Lines in the gaps join the windows of one tile (the part of the screen you split with {split v} or {split h}), and a current runs along them when the focus moves or while $mod is held. Windows you haven't used for a while fade a little, so the active one stands out.",
            "Linie w odstępach łączą okna jednego kafelka (części ekranu podzielonej przez {split v} lub {split h}), a gdy przenosisz fokus albo trzymasz $mod, płynie po nich prąd. Okna nieużywane od dłuższego czasu lekko przygasają, więc aktywne się wyróżnia.");

        // ---- General
        A("How Mozaic behaves. Changes are applied when you save.", "Jak zachowuje się Mozaic. Zmiany zostaną zastosowane po zapisaniu.");
        A("Keyboard", "Klawiatura");
        A("Modifier key ($mod)", "Klawisz modyfikatora ($mod)");
        A("All shortcuts start with this key. Alt avoids conflicts with Windows (Win+L cannot be changed).",
            "Wszystkie skróty zaczynają się od tego klawisza. Alt unika konfliktów z Windows (Win+L nie da się zmienić).");
        A("Terminal ($term)", "Terminal ($term)");
        A("Program started by $mod+Enter.", "Program uruchamiany przez $mod+Enter.");
        A("Autotiling", "Autokafelkowanie");
        A("New windows split the focused window along its longer side (spiral). Off: classic i3 behaviour.",
            "Nowe okna dzielą aktywne okno wzdłuż dłuższego boku (spirala). Wyłączone: klasyczne zachowanie i3.");
        A("Default split direction", "Domyślny kierunek podziału");
        A("Direction of windows on a new workspace.", "Kierunek układania okien na nowym obszarze roboczym.");
        A("Automatic (by monitor shape)", "Automatycznie (wg kształtu monitora)");
        A("Side by side", "Obok siebie");
        A("Stacked vertically", "Jedno pod drugim");
        A("Layout of new workspaces", "Układ nowych obszarów roboczych");
        A("Tabbed or stacked workspaces show one window at a time.", "W kartach lub w stosie widać jedno okno naraz.");
        A("Split", "Podział");
        A("Tabbed", "Karty");
        A("Stacked", "Stos");
        A("Remove unnecessary containers", "Usuwaj zbędne kontenery");
        A("Containers left with a single window are removed automatically.", "Kontenery z jednym oknem są usuwane automatycznie.");
        A("Focus follows mouse", "Fokus podąża za myszą");
        A("The window under the mouse pointer gets the focus.", "Fokus dostaje okno pod kursorem myszy.");
        A("Move the mouse to the focused monitor", "Przenoś kursor na aktywny monitor");
        A("When the keyboard moves the focus to another monitor, the mouse pointer follows.", "Gdy klawiatura przeniesie fokus na inny monitor, kursor myszy podąży za nim.");
        A("Wrap around at the edges", "Zawijanie na krawędziach");
        A("Moving the focus past the last window jumps to the first one.", "Przejście za ostatnie okno wraca do pierwszego.");
        A("Workspace back and forth", "Powrót do poprzedniego obszaru");
        A("Pressing the key of the current workspace returns to the previous one.", "Naciśnięcie klawisza bieżącego obszaru wraca do poprzedniego.");
        A("When an application activates itself", "Gdy aplikacja sama się aktywuje");
        A("For example a chat window that pops up on another workspace.", "Na przykład okno czatu, które wyskakuje na innym obszarze roboczym.");
        A("Focus it if visible, otherwise mark it urgent", "Aktywuj, jeśli widoczne, w przeciwnym razie oznacz jako pilne");
        A("Only mark it urgent", "Tylko oznacz jako pilne");
        A("Always focus it (switches workspace)", "Zawsze aktywuj (przełącza obszar)");
        A("Ignore it", "Ignoruj");
        A("Drag tiled windows", "Przeciąganie kafelków");
        A("Drop a window on another one to rearrange; drag an edge to resize the split.", "Upuść okno na inne, aby zmienić układ; przeciągnij krawędź, aby zmienić podział.");
        A("$mod + drag", "$mod + przeciąganie");
        A("Hold $mod and drag with the left button to move, with the right button to resize.", "Przytrzymaj $mod i przeciągnij lewym przyciskiem, aby przesunąć, prawym — aby zmienić rozmiar.");
        A("Windows", "Okna");
        A("Remember the layout", "Zapamiętuj układ");
        A("After restarting Mozaic, windows that still exist return to their places.", "Po ponownym uruchomieniu Mozaic istniejące okna wracają na swoje miejsca.");
        A("Keep floating windows on top", "Okna pływające zawsze na wierzchu");
        A("Floating windows stay above tiled windows (like i3).", "Okna pływające pozostają nad kafelkami (jak w i3).");
        A("On-screen display", "Komunikat na ekranie");
        A("Briefly show the workspace name or mode in the middle of the screen.", "Na chwilę pokaż nazwę obszaru lub trybu na środku ekranu.");
        A("Show the guide at startup", "Pokazuj przewodnik przy starcie");
        A("Open the shortcut guide every time Mozaic starts.", "Otwieraj przewodnik po skrótach przy każdym starcie Mozaic.");

        // ---- Appearance
        A("Gaps, borders, colors and font. The preview updates as you change things.", "Odstępy, ramki, kolory i czcionka. Podgląd zmienia się na bieżąco.");
        A("Gaps", "Odstępy");
        A("Between windows", "Między oknami");
        A("Space between neighbouring windows, in pixels at 100% scaling.", "Odstęp między sąsiednimi oknami, w pikselach przy skali 100%.");
        A("At the screen edges", "Przy krawędziach ekranu");
        A("Added to the gap between windows.", "Dodawany do odstępu między oknami.");
        A("Smart gaps", "Inteligentne odstępy");
        A("No gaps when a workspace has only one window.", "Bez odstępów, gdy na obszarze jest tylko jedno okno.");
        A("Borders", "Ramki");
        A("Colored window borders", "Kolorowe ramki okien");
        A("Windows 11 draws the border of each window in the colors below (focused / unfocused).", "Windows 11 rysuje ramkę każdego okna w kolorach poniżej (aktywne / nieaktywne).");
        A("Thick focus frame", "Gruba ramka aktywnego okna");
        A("Extra frame around the focused window, like i3 (0 = off).", "Dodatkowa ramka wokół aktywnego okna, jak w i3 (0 = wyłączona).");
        A("Window corners", "Narożniki okien");
        A("Effects", "Efekty");
        A("Group lines", "Linie grup");
        A("Lines in the gaps between windows of the same tile show which windows belong together. A current flows along them when the focus or layout changes.",
            "Linie w odstępach między oknami tego samego kafelka pokazują, które okna są razem. Gdy zmienia się fokus lub układ, płynie po nich prąd.");
        A("Color of the current", "Kolor prądu");
        A("Color of the group lines and the current flowing along them.", "Kolor linii grup i płynącego po nich prądu.");
        A("Flowing after changes and while $mod is held", "Prąd po zmianach i gdy trzymasz $mod");
        A("Always flowing", "Prąd przez cały czas");
        A("Fade windows you are not using", "Przygaszaj nieużywane okna");
        A("The active window is opaque; the longer ago a window was used, the more transparent it gets.",
            "Aktywne okno jest w pełni widoczne; im dawniej używane okno, tym bardziej przezroczyste.");
        A("Previously used window", "Poprzednio używane okno");
        A("Opacity of the window you used before the active one.", "Krycie okna używanego tuż przed aktywnym.");
        A("Windows used long ago", "Okna używane dawno");
        A("Older windows fade down to this opacity.", "Starsze okna stopniowo przygasają do tego krycia.");
        A("Windows default", "Domyślne Windows");
        A("Rounded", "Zaokrąglone");
        A("Slightly rounded", "Lekko zaokrąglone");
        A("Square (like i3)", "Kwadratowe (jak w i3)");
        A("Font", "Czcionka");
        A("Font of the bar, tabs and launcher", "Czcionka paska, kart i launchera");
        A("Window colors", "Kolory okien");
        A("Window border", "Ramka okna");
        A("Tab background", "Tło karty");
        A("Tab border", "Obramowanie karty");
        A("Tab text", "Tekst karty");
        A("Focused", "Aktywne");
        A("Focused on another monitor", "Aktywne na innym monitorze");
        A("Unfocused", "Nieaktywne");
        A("Urgent", "Pilne");
        A("Click to choose a color", "Kliknij, aby wybrać kolor");

        // ---- Bar
        A("The i3bar-style bar with workspaces, the focused window and status information.", "Pasek w stylu i3bar z obszarami roboczymi, aktywnym oknem i informacjami o stanie.");
        A("Show the bar", "Pokazuj pasek");
        A("Position", "Położenie");
        A("Top", "Na górze");
        A("Bottom", "Na dole");
        A("Height", "Wysokość");
        A("0 = automatic, from the font size.", "0 = automatycznie, wg rozmiaru czcionki.");
        A("Contents", "Zawartość");
        A("Workspace buttons", "Przyciski obszarów roboczych");
        A("Application icons on workspace buttons", "Ikony aplikacji na przyciskach obszarów");
        A("Hide workspace numbers", "Ukryj numery obszarów");
        A("Show only the name of workspaces such as 2:web.", "Pokazuj tylko nazwę obszarów typu 2:web.");
        A("Title of the focused window", "Tytuł aktywnego okna");
        A("Binding mode indicator", "Wskaźnik trybu");
        A("Shows resize when the resize mode is active.", "Pokazuje resize, gdy aktywny jest tryb zmiany rozmiaru.");
        A("Status", "Stan");
        A("Processor", "Procesor");
        A("Memory", "Pamięć");
        A("Battery", "Bateria");
        A("Disk space", "Miejsce na dysku");
        A("Date and time", "Data i godzina");
        A("Date and time format (.NET format, e.g. ddd d MMM HH:mm)", "Format daty i godziny (format .NET, np. ddd d MMM HH:mm)");
        A("invalid format", "nieprawidłowy format");
        A("Status program (optional, i3bar protocol or plain text lines; replaces the blocks above)",
            "Program stanu (opcjonalnie, protokół i3bar lub zwykłe linie tekstu; zastępuje bloki powyżej)");
        A("Colors", "Kolory");
        A("Background", "Tło");
        A("Text", "Tekst");
        A("Separator", "Separator");
        A("Border", "Obramowanie");
        A("Focused workspace", "Aktywny obszar");
        A("Visible on another monitor", "Widoczny na innym monitorze");
        A("Hidden workspace", "Ukryty obszar");
        A("Urgent workspace", "Pilny obszar");
        A("Binding mode", "Tryb");
        A("Windows taskbar", "Pasek zadań Windows");
        A("Automatically hide the Windows taskbar", "Automatycznie ukrywaj pasek zadań Windows");
        A("A Windows setting, applied immediately. Gives the tiled windows the whole screen.", "Ustawienie Windows, stosowane od razu. Daje kafelkom cały ekran.");

        // ---- Keys
        A("Each shortcut runs an i3 command. Double-click a shortcut to change it. A red mark means the same keys are used twice.",
            "Każdy skrót uruchamia polecenie i3. Kliknij dwukrotnie, aby go zmienić. Czerwony znak oznacza, że te same klawisze są użyte dwa razy.");
        A("Add shortcut", "Dodaj skrót");
        A("Mode", "Tryb");
        A("Search by key or command", "Szukaj po klawiszu lub poleceniu");
        A("These keys are used by another shortcut too", "Te klawisze są używane także przez inny skrót");
        A("Shortcut", "Skrót");
        A("Keys", "Klawisze");
        A("Command", "Polecenie");
        A("Click here and press the shortcut…", "Kliknij tutaj i naciśnij skrót…");
        A("Press the keys…", "Naciśnij klawisze…");
        A("Click the box and press the keys. Mozaic shortcuts are paused meanwhile. Win+L is reserved by Windows.",
            "Kliknij pole i naciśnij klawisze. Skróty Mozaic są w tym czasie wstrzymane. Win+L jest zarezerwowany przez Windows.");
        A("Run when the keys are released", "Uruchamiaj po puszczeniu klawiszy");
        A("Press the keys first.", "Najpierw naciśnij klawisze.");
        A("These keys are not valid.", "Te klawisze są nieprawidłowe.");
        A("Win+L is reserved by Windows and cannot be used.", "Win+L jest zarezerwowany przez Windows i nie można go użyć.");
        A("Enter a command.", "Wpisz polecenie.");
        A("The command is not valid: ", "Polecenie jest nieprawidłowe: ");
        A("{0} is already used by: {1}\n\nUse it anyway?", "{0} jest już używane przez: {1}\n\nUżyć mimo to?");

        // ---- Rules
        A("Decide what happens to particular applications: float them, open them on a workspace or leave them alone. Pick a running window to fill in the details.",
            "Zdecyduj, co się dzieje z konkretnymi aplikacjami: niech pływają, otwierają się na danym obszarze albo zostaną pominięte. Wybierz działające okno, aby wypełnić dane.");
        A("Add rule", "Dodaj regułę");
        A("When it opens", "Po otwarciu");
        A("Open on", "Otwieraj na");
        A("Do not manage", "Nie zarządzaj");
        A("Do not focus", "Nie aktywuj");
        A("workspace", "obszar");
        A("Window rule", "Reguła okna");
        A("Pick a window (optional)", "Wybierz okno (opcjonalnie)");
        A("(Mozaic is not running — type the details below)", "(Mozaic nie działa — wpisz dane poniżej)");
        A("Which windows", "Których okien dotyczy");
        A("Program", "Program");
        A("Window class", "Klasa okna");
        A("Title contains", "Tytuł zawiera");
        A("What to do", "Co zrobić");
        A("Float (do not tile)", "Pływające (nie kafelkuj)");
        A("Float and show on every workspace", "Pływające i widoczne na każdym obszarze");
        A("Always open on workspace", "Zawsze otwieraj na obszarze");
        A("Do not focus it when it opens", "Nie aktywuj po otwarciu");
        A("Do not manage it at all", "Nie zarządzaj nim wcale");
        A("Run commands", "Wykonaj polecenia");
        A("Choose at least one way to recognise the window.", "Wybierz co najmniej jeden sposób rozpoznania okna.");

        // ---- Workspaces
        A("Give workspaces names (for example 2:web; the number keeps the shortcut working) and pin them to a monitor.",
            "Nadaj obszarom nazwy (np. 2:web — numer zachowuje działanie skrótu) i przypnij je do monitora.");
        A("Name", "Nazwa");
        A("Monitor", "Monitor");
        A("Any monitor", "Dowolny monitor");
        A("Monitor names (DISPLAY1, DISPLAY2…) come from the running Mozaic. primary is the main monitor.",
            "Nazwy monitorów (DISPLAY1, DISPLAY2…) pochodzą z działającego Mozaic. primary to monitor główny.");

        // ---- Startup
        A("Start Mozaic with Windows and run programs when it starts.", "Uruchamiaj Mozaic z Windows i startuj programy razem z nim.");
        A("Start Mozaic with Windows", "Uruchamiaj Mozaic z Windows");
        A("Applied immediately (current user only).", "Stosowane od razu (tylko bieżący użytkownik).");
        A("Programs started with Mozaic", "Programy uruchamiane z Mozaic");
        A("Add program", "Dodaj program");
        A("A command line such as notepad.exe or wt.exe. Also on reload: run again every time the configuration is reloaded.",
            "Wiersz polecenia, np. notepad.exe albo wt.exe. „Także przy przeładowaniu”: uruchamiaj ponownie przy każdym przeładowaniu konfiguracji.");
        A("Also on reload", "Także przy przeładowaniu");

        // ---- Editor
        A("Open folder", "Otwórz folder");
        A("Restore defaults", "Przywróć domyślne");
        A("Double-click to go to the line", "Kliknij dwukrotnie, aby przejść do linii");
        A("Replace the whole configuration with the defaults? You can still discard the change before saving.",
            "Zastąpić całą konfigurację domyślną? Przed zapisaniem nadal możesz odrzucić tę zmianę.");

        // ---- Diagnostics
        A("See which windows Mozaic manages and why others are left alone. Run any i3 command.",
            "Zobacz, którymi oknami zarządza Mozaic i dlaczego inne są pomijane. Wykonaj dowolne polecenie i3.");
        A("Reload", "Przeładuj");
        A("Restart", "Uruchom ponownie");
        A("Open log", "Otwórz log");
        A("Run", "Wykonaj");
        A("Create a rule for this window", "Utwórz regułę dla tego okna");
        A("Open windows", "Otwarte okna");
        A("Title", "Tytuł");
        A("Reason", "Powód");
        A("Class", "Klasa");
        A("{0} managed windows", "zarządzane okna: {0}");
        A("tiled", "kafelek");
        A("waiting", "oczekuje");
        A("not managed", "pominięte");
        A("not a window", "to nie jest okno");
        A("Mozaic's own window", "okno Mozaic");
        A("child window", "okno podrzędne");
        A("system window", "okno systemowe");
        A("tool window", "okno narzędziowe");
        A("window that cannot be focused", "okno, którego nie można aktywować");
        A("popup without a frame", "wyskakujące okno bez ramki");
        A("invisible", "niewidoczne");
        A("cloaked (another virtual desktop or suspended)", "ukryte przez system (inny pulpit wirtualny lub uśpione)");
        A("no title yet", "jeszcze bez tytułu");
        A("too small", "za małe");
        A("excluded by --manage-only", "wykluczone przez --manage-only");
        A("matches an ignore rule", "pasuje do reguły ignore");
        A("runs as administrator (start Mozaic elevated to manage it)", "działa jako administrator (uruchom Mozaic jako administrator, aby nim zarządzać)");
        A("dialog", "okno dialogowe");
        A("not resizable", "bez zmiany rozmiaru");
        A("always on top", "zawsze na wierzchu");
    }
}
