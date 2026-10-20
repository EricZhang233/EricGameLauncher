using System.Reflection;
using Xunit;
using YamlDotNet.Serialization;

namespace EricGameLauncher.Tests;

public sealed class ResourceTests
{
    [Fact]
    [Trait("Category", "Core")]
    public void CurrentLanguagesContainMatchingNonEmptyResources()
    {
        string root = typeof(ResourceTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(x => x.Key == "TestRoot").Value!;
        var resources = new DeserializerBuilder().Build().Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(Path.Combine(root, "..", "text.yaml")));
        Assert.Equal(resources["Zh-CN"].Keys.Order(), resources["EN"].Keys.Order());
        foreach (string language in new[] { "Zh-CN", "EN", "CLI" })
            Assert.All(resources[language], item => Assert.False(string.IsNullOrWhiteSpace(item.Value), language + ": " + item.Key));
    }
}
