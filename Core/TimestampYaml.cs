using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace EricGameLauncher;

internal sealed class TimestampYaml : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(DateTimeOffset) || type == typeof(DateTimeOffset?);

    public object? ReadYaml(IParser parser, Type type)
    {
        if (parser.TryConsume<Scalar>(out var scalar))
        {
            if (string.IsNullOrWhiteSpace(scalar.Value) || scalar.Value is "null" or "~") return null;
            return DateTimeOffset.Parse(scalar.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }
        var legacy = new DeserializerBuilder().Build().Deserialize<Dictionary<string, object>>(parser);
        if (legacy.TryGetValue("UtcDateTime", out var utc))
            return DateTimeOffset.Parse(Convert.ToString(utc, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        if (legacy.TryGetValue("DateTime", out var date) && legacy.TryGetValue("Offset", out var offset))
            return new DateTimeOffset(DateTime.SpecifyKind(DateTime.Parse(Convert.ToString(date, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture), DateTimeKind.Unspecified), TimeSpan.Parse(Convert.ToString(offset, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture));
        throw new InvalidDataException("Invalid persisted deletion timestamp.");
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type) => emitter.Emit(new Scalar(value is DateTimeOffset timestamp ? timestamp.ToString("O", CultureInfo.InvariantCulture) : ""));
}
