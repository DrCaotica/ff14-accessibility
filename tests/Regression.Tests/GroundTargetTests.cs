using FF14Accessibility;
using FF14Accessibility.Services;

namespace Regression.Tests;

public sealed class GroundTargetTests : IDisposable
{
    private readonly LanguageMode _previous = Loc.Mode;

    public GroundTargetTests() => Loc.Mode = LanguageMode.German;

    public void Dispose() => Loc.Mode = _previous;

    [Fact]
    public void Entering_says_the_key_once()
    {
        var session = new GroundTargetSession();
        var entered = session.Advance(5, false, 0, "Asyl", "");
        Assert.Equal(GroundTargetNotice.Entered, entered.Notice);
        Assert.Equal(
            "Bodenziel. Asyl. Nummernblock 0 wirft auf das anvisierte Ziel. Num-Taste an. Escape bricht ab.",
            entered.Speech);
        Assert.False(entered.Consume);

        var again = session.Advance(5, false, 0, "Asyl", "");
        Assert.Equal(GroundTargetNotice.None, again.Notice);
        Assert.Null(again.Speech);
    }

    [Fact]
    public void Empty_action_name_still_names_the_key()
    {
        var entered = new GroundTargetSession().Advance(3, false, 0, "", "");
        Assert.Equal(
            "Bodenziel. Nummernblock 0 wirft auf das anvisierte Ziel. Num-Taste an. Escape bricht ab.",
            entered.Speech);
    }

    [Fact]
    public void Confirm_without_the_mode_does_not_take_the_key()
    {
        var decision = new GroundTargetSession().Advance(0, true, 99, "", "Goblin");
        Assert.Equal(GroundTargetNotice.None, decision.Notice);
        Assert.False(decision.Consume);
        Assert.Equal(0ul, decision.WriteObjectId);
    }

    [Fact]
    public void Missing_target_is_spoken_and_not_written()
    {
        var session = new GroundTargetSession();
        session.Advance(5, false, 0, "Asyl", "");
        var missing = session.Advance(5, true, 0, "Asyl", "Goblin");
        Assert.Equal(GroundTargetNotice.NoTarget, missing.Notice);
        Assert.Equal("Kein Ziel.", missing.Speech);
        Assert.True(missing.Consume);
        Assert.Equal(0ul, missing.WriteObjectId);

        var sentinel = session.Advance(5, true, GroundTargetSession.NoObject, "Asyl", "Goblin");
        Assert.Equal(GroundTargetNotice.NoTarget, sentinel.Notice);
        Assert.Equal(0ul, sentinel.WriteObjectId);
    }

    [Fact]
    public void Confirm_writes_the_hard_target_and_leaving_is_not_a_cancel()
    {
        var session = new GroundTargetSession();
        session.Advance(5, false, 0, "Asyl", "");
        var aimed = session.Advance(5, true, 0x1000ABC, "Asyl", "Goblin");
        Assert.Equal(GroundTargetNotice.Aimed, aimed.Notice);
        Assert.Equal("Ziel: Goblin.", aimed.Speech);
        Assert.Equal(0x1000ABCul, aimed.WriteObjectId);
        Assert.True(aimed.Consume);

        var left = session.Advance(0, false, 0, "", "");
        Assert.Equal(GroundTargetNotice.None, left.Notice);
        Assert.Null(left.Speech);
    }

    [Fact]
    public void Leaving_without_a_throw_says_cancelled()
    {
        var session = new GroundTargetSession();
        session.Advance(5, false, 0, "Asyl", "");
        var left = session.Advance(0, false, 0, "", "");
        Assert.Equal(GroundTargetNotice.Cancelled, left.Notice);
        Assert.Equal("Bodenziel abgebrochen.", left.Speech);
    }

    [Fact]
    public void Same_frame_confirm_aims_and_does_not_cancel_afterwards()
    {
        var session = new GroundTargetSession();
        var aimed = session.Advance(5, true, 42, "Asyl", "Goblin");
        Assert.Equal(GroundTargetNotice.Aimed, aimed.Notice);
        Assert.Equal(42ul, aimed.WriteObjectId);
        var left = session.Advance(0, false, 0, "", "");
        Assert.Equal(GroundTargetNotice.None, left.Notice);
    }

    [Fact]
    public void A_second_press_can_choose_another_target()
    {
        var session = new GroundTargetSession();
        session.Advance(7, true, 1, "Asyl", "A");
        var again = session.Advance(7, true, 2, "Asyl", "B");
        Assert.Equal("Ziel: B.", again.Speech);
        Assert.Equal(2ul, again.WriteObjectId);
    }

    [Fact]
    public void Still_active_after_the_wait_is_logged_once_without_speech()
    {
        var session = new GroundTargetSession();
        session.Advance(5, true, 99, "Asyl", "Goblin");
        GroundTargetDecision last = default;
        for (var i = 0; i < GroundTargetSession.ConfirmFrames; i++)
            last = session.Advance(5, false, 0, "Asyl", "");
        Assert.Equal(GroundTargetNotice.StillThere, last.Notice);
        Assert.Null(last.Speech);

        var repeat = session.Advance(5, false, 0, "Asyl", "");
        Assert.Equal(GroundTargetNotice.None, repeat.Notice);
        var left = session.Advance(0, false, 0, "", "");
        Assert.Equal(GroundTargetNotice.None, left.Notice);
    }

    [Fact]
    public void Blank_target_name_uses_the_unnamed_line()
    {
        Assert.Equal("Ziel: Ziel ohne Namen.", AccessibilityStrings.GroundTargetOn("  "));
    }
}
