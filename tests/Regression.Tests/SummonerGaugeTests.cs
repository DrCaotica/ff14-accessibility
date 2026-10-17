using FF14Accessibility.Services;

namespace Regression.Tests;

public class SummonerGaugeTests
{
    [Fact]
    public void ChargedGemIsSpokenOnlyWhenNoSummonOccupiesTheSlot()
    {
        Assert.True(JobGaugeService.SummonReadyNow(true, 0, 0));
        Assert.False(JobGaugeService.SummonReadyNow(false, 0, 0));
        Assert.False(JobGaugeService.SummonReadyNow(true, 1, 0));
        Assert.False(JobGaugeService.SummonReadyNow(true, 0, 1));
        Assert.False(JobGaugeService.SummonReadyNow(true, 15000, 2));
    }

    [Fact]
    public void SummonerPreviewDoesNotOfferAllThreeReady()
    {
        var tracks = GaugeReadyCues.ByJob()
            .Where(job => job.Cues.Contains(GaugeReadyCueId.Ruby))
            .ToArray();

        Assert.Equal(2, tracks.Length);
        foreach (var track in tracks)
        {
            Assert.Contains(GaugeReadyCueId.Topaz, track.Cues);
            Assert.Contains(GaugeReadyCueId.Emerald, track.Cues);
            Assert.Contains(GaugeReadyCueId.Aetherflow, track.Cues);
            Assert.DoesNotContain(GaugeReadyCueId.AllGems, track.Cues);
        }

        Assert.Contains(GaugeReadyCues.ByJob(), job => job.Cues.Contains(GaugeReadyCueId.ThreeSen));
    }
}
