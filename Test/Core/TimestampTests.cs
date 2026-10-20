using EricGameLauncher;
using Xunit;
using YamlDotNet.Serialization;

namespace EricGameLauncher.Tests;

public sealed class TimestampTests
{
    private static readonly ISerializer Serializer = new SerializerBuilder().WithTypeConverter(new TimestampYaml()).Build();
    private static readonly IDeserializer Deserializer = new DeserializerBuilder().WithTypeConverter(new TimestampYaml()).Build();

    public sealed class Record { public DateTimeOffset? DeletedAt { get; set; } }

    [Theory]
    [InlineData("2026-10-19T12:34:56.1234567+08:00")]
    [InlineData("2026-10-19T04:34:56.1234567+00:00")]
    [Trait("Category", "Core")]
    public void DeletionDeadlineSurvivesYamlRoundTrip(string timestamp)
    {
        var original = DateTimeOffset.Parse(timestamp, System.Globalization.CultureInfo.InvariantCulture);
        string yaml = Serializer.Serialize(new Record { DeletedAt = original });
        var restored = Deserializer.Deserialize<Record>(yaml).DeletedAt;
        Assert.Equal(original, restored);
        Assert.Equal(original.Offset, restored!.Value.Offset);
        Assert.Equal(original.AddHours(72), restored.Value.AddHours(72));
        Assert.DoesNotContain("Ticks:", yaml);
    }

    [Fact]
    [Trait("Category", "Core")]
    public void NoDeletionDateRemainsNull() => Assert.Null(Deserializer.Deserialize<Record>(Serializer.Serialize(new Record())).DeletedAt);

    [Fact]
    [Trait("Category", "Core")]
    public void LegacyObjectTimestampRecoversUtcInstant()
    {
        var record = Deserializer.Deserialize<Record>("DeletedAt:\n  DateTime: 2026-10-19T12:34:56.0000000\n  UtcDateTime: 2026-10-19T04:34:56.0000000Z\n  Offset: 08:00:00\n  Ticks: 123\n");
        Assert.Equal(new DateTimeOffset(2026, 10, 19, 4, 34, 56, TimeSpan.Zero), record.DeletedAt);
    }

    [Theory]
    [InlineData("DeletedAt: invalid")]
    [InlineData("DeletedAt:\n  Ticks: 123")]
    [Trait("Category", "Core")]
    public void InvalidTimestampCannotSilentlyBecomeExpired(string yaml) => Assert.ThrowsAny<Exception>(() => Deserializer.Deserialize<Record>(yaml));
}
