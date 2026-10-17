using System;
using System.Collections.Generic;
using System.Numerics;

namespace FF14Accessibility.Services;

/// <summary>
/// One accepted-quest map pin in the current territory. World coordinates,
/// the same positions Quest goals already walks to. This is not a minimap
/// icon: those live in map pixels and are not spoken from here.
/// </summary>
public readonly record struct DutyPinPoint(string Label, float X, float Y, float Z, float Radius);

/// <summary>
/// Decides when a duty or quest step is worth saying, and which map pin to
/// name. No game memory. The watcher stores the result and speaks only after
/// a silent first sample.
/// </summary>
public static class DutyGuidancePlanner
{
    /// <summary>
    /// A pin that only creeps or jitters stays quiet. A new spot at least this
    /// far from the last stored one is a new place. Speech gap, not a game radius.
    /// </summary>
    public const float PinMoveMetres = 25f;

    /// <summary>One stored pin, horizontal only. Height is not part of the match.</summary>
    public readonly record struct StoredPin(string Label, float X, float Z);

    /// <summary>
    /// Instruction sentences only. Progress counters stay out, so a kill does
    /// not look like a new order. The objective is dropped when a line already
    /// says the same words.
    /// </summary>
    public static List<string> InstructionLines(IReadOnlyList<DirectorTasks> tasks)
    {
        var lines = new List<string>();
        foreach (var task in tasks)
        {
            if (task.Objective.Length > 0 && !ContainsText(task.Lines, task.Objective))
                lines.Add(task.Objective);
            foreach (var line in task.Lines)
            {
                if (line.Text.Length > 0)
                    lines.Add(line.Text);
            }
        }
        return lines;
    }

    /// <summary>Lines present in <paramref name="next"/> and not still covered by <paramref name="previous"/>.</summary>
    public static List<string> AddedLines(IReadOnlyList<string> previous, IReadOnlyList<string> next)
    {
        var left = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in previous)
            left[line] = left.GetValueOrDefault(line) + 1;

        var added = new List<string>();
        foreach (var line in next)
        {
            if (left.TryGetValue(line, out var count) && count > 0)
                left[line] = count - 1;
            else
                added.Add(line);
        }
        return added;
    }

    /// <summary>
    /// Drops a director line the on-screen quest tracker already shows, so the
    /// same sentence is not spoken twice. Match is the whole line inside the
    /// tracker text.
    /// </summary>
    public static List<string> WithoutTrackerEcho(IReadOnlyList<string> lines, IReadOnlyList<string> trackerValues)
    {
        var result = new List<string>();
        foreach (var line in lines)
        {
            var echoed = false;
            foreach (var value in trackerValues)
            {
                if (value.Contains(line, StringComparison.Ordinal))
                {
                    echoed = true;
                    break;
                }
            }
            if (!echoed)
                result.Add(line);
        }
        return result;
    }

    /// <summary>
    /// Pins to follow. Tracker names win, then the duty title, then every
    /// in-zone pin. An empty match falls through instead of hiding the pin.
    /// </summary>
    public static List<DutyPinPoint> RelevantPins(
        IReadOnlyList<DutyPinPoint> pins,
        IReadOnlyList<string> trackerNames,
        IReadOnlyList<string> titles)
    {
        var tracked = Matching(pins, trackerNames);
        if (tracked.Count > 0) return tracked;
        var titled = Matching(pins, titles);
        if (titled.Count > 0) return titled;
        var all = new List<DutyPinPoint>(pins.Count);
        all.AddRange(pins);
        return all;
    }

    /// <summary>Horizontal snapshot used to notice a real move.</summary>
    public static List<StoredPin> Store(IReadOnlyList<DutyPinPoint> pins)
    {
        var stored = new List<StoredPin>(pins.Count);
        foreach (var pin in pins)
            stored.Add(new StoredPin(pin.Label, pin.X, pin.Z));
        return stored;
    }

    /// <summary>
    /// True when a pin appeared, vanished, changed quest, or moved at least
    /// <paramref name="moveMetres"/> from its previous spot. Two pins of the
    /// same quest match nearest-first, so a new neighbour is not swallowed by
    /// the old one.
    /// </summary>
    public static bool PinsChanged(
        IReadOnlyList<StoredPin> previous, IReadOnlyList<StoredPin> current, float moveMetres)
    {
        var used = new bool[previous.Count];
        var limit = moveMetres * moveMetres;
        foreach (var pin in current)
        {
            var best = -1;
            var bestDistance = float.MaxValue;
            for (var i = 0; i < previous.Count; i++)
            {
                if (used[i] || !string.Equals(previous[i].Label, pin.Label, StringComparison.Ordinal))
                    continue;
                var dx = previous[i].X - pin.X;
                var dz = previous[i].Z - pin.Z;
                var distance = dx * dx + dz * dz;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }
            if (best < 0 || bestDistance >= limit)
                return true;
            used[best] = true;
        }
        for (var i = 0; i < used.Length; i++)
        {
            if (!used[i]) return true;
        }
        return false;
    }

    /// <summary>Spoken pin for the closest entry. Empty when there is none.</summary>
    public static string PinSentence(IReadOnlyList<DutyPinPoint> pins, Vector3 player)
    {
        if (!TryClosest(pins, player, out var pin, out var distance))
            return string.Empty;

        var compass = RouteService.CompassAdjective(player, new Vector3(pin.X, pin.Y, pin.Z));
        // Under a metre the circle is a point. Larger circles use the edge.
        if (pin.Radius >= 1f && distance <= pin.Radius)
            return AccessibilityStrings.QuestMapPinInside(pin.Label);
        if (pin.Radius >= 1f)
        {
            var edge = MathF.Max(0f, distance - pin.Radius);
            return AccessibilityStrings.QuestMapPinEdge(
                pin.Label, AccessibilityStrings.FormatDistance(edge), compass);
        }
        return AccessibilityStrings.QuestMapPin(
            pin.Label, AccessibilityStrings.FormatDistance(distance), compass);
    }

    /// <summary>One announcement: new sentences, then the pin when asked.</summary>
    public static string Compose(
        IReadOnlyList<string> spokenLines,
        bool includePin,
        IReadOnlyList<DutyPinPoint> pins,
        Vector3 player,
        bool hasPlayer)
    {
        var parts = new List<string>();
        if (spokenLines.Count > 0)
        {
            var body = string.Join(". ", spokenLines);
            if (!body.EndsWith('.')) body += ".";
            parts.Add(AccessibilityStrings.DutyInstructionChanged(body));
        }
        if (includePin && hasPlayer)
        {
            var pin = PinSentence(pins, player);
            if (pin.Length > 0) parts.Add(pin);
        }
        return string.Join(" ", parts);
    }

    private static bool TryClosest(
        IReadOnlyList<DutyPinPoint> pins, Vector3 player, out DutyPinPoint pin, out float distance)
    {
        pin = default;
        distance = 0f;
        if (pins.Count == 0) return false;

        var best = 0;
        var bestDistance = float.MaxValue;
        for (var i = 0; i < pins.Count; i++)
        {
            var dx = pins[i].X - player.X;
            var dz = pins[i].Z - player.Z;
            var horizontal = MathF.Sqrt(dx * dx + dz * dz);
            if (horizontal < bestDistance)
            {
                bestDistance = horizontal;
                best = i;
            }
        }
        pin = pins[best];
        distance = bestDistance;
        return true;
    }

    private static List<DutyPinPoint> Matching(IReadOnlyList<DutyPinPoint> pins, IReadOnlyList<string> names)
    {
        var matched = new List<DutyPinPoint>();
        foreach (var pin in pins)
        {
            foreach (var name in names)
            {
                if (name.Length > 0 && string.Equals(name, pin.Label, StringComparison.Ordinal))
                {
                    matched.Add(pin);
                    break;
                }
            }
        }
        return matched;
    }

    private static bool ContainsText(IReadOnlyList<DirectorTodoLine> lines, string text)
    {
        foreach (var line in lines)
        {
            if (line.Text == text) return true;
        }
        return false;
    }
}
