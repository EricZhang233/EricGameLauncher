using updater.cfgver;
using Xunit;

namespace EricGameLauncher.Tests;

public sealed class MigrationTests
{
    [Fact]
    [Trait("Category", "Core")]
    public void CurrentVersionIsAnUnchangedNoOp()
    {
        var root = new Dictionary<object, object> { ["version"] = 2, ["custom"] = "keep" };
        Assert.False(MigrationRules.Apply(root, [], 2));
        Assert.Equal("keep", root["custom"]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3)]
    [Trait("Category", "Core")]
    public void InvalidVersionTransitionsAreRejected(int next)
    {
        var root = new Dictionary<object, object> { ["version"] = 0 };
        Assert.Throws<InvalidDataException>(() => MigrationRules.Apply(root, [new Dictionary<object, object> { ["from"] = 0, ["to"] = next }], 2));
        Assert.Equal(0, root["version"]);
    }

    [Fact]
    [Trait("Category", "Core")]
    public void MissingRulesAreRejected() => Assert.Throws<InvalidDataException>(() => MigrationRules.Apply(new() { ["version"] = 0 }, [], 1));

    [Fact]
    [Trait("Category", "Core")]
    public void UnknownTransformationIsRejected()
    {
        var rule = new Dictionary<object, object> { ["from"] = 0, ["to"] = 1, ["transformations"] = new List<object> { new Dictionary<object, object> { ["type"] = "Unknown" } } };
        Assert.Throws<InvalidDataException>(() => MigrationRules.Apply(new() { ["version"] = 0 }, [rule], 1));
    }
}
