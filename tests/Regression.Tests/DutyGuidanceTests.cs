using System.Numerics;
using FF14Accessibility;
using FF14Accessibility.Services;

namespace Regression.Tests;

[Collection("Language")]
public sealed class DutyGuidanceTests : IDisposable
{
    private readonly LanguageMode _previous = Loc.Mode;

    public DutyGuidanceTests() => Loc.Mode = LanguageMode.German;

    public void Dispose() => Loc.Mode = _previous;

    [Fact]
    public void Instruction_skips_duplicate_objective_and_ignores_counter()
    {
        var tasks = new List<DirectorTasks>
        {
            new("Spuren", "Folge den Spuren", new List<DirectorTodoLine>
            {
                new("Folge den Spuren", ", 0 von 1", false),
            }),
        };

        var lines = DutyGuidancePlanner.InstructionLines(tasks);

        var line = Assert.Single(lines);
        Assert.Equal("Folge den Spuren", line);
    }

    [Fact]
    public void Added_lines_report_only_new_text()
    {
        var previous = new List<string> { "Folge den Spuren" };
        var next = new List<string> { "Folge den Spuren", "Folge dem Ketzer" };

        var added = DutyGuidancePlanner.AddedLines(previous, next);

        Assert.Equal("Folge dem Ketzer", Assert.Single(added));
    }

    [Fact]
    public void Removed_lines_are_not_spoken()
    {
        var previous = new List<string> { "Folge den Spuren", "Folge dem Ketzer" };
        var next = new List<string> { "Folge dem Ketzer" };

        Assert.Empty(DutyGuidancePlanner.AddedLines(previous, next));
    }

    [Fact]
    public void Tracker_echo_drops_the_repeated_sentence()
    {
        var added = new List<string> { "Folge dem Ketzer" };
        var tracker = new List<string> { "Folge dem Ketzer, nach Norden" };

        Assert.Empty(DutyGuidancePlanner.WithoutTrackerEcho(added, tracker));
    }

    [Fact]
    public void Pin_creep_under_the_gap_stays_quiet()
    {
        var previous = new List<DutyGuidancePlanner.StoredPin> { new("Spuren", 0f, 0f) };
        var current = new List<DutyGuidancePlanner.StoredPin> { new("Spuren", 24f, 0f) };

        Assert.False(DutyGuidancePlanner.PinsChanged(previous, current, DutyGuidancePlanner.PinMoveMetres));
    }

    [Fact]
    public void Pin_jump_of_the_gap_is_a_new_place()
    {
        var previous = new List<DutyGuidancePlanner.StoredPin> { new("Spuren", 0f, 0f) };
        var current = new List<DutyGuidancePlanner.StoredPin> { new("Spuren", 25f, 0f) };

        Assert.True(DutyGuidancePlanner.PinsChanged(previous, current, DutyGuidancePlanner.PinMoveMetres));
    }

    [Fact]
    public void Appearing_and_vanishing_pins_count_as_a_change()
    {
        var none = new List<DutyGuidancePlanner.StoredPin>();
        var one = new List<DutyGuidancePlanner.StoredPin> { new("Spuren", 0f, 0f) };

        Assert.True(DutyGuidancePlanner.PinsChanged(none, one, DutyGuidancePlanner.PinMoveMetres));
        Assert.True(DutyGuidancePlanner.PinsChanged(one, none, DutyGuidancePlanner.PinMoveMetres));
        Assert.False(DutyGuidancePlanner.PinsChanged(none, none, DutyGuidancePlanner.PinMoveMetres));
    }

    [Fact]
    public void A_second_pin_next_to_the_old_one_is_still_new()
    {
        var previous = new List<DutyGuidancePlanner.StoredPin> { new("Spuren", 0f, 0f) };
        var current = new List<DutyGuidancePlanner.StoredPin>
        {
            new("Spuren", 0f, 0f),
            new("Spuren", 5f, 0f),
        };

        Assert.True(DutyGuidancePlanner.PinsChanged(previous, current, DutyGuidancePlanner.PinMoveMetres));
    }

    [Fact]
    public void Different_label_at_the_same_spot_is_a_change()
    {
        var previous = new List<DutyGuidancePlanner.StoredPin> { new("Spuren", 0f, 0f) };
        var current = new List<DutyGuidancePlanner.StoredPin> { new("Andere", 0f, 0f) };

        Assert.True(DutyGuidancePlanner.PinsChanged(previous, current, DutyGuidancePlanner.PinMoveMetres));
    }

    [Fact]
    public void Tracker_name_wins_over_a_closer_other_quest()
    {
        var pins = new List<DutyPinPoint>
        {
            new("Nebenquest", 10f, 0f, 0f, 0f),
            new("Spuren im Schnee", 80f, 0f, 0f, 0f),
        };

        var relevant = DutyGuidancePlanner.RelevantPins(
            pins, new List<string> { "Spuren im Schnee" }, new List<string> { "Nebenquest" });

        var speech = DutyGuidancePlanner.PinSentence(relevant, Vector3.Zero);

        Assert.Equal("Kartenpunkt, Spuren im Schnee, 80 Meter, östlich.", speech);
    }

    [Fact]
    public void New_instruction_adds_the_point_pin()
    {
        var pins = new List<DutyPinPoint> { new("Spuren im Schnee", 30f, 0f, 0f, 0f) };

        var speech = DutyGuidancePlanner.Compose(
            new List<string> { "Folge dem Ketzer" },
            includePin: true,
            pins,
            Vector3.Zero,
            hasPlayer: true);

        Assert.Equal(
            "Neue Aufgabe. Folge dem Ketzer. Kartenpunkt, Spuren im Schnee, 30 Meter, östlich.",
            speech);
    }

    [Fact]
    public void Inside_a_circle_names_the_goal_area()
    {
        var pins = new List<DutyPinPoint> { new("Spuren im Schnee", 10f, 0f, 0f, 30f) };

        var speech = DutyGuidancePlanner.PinSentence(pins, Vector3.Zero);

        Assert.Equal("Kartenpunkt, Spuren im Schnee, im Zielkreis.", speech);
    }

    [Fact]
    public void Outside_a_circle_measures_to_the_edge()
    {
        var pins = new List<DutyPinPoint> { new("Spuren im Schnee", 40f, 0f, 0f, 10f) };

        var speech = DutyGuidancePlanner.PinSentence(pins, Vector3.Zero);

        Assert.Equal(
            "Kartenpunkt, Spuren im Schnee, noch 30 Meter bis zum Zielkreis, östlich.",
            speech);
    }

    [Fact]
    public void Echoed_text_can_still_carry_the_pin()
    {
        var pins = new List<DutyPinPoint> { new("Spuren im Schnee", 30f, 0f, 0f, 0f) };

        var speech = DutyGuidancePlanner.Compose(
            Array.Empty<string>(),
            includePin: true,
            pins,
            Vector3.Zero,
            hasPlayer: true);

        Assert.Equal("Kartenpunkt, Spuren im Schnee, 30 Meter, östlich.", speech);
    }
}
