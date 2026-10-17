namespace FF14Accessibility.Services;

internal enum GroundTargetNotice
{
    None,
    Entered,
    NoTarget,
    Aimed,
    Cancelled,
    StillThere,
}

internal readonly struct GroundTargetDecision
{
    public GroundTargetNotice Notice { get; init; }
    public string? Speech { get; init; }
    public bool Consume { get; init; }
    public ulong WriteObjectId { get; init; }
}

/// <summary>
/// Speech and confirm rules for the game's area-target mode.
/// The game write itself stays in <see cref="GroundTargetService"/>.
/// </summary>
internal sealed class GroundTargetSession
{
    internal const int ConfirmFrames = 30;

    /// <summary>Same unused-target id as <c>ActionManager.UseAction</c>.</summary>
    internal const ulong NoObject = 0xE0000000;

    private bool _inMode;
    private bool _armed;
    private bool _aimedThisMode;
    private bool _stuckReported;
    private int _left;

    /// <summary>The object field could not be written. Leaving the mode may still say it was cancelled.</summary>
    internal void NoteWriteFailed()
    {
        _armed = false;
        _aimedThisMode = false;
        _left = 0;
    }

    internal GroundTargetDecision Advance(uint areaActionId, bool confirmPressed, ulong targetId, string actionName, string targetName)
    {
        var inMode = areaActionId != 0;
        if (!inMode)
        {
            var cancelled = _inMode && !_aimedThisMode;
            Clear();
            return cancelled
                ? new GroundTargetDecision
                {
                    Notice = GroundTargetNotice.Cancelled,
                    Speech = AccessibilityStrings.GroundTargetCancelled,
                }
                : default;
        }

        string? speech = null;
        var notice = GroundTargetNotice.None;
        if (!_inMode)
        {
            _inMode = true;
            _armed = false;
            _aimedThisMode = false;
            _stuckReported = false;
            _left = 0;
            notice = GroundTargetNotice.Entered;
            speech = AccessibilityStrings.GroundTargetEntered(actionName);
        }

        if (confirmPressed)
        {
            if (targetId == 0 || targetId == NoObject)
            {
                var missing = AccessibilityStrings.GroundTargetNoTarget;
                return new GroundTargetDecision
                {
                    Notice = GroundTargetNotice.NoTarget,
                    Speech = speech == null ? missing : $"{speech} {missing}",
                    Consume = true,
                };
            }

            _armed = true;
            _aimedThisMode = true;
            _stuckReported = false;
            _left = ConfirmFrames;
            return new GroundTargetDecision
            {
                Notice = GroundTargetNotice.Aimed,
                Speech = AccessibilityStrings.GroundTargetOn(targetName),
                Consume = true,
                WriteObjectId = targetId,
            };
        }

        if (_armed && !_stuckReported)
        {
            if (_left > 0) _left--;
            if (_left == 0)
            {
                _armed = false;
                _stuckReported = true;
                notice = GroundTargetNotice.StillThere;
            }
        }

        return new GroundTargetDecision { Notice = notice, Speech = speech };
    }

    private void Clear()
    {
        _inMode = false;
        _armed = false;
        _aimedThisMode = false;
        _stuckReported = false;
        _left = 0;
    }
}
