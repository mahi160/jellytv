using Jellyfin.Plugin.Broadcast.Configuration;
using Jellyfin.Plugin.Broadcast.Scheduling;
using Xunit;

namespace Jellyfin.Plugin.Broadcast.Tests;

public class BlockValidatorTests
{
    private static ProgrammingBlock ValidBlock(string name = "Prime Time") =>
        new() { Name = name, StartTime = "19:00", EndTime = "23:00" };

    [Fact]
    public void Validate_AcceptsWellFormedBlocks()
    {
        var (valid, invalid) = BlockValidator.Validate(new[] { ValidBlock() });

        Assert.Single(valid);
        Assert.Empty(invalid);
    }

    [Fact]
    public void Validate_RejectsEmptyName()
    {
        var block = ValidBlock();
        block.Name = "  ";

        var (valid, invalid) = BlockValidator.Validate(new[] { block });

        Assert.Empty(valid);
        Assert.Single(invalid);
    }

    [Fact]
    public void Validate_RejectsDuplicateNames()
    {
        var (valid, invalid) = BlockValidator.Validate(new[] { ValidBlock("A"), ValidBlock("A") });

        Assert.Single(valid);
        Assert.Single(invalid);
    }

    [Theory]
    [InlineData("19:00")]
    [InlineData("07:05")]
    public void Validate_AcceptsWellFormedTimes(string time)
    {
        var block = ValidBlock();
        block.StartTime = time;

        var (valid, _) = BlockValidator.Validate(new[] { block });

        Assert.Single(valid);
    }

    [Theory]
    [InlineData("25:00")]
    [InlineData("7pm")]
    [InlineData("")]
    [InlineData("24:00")]
    public void Validate_RejectsMalformedTimes(string time)
    {
        var block = ValidBlock();
        block.StartTime = time;

        var (valid, invalid) = BlockValidator.Validate(new[] { block });

        Assert.Empty(valid);
        Assert.Single(invalid);
    }

    [Fact]
    public void Validate_RejectsInvertedYearRange()
    {
        var block = ValidBlock();
        block.MinYear = 2020;
        block.MaxYear = 2010;

        var (valid, invalid) = BlockValidator.Validate(new[] { block });

        Assert.Empty(valid);
        Assert.Single(invalid);
    }

    [Fact]
    public void Validate_RejectsNegativeCooldown()
    {
        var block = ValidBlock();
        block.MovieCooldownDays = -1;

        var (valid, invalid) = BlockValidator.Validate(new[] { block });

        Assert.Empty(valid);
        Assert.Single(invalid);
    }

    [Fact]
    public void Validate_OneBadBlockDoesNotRejectGoodOnes()
    {
        var good = ValidBlock("Good");
        var bad = ValidBlock("Bad");
        bad.StartTime = "not-a-time";

        var (valid, invalid) = BlockValidator.Validate(new[] { good, bad });

        Assert.Single(valid);
        Assert.Equal("Good", valid[0].Name);
        Assert.Single(invalid);
    }
}
