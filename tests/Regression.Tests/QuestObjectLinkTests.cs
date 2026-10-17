using FF14Accessibility.Services;

namespace Regression.Tests;

public sealed class QuestObjectLinkTests
{
    [Fact]
    public void Quest_argument_keeps_an_eobject_and_ignores_an_actor()
    {
        var footprint = QuestObjectLinks.FromQuestArgument(67124, "EOBJECT0", 2005408);
        Assert.NotNull(footprint);
        Assert.Equal(2005408u, footprint.Value.BaseId);
        Assert.False(footprint.Value.LimitToTerritory);

        Assert.Null(QuestObjectLinks.FromQuestArgument(67124, "ACTOR0", 1012344));
        Assert.Null(QuestObjectLinks.FromQuestArgument(67124, "EOBJ_FOOTPRINT_01", 5888470));
    }

    [Fact]
    public void Battle_level_keeps_an_event_object_in_its_territory()
    {
        var link = QuestObjectLinks.FromBattleLevel(67124, QuestObjectLinks.EventObject, 2005711, 479);

        Assert.NotNull(link);
        Assert.True(link.Value.LimitToTerritory);
        Assert.Equal((ushort)479, link.Value.Territory);
        Assert.Null(QuestObjectLinks.FromBattleLevel(67124, 9, 4316, 479));
        Assert.Null(QuestObjectLinks.FromBattleLevel(67124, QuestObjectLinks.EventObject, 2005711, 0));
    }

    [Fact]
    public void Accepted_sheet_link_is_listed_in_any_zone()
    {
        var links = new[]
        {
            new QuestObjectLink(67124, 2005408, 0, false),
        };
        var accepted = new HashSet<uint> { 67124 };

        Assert.Contains(2005408u, QuestObjectLinks.BaseIds(links, accepted, 397));
        Assert.Contains(2005408u, QuestObjectLinks.BaseIds(links, accepted, 479));
    }

    [Fact]
    public void Battle_prop_stays_in_its_own_territory()
    {
        var links = new[]
        {
            new QuestObjectLink(67124, 2005711, 479, true),
            new QuestObjectLink(67124, 2005850, 479, true),
        };
        var accepted = new HashSet<uint> { 67124 };

        var inDuty = QuestObjectLinks.BaseIds(links, accepted, 479);
        Assert.Contains(2005711u, inDuty);
        Assert.Contains(2005850u, inDuty);
        Assert.Empty(QuestObjectLinks.BaseIds(links, accepted, 397));
    }

    [Fact]
    public void Unaccepted_quest_adds_no_object()
    {
        var links = new[]
        {
            new QuestObjectLink(67124, 2005408, 0, false),
            new QuestObjectLink(67124, 2005711, 479, true),
        };

        Assert.Empty(QuestObjectLinks.BaseIds(links, new HashSet<uint>(), 479));
        Assert.Empty(QuestObjectLinks.BaseIds(links, new HashSet<uint> { 1 }, 479));
    }

    [Fact]
    public void Hiding_place_range_is_a_trigger_and_an_enemy_is_not()
    {
        Assert.True(QuestObjectLinks.IsBattleTrigger(67124, QuestObjectLinks.EventRange, 479));
        Assert.False(QuestObjectLinks.IsBattleTrigger(67124, 9, 479));
        Assert.False(QuestObjectLinks.IsBattleTrigger(0, QuestObjectLinks.EventRange, 479));
    }

    [Fact]
    public void Two_quests_for_one_prop_leave_the_name_unset()
    {
        var pairs = new (uint, uint)[] { (2005711, 67124), (2005711, 67125) };

        Assert.Equal(0u, QuestObjectLinks.UniqueQuest(pairs, 2005711));
        Assert.Equal(67124u, QuestObjectLinks.UniqueQuest(new (uint, uint)[] { (2005711, 67124) }, 2005711));
    }
}
