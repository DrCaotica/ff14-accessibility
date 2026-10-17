using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace FF14Accessibility.Services;

/// <summary>
/// Speaks a new duty or quest instruction, and where the current map pin sits.
/// The first sample after login is silent. Counters are ignored. The quest
/// tracker already speaks its own text, so a line it already shows is not
/// repeated. The pin is still spoken, because the tracker has no direction.
///
/// Minimap icons are only written to the log when the instruction text
/// changes. Their positions are map pixels and have not been checked on a
/// footprint duty, so they are not spoken.
/// </summary>
public sealed class DutyGuidanceService
{
    private const int PollMs = 500;
    private const int ProbeLines = 8;

    private readonly DirectorTodoService _todos;
    private readonly QuestMarkerService _markers;
    private readonly IObjectTable _objects;
    private readonly IClientState _client;
    private readonly TolkService _tolk;
    private readonly IPluginLog _log;
    private readonly Stopwatch _since = Stopwatch.StartNew();

    private bool _primed;
    private List<string> _lines = new();
    private List<DutyGuidancePlanner.StoredPin> _pins = new();

    /// <summary>Same switch as the quest-tracker announcements. Null counts as on.</summary>
    public Func<bool>? IsEnabled { get; set; }

    /// <summary>While true, a change is held and spoken on the next clear poll.</summary>
    public Func<bool>? Suppress { get; set; }

    public DutyGuidanceService(
        DirectorTodoService todos,
        QuestMarkerService markers,
        IObjectTable objects,
        IClientState client,
        TolkService tolk,
        IPluginLog log)
    {
        _todos = todos;
        _markers = markers;
        _objects = objects;
        _client = client;
        _tolk = tolk;
        _log = log;
    }

    /// <summary>Poll. Safe to call every frame. Work runs twice a second.</summary>
    public void Update()
    {
        if (!_client.IsLoggedIn)
        {
            Reset();
            return;
        }
        if (_since.ElapsedMilliseconds < PollMs) return;
        _since.Restart();

        var tasks = _todos.GetActiveTasks();
        var tracker = _markers.GetQuestObjectives(log: false);
        var sample = Capture(tasks, tracker);

        if (!_primed)
        {
            Store(sample);
            _primed = true;
            _log.Info($"[Aufgabe] Basis: Zeilen={sample.Lines.Count}, Kartenpunkte={sample.Pins.Count}.");
            return;
        }

        if (IsEnabled != null && !IsEnabled())
        {
            Store(sample);
            return;
        }
        if (Suppress?.Invoke() == true) return;
        // Zone loads hide the player. Keep the old sample and try again,
        // so the new pin is not marked as already said.
        if (_objects.LocalPlayer == null) return;

        var added = DutyGuidancePlanner.AddedLines(_lines, sample.Lines);
        var pinsChanged = DutyGuidancePlanner.PinsChanged(
            _pins, DutyGuidancePlanner.Store(sample.Pins), DutyGuidancePlanner.PinMoveMetres);
        if (added.Count == 0 && !pinsChanged) return;

        var trackerValues = new List<string>(tracker.Count);
        foreach (var value in tracker.Values)
            trackerValues.Add(value);
        var spoken = DutyGuidancePlanner.WithoutTrackerEcho(added, trackerValues);
        var player = _objects.LocalPlayer;
        var speech = DutyGuidancePlanner.Compose(
            spoken,
            includePin: added.Count > 0 || pinsChanged,
            sample.Pins,
            player?.Position ?? default,
            hasPlayer: player != null);

        Store(sample);
        if (speech.Length > 0)
        {
            _log.Info($"[Aufgabe] {speech}");
            _tolk.Speak(speech);
        }
        else
        {
            _log.Info("[Aufgabe] Instruction changed, no new speech.");
        }
        if (added.Count > 0)
            LogMarkerProbe();
    }

    /// <summary>Pin sentence for the manual task-list key. Empty when no pin fits.</summary>
    public string ClosestPinSpeech(IReadOnlyList<DirectorTasks> tasks)
    {
        var player = _objects.LocalPlayer;
        if (player == null) return string.Empty;
        var tracker = _markers.GetQuestObjectives(log: false);
        var sample = Capture(tasks, tracker);
        return DutyGuidancePlanner.PinSentence(sample.Pins, player.Position);
    }

    private readonly record struct GuidanceSample(List<string> Lines, List<DutyPinPoint> Pins);

    private GuidanceSample Capture(IReadOnlyList<DirectorTasks> tasks, Dictionary<string, string> tracker)
    {
        var names = new List<string>(tracker.Count);
        foreach (var name in tracker.Keys)
            names.Add(name);
        var titles = new List<string>(tasks.Count);
        foreach (var task in tasks)
        {
            if (task.Title.Length > 0) titles.Add(task.Title);
        }
        var pins = DutyGuidancePlanner.RelevantPins(_markers.GetInZonePins(), names, titles);
        return new GuidanceSample(DutyGuidancePlanner.InstructionLines(tasks), pins);
    }

    private void Store(GuidanceSample sample)
    {
        _lines = sample.Lines;
        _pins = DutyGuidancePlanner.Store(sample.Pins);
    }

    private void Reset()
    {
        if (!_primed && _lines.Count == 0 && _pins.Count == 0) return;
        _primed = false;
        _lines = new List<string>();
        _pins = new List<DutyGuidancePlanner.StoredPin>();
    }

    /// <summary>
    /// One log when the instruction text changes. Records quest pins,
    /// event markers and minimap slots. Minimap X/Y stay pixels.
    /// </summary>
    private unsafe void LogMarkerProbe()
    {
        var map = Map.Instance();
        var territory = _client.TerritoryType;
        var questParts = new List<string>();
        if (map != null)
        {
            foreach (ref var marker in map->QuestMarkers)
            {
                if (questParts.Count >= ProbeLines) break;
                var label = marker.Label.ToString();
                if (string.IsNullOrWhiteSpace(label)) continue;
                var count = marker.MarkerData.Count;
                if (count is < 0 or > 100) continue;
                for (var i = 0; i < count && questParts.Count < ProbeLines; i++)
                {
                    var data = marker.MarkerData[i];
                    if (data.TerritoryTypeId != territory) continue;
                    var tip = data.TooltipString != null ? data.TooltipString->ToString() : string.Empty;
                    questParts.Add(
                        $"'{label}' render={marker.ShouldRender} icon={data.IconId} " +
                        $"pos=({data.Position.X:F0}|{data.Position.Y:F0}|{data.Position.Z:F0}) " +
                        $"r={data.Radius:F0} tt='{tip}'");
                }
            }
        }

        var agent = AgentMap.Instance();
        if (agent == null)
        {
            _log.Info("[Aufgabe] Sonde: AgentMap null. Quest: " + JoinProbe(questParts));
            return;
        }

        var events = agent->EventMarkers.LongCount;
        var mini = agent->MiniMapMarkers;
        var filled = 0;
        var miniParts = new List<string>();
        for (var i = 0; i < mini.Length; i++)
        {
            var slot = mini[i];
            if (slot.MapMarker.IconId == 0 && slot.DataType == 0) continue;
            filled++;
            if (miniParts.Count >= ProbeLines) continue;
            var sub = slot.MapMarker.Subtext.ToString();
            miniParts.Add(
                $"#{i} type={slot.DataType} key={slot.DataKey} icon={slot.MapMarker.IconId} " +
                $"X={slot.MapMarker.X} Y={slot.MapMarker.Y} sub='{sub}'");
        }

        _log.Info(
            $"[Aufgabe] Sonde terr={territory} EventMarkers={events} " +
            $"MiniMapCount={agent->MiniMapMarkerCount} miniFilled={filled} " +
            $"mini={JoinProbe(miniParts)} quest={JoinProbe(questParts)}");
    }

    private static string JoinProbe(List<string> parts)
        => parts.Count == 0 ? "keine" : string.Join(" | ", parts);
}
