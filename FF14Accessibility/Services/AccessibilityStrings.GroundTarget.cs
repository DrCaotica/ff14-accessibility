namespace FF14Accessibility.Services;

public static partial class AccessibilityStrings
{
    public static string GroundTargetUnnamed =>
        L("Ziel ohne Namen", "Unnamed target", "Цель без имени");

    public static string GroundTargetEntered(string action)
    {
        if (string.IsNullOrWhiteSpace(action))
            return L(
                "Bodenziel. Nummernblock 0 wirft auf das anvisierte Ziel. Num-Taste an. Escape bricht ab.",
                "Ground target. Numpad 0 places it on your target. Num Lock on. Escape cancels.",
                "Цель на земле. Ноль на цифровой клавиатуре ставит её на цель. Num Lock включён. Escape отменяет.");
        return L(
            $"Bodenziel. {action}. Nummernblock 0 wirft auf das anvisierte Ziel. Num-Taste an. Escape bricht ab.",
            $"Ground target. {action}. Numpad 0 places it on your target. Num Lock on. Escape cancels.",
            $"Цель на земле. {action}. Ноль на цифровой клавиатуре ставит её на цель. Num Lock включён. Escape отменяет.");
    }

    public static string GroundTargetNoTarget =>
        L("Kein Ziel.", "No target.", "Нет цели.");

    public static string GroundTargetOn(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) name = GroundTargetUnnamed;
        return L($"Ziel: {name}.", $"Target: {name}.", $"Цель: {name}.");
    }

    public static string GroundTargetCancelled =>
        L("Bodenziel abgebrochen.", "Ground target cancelled.", "Цель на земле отменена.");

    public static string GroundTargetUnreadable =>
        L("Der Bodenzielmodus ist gerade nicht lesbar.",
          "Ground targeting cannot be read right now.",
          "Режим цели на земле сейчас не читается.");
}
