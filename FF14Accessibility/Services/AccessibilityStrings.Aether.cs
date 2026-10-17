namespace FF14Accessibility.Services;

public static partial class AccessibilityStrings
{
    public static string AetherNoInformation =>
        L("Keine Informationen verfügbar.", "No information available.", "Нет сведений.");

    public static string AetherUnnamedZone =>
        L("Gebiet ohne Namen", "Unnamed area", "Область без названия");

    public static string AetherUnnamedQuest =>
        L("Auftrag ohne Namen", "Unnamed quest", "Задание без названия");

    public static string AetherCompiled =>
        L("Gebündelt. Fliegen ist frei.", "Compiled. Flying is unlocked.", "Собрано. Полёт открыт.");

    public static string AetherHiddenCard(int page, int pages, int entry, int entries, string status) =>
        L($"Seite {page} von {pages}. Eintrag {entry} von {entries}. {status}",
          $"Page {page} of {pages}. Entry {entry} of {entries}. {status}",
          $"Страница {page} из {pages}. Запись {entry} из {entries}. {status}");

    public static string AetherZoneCard(int page, int pages, string zone, int entry, int entries, int found, int total, bool compiled)
    {
        var line = L(
            $"Seite {page} von {pages}. {zone}, Eintrag {entry} von {entries}. {found} von {total}.",
            $"Page {page} of {pages}. {zone}, entry {entry} of {entries}. {found} of {total}.",
            $"Страница {page} из {pages}. {zone}, запись {entry} из {entries}. {found} из {total}.");
        return compiled ? $"{line} {AetherCompiled}" : line;
    }

    public static string AetherZoneNoCount(int page, int pages, string zone, int entry, int entries, bool compiled)
    {
        var line = L(
            $"Seite {page} von {pages}. {zone}, Eintrag {entry} von {entries}. {AetherProgressUnread}",
            $"Page {page} of {pages}. {zone}, entry {entry} of {entries}. {AetherProgressUnread}",
            $"Страница {page} из {pages}. {zone}, запись {entry} из {entries}. {AetherProgressUnread}");
        return compiled ? $"{line} {AetherCompiled}" : line;
    }

    public static string AetherPage(int page, int pages) =>
        L($"Seite {page} von {pages}.", $"Page {page} of {pages}.", $"Страница {page} из {pages}.");

    public static string AetherFirstPage =>
        L("Das ist die erste Seite.", "This is the first page.", "Это первая страница.");

    public static string AetherLastPage =>
        L("Das ist die letzte Seite.", "This is the last page.", "Это последняя страница.");

    public static string AetherPageUnchanged =>
        L("Die Seite hat nicht gewechselt.", "The page did not change.", "Страница не сменилась.");

    public static string AetherPageHint(string readKey) =>
        L($"Nummernblock Plus und Minus wechseln die Seite. {readKey} sagt, was noch fehlt. Die Kategorie Windäther und Nummernblock 3 laufen die fehlenden Quellen an.",
          $"Numpad plus and minus change the page. {readKey} says what is still missing. The Aether currents category and Numpad 3 walk the missing currents.",
          $"Плюс и минус на цифровой клавиатуре меняют страницу. {readKey} говорит, чего ещё нет. Категория эфирных течений и Numpad 3 ведут к недостающим течениям.");

    public static string NoAetherWalks =>
        L("Keine anlaufbare Windätherquelle. Im jetzigen Gebiet ist keine Feldquelle übrig. Andere Gebiete nennt nur das Fenster, oder eines, in dem du schon eine Quelle hast.",
          "No aether current to walk to. This area has no field current left. Other areas are only those the window names, or where you already have a current.",
          "Нет эфирного течения, к которому можно идти. В этой зоне не осталось полевого течения. Другие зоны называет только окно, или там, где течение уже есть.");

    public static string AetherWalkField =>
        L("Quelle", "Current", "Течение");

    public static string AetherWalkQuest(string name) =>
        L($"Auftrag {name}", $"Quest {name}", $"Задание {name}");

    public static string CategoryAetherCount(int here, int away) =>
        away > 0
            ? L($"Windäther. {here} hier, {away} in anderen Gebieten.",
                $"Aether currents. {here} here, {away} in other areas.",
                $"Эфирные течения. {here} здесь, {away} в других областях.")
            : L($"Windäther. {here} hier.",
                $"Aether currents. {here} here.",
                $"Эфирные течения. {here} здесь.");

    public static string AetherProgressUnread =>
        L("Der Fortschritt der Quellen ist nicht lesbar.",
          "The current progress cannot be read.",
          "Прогресс эфирных течений прочитать нельзя.");

    public static string AetherMissingHeader =>
        L("Es fehlt noch:", "Still missing:", "Ещё нет:");

    public static string AetherNoneMissing =>
        L("Hier fehlt nichts.", "Nothing is missing here.", "Здесь ничего не отсутствует.");

    public static string AetherQuestSpot(int number, string name) =>
        L($"Auftrag {number}, {name}.", $"Quest {number}, {name}.", $"Задание {number}, {name}.");

    public static string AetherFieldSpot(int number) =>
        L($"Quelle {number}.", $"Current {number}.", $"Течение {number}.");

    public static string AetherMap(float x, float y) =>
        L($"Kartenkoordinaten {x:0.0}, {y:0.0}.",
          $"Map coordinates {x:0.0}, {y:0.0}.",
          $"Координаты карты {x:0.0}, {y:0.0}.");

    public static string AetherHere(float metres, string compass, float x, float y) =>
        L($"{MetersRemaining(metres)}, {compass}. Kartenkoordinaten {x:0.0}, {y:0.0}.",
          $"{MetersRemaining(metres)}, {compass}. Map coordinates {x:0.0}, {y:0.0}.",
          $"{MetersRemaining(metres)}, {compass}. Координаты карты {x:0.0}, {y:0.0}.");

    public static string AetherHereNoMap(float metres, string compass) =>
        L($"{MetersRemaining(metres)}, {compass}. {AetherPlaceUnknown}",
          $"{MetersRemaining(metres)}, {compass}. {AetherPlaceUnknown}",
          $"{MetersRemaining(metres)}, {compass}. {AetherPlaceUnknown}");

    public static string AetherPlaceUnknown =>
        L("Ort unbekannt.", "Place unknown.", "Место неизвестно.");
}
