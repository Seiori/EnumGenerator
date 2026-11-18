using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EnumGenerator.Tests.Enums;
using Xunit;

namespace EnumGenerator.Tests;

public class JsonTests
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(StatusExtensions.JsonConverter);
        options.Converters.Add(TrickyExtensions.JsonConverter);
        options.Converters.Add(PermissionsExtensions.JsonConverter);
        options.Converters.Add(ULongsExtensions.JsonConverter);
        options.Converters.Add(LongsExtensions.JsonConverter);
        options.Converters.Add(ColorExtensions.JsonConverter);
        return options;
    }

    [Theory]
    [InlineData(Status.Pending, "\"pending_status\"")]
    [InlineData(Status.Active, "\"Active\"")]
    [InlineData(Status.OnHold, "\"on-hold\"")]
    [InlineData(Status.Archived, "\"stj_name\"")]
    [InlineData((Status)99, "99")]
    public void RoundTrips(Status value, string json)
    {
        Assert.Equal(json, JsonSerializer.Serialize(value, Options));
        Assert.Equal(value, JsonSerializer.Deserialize<Status>(json, Options));
    }

    [Theory]
    [InlineData("\"PENDING_STATUS\"", Status.Pending)]
    [InlineData("\"Pending\"", Status.Pending)]
    [InlineData("\"onhold\"", Status.OnHold)]
    [InlineData("\"ON-HOLD\"", Status.OnHold)]
    [InlineData("\"archived\"", Status.Archived)]
    [InlineData("1", Status.Active)]
    public void Read_IsLenient(string json, Status expected) =>
        Assert.Equal(expected, JsonSerializer.Deserialize<Status>(json, Options));

    [Fact]
    public void Read_EscapedStrings_TakeTheSlowPathCorrectly()
    {
        // "pending_status": the raw bytes do not match, but the unescaped value does.
        var json = "\"pending" + '\\' + "u005Fstatus\"";
        Assert.Equal(Status.Pending, JsonSerializer.Deserialize<Status>(json, Options));
    }

    [Theory]
    [InlineData("\"nope\"")]
    [InlineData("\"\"")]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("1.5")]
    [InlineData("{}")]
    [InlineData("99999999999")]
    public void Read_InvalidInput_Throws(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Status>(json, Options));

    [Fact]
    public void Read_TextLongerThanTheStackBuffer_StillWorks()
    {
        // The lenient path unescapes into a 128-char stack buffer; tokens with more bytes than that use a string.
        var options = new JsonSerializerOptions { Converters = { LongNamesExtensions.JsonConverter } };
        static string Escaped(string text) => "\"" + string.Concat(text.Select(c => "\\u" + ((int)c).ToString("X4"))) + "\"";

        // Fits: 14 characters written as 84 bytes of escapes.
        Assert.Equal(Status.Pending, JsonSerializer.Deserialize<Status>(Escaped("PENDING_STATUS"), Options));

        // Fits unescaped (48 bytes), does not fit escaped (288 bytes); both must parse.
        const string name = nameof(LongNames.ThisMemberNameIsFarLongerThanTwentyTwoCharacters);
        Assert.Equal(48, name.Length);
        Assert.Equal(
            LongNames.ThisMemberNameIsFarLongerThanTwentyTwoCharacters,
            JsonSerializer.Deserialize<LongNames>("\"" + name.ToLowerInvariant() + "\"", options));
        Assert.Equal(
            LongNames.ThisMemberNameIsFarLongerThanTwentyTwoCharacters,
            JsonSerializer.Deserialize<LongNames>(Escaped(name), options));

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<LongNames>("\"" + new string('x', 500) + "\"", options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<LongNames>("\"" + new string('x', 128) + "\"", options));
    }

    [Theory]
    [InlineData("\"blue\"", Color.Blue)]
    [InlineData("\"GREEN\"", Color.Green)]
    [InlineData("\"R\\u0065d\"", Color.Red)]
    public void Read_IsLenient_ForEnumsWithoutSerializedNames(string json, Color expected) =>
        Assert.Equal(expected, JsonSerializer.Deserialize<Color>(json, Options));

    [Theory]
    [InlineData("second", Crossed.First)] // exact serialized name
    [InlineData("Second", Crossed.First)] // a serialized name ignoring case beats the member name "Second"
    [InlineData("SECOND", Crossed.First)]
    [InlineData("FIRST", Crossed.Second)]
    [InlineData("first", Crossed.Second)]
    [InlineData("First", Crossed.Second)]
    [InlineData("third", Crossed.Third)]
    [InlineData("THIRD", Crossed.Third)]
    [InlineData("fourth", Crossed.Fourth)] // member name, ignoring case
    [InlineData("WITH_UNDERSCORE", Crossed.Fourth)]
    [InlineData("With-Hyphen-42", Crossed.Fifth)]
    [InlineData("FIFTH", Crossed.Fifth)]
    public void Read_Lenient_ByteAndCharPathsAgree(string text, Crossed expected)
    {
        var options = new JsonSerializerOptions { Converters = { CrossedExtensions.JsonConverter } };

        // Plain text is matched on its UTF-8 bytes; escaping one character forces the unescape-to-chars path.
        Assert.Equal(expected, JsonSerializer.Deserialize<Crossed>("\"" + text + "\"", options));
        var escaped = "\\u" + ((int)text[0]).ToString("X4") + text[1..];
        Assert.Equal(expected, JsonSerializer.Deserialize<Crossed>("\"" + escaped + "\"", options));
    }

    [Theory]
    [InlineData("with underscore")]
    [InlineData("with\x7Funderscore")] // '_' (0x5F) and DEL (0x7F) share a switch label once bit 0x20 is set
    [InlineData("with`underscore")]
    [InlineData("with_hyphen_42")]
    [InlineData("with-hyphen-4\x12")] // '2' (0x32) and 0x12 differ only in bit 0x20 as well
    [InlineData("with\rhyphen-42")]
    [InlineData("fir\x17Ft")] // LATIN SMALL LETTER LONG S upper-cases to 'S' in Unicode, but it is not ASCII
    [InlineData("\x212Aird")] // KELVIN SIGN lower-cases to 'k'
    [InlineData("secon")]
    [InlineData("seconds")]
    public void Read_Lenient_RejectsNearMisses(string text)
    {
        var options = new JsonSerializerOptions { Converters = { CrossedExtensions.JsonConverter } };
        var json = JsonSerializer.Serialize(text); // let the serializer do any escaping the text needs
        var reference = Enum.GetNames<Crossed>()
            .Concat(Enum.GetValues<Crossed>().Select(v => v.GetEnumMemberValue()))
            .Any(name => string.Equals(name, text, StringComparison.OrdinalIgnoreCase));

        // Whatever OrdinalIgnoreCase says on this runtime is the contract; the converter must agree with it.
        if (reference) JsonSerializer.Deserialize<Crossed>(json, options);
        else Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Crossed>(json, options));

        // And the same text arriving unescaped, as raw UTF-8.
        var raw = Encoding.UTF8.GetBytes("\"" + text + "\"");
        if (text.All(c => c >= 0x20))
        {
            if (reference) JsonSerializer.Deserialize<Crossed>(raw, options);
            else Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Crossed>(raw, options));
        }
    }

    [Fact]
    public void Read_Lenient_EveryCaseBitFlip_AgreesWithTheCharMatchers()
    {
        // The byte matcher sets bit 0x20 before comparing, so flip that bit at every position of every name: a
        // letter changes case and must still match; anything else must be treated as the char matchers treat it.
        CheckFlips(CrossedExtensions.JsonConverter, static (s, out v) =>
            CrossedExtensions.TryParseEnumMemberValue(s, true, out v) || CrossedExtensions.TryParse(s, true, out v));
        CheckFlips(StatusExtensions.JsonConverter, static (s, out v) =>
            StatusExtensions.TryParseEnumMemberValue(s, true, out v) || StatusExtensions.TryParse(s, true, out v));
        CheckFlips(LongNamesExtensions.JsonConverter, static (s, out v) =>
            LongNamesExtensions.TryParseEnumMemberValue(s, true, out v) || LongNamesExtensions.TryParse(s, true, out v));
        CheckFlips(ColorExtensions.JsonConverter, static (s, out v) =>
            ColorExtensions.TryParseEnumMemberValue(s, true, out v) || ColorExtensions.TryParse(s, true, out v));
    }

    private delegate bool Oracle<T>(string text, out T value);

    private static void CheckFlips<T>(JsonConverter<T> converter, Oracle<T> oracle) where T : struct, Enum
    {
        var tokens = Enum.GetNames<T>()
            .Concat(Enum.GetValues<T>().Select(v => JsonSerializer.Deserialize<string>(Serialize(converter, v))!))
            .Distinct()
            .ToList();
        Assert.NotEmpty(tokens);

        foreach (var token in tokens)
        {
            for (var i = -1; i < token.Length; i++)
            {
                var chars = token.ToCharArray();
                if (i >= 0) chars[i] = (char)(chars[i] ^ 0x20);
                if (i < 0) chars = token.ToUpperInvariant().ToCharArray();
                var text = new string(chars);
                if (text.Any(static c => c < 0x20 || c is '"' or '\\')) continue; // not valid unescaped in JSON

                var expected = oracle(text, out var expectedValue);
                var json = Encoding.UTF8.GetBytes("\"" + text + "\"");
                var reader = new Utf8JsonReader(json);
                reader.Read();
                bool actual;
                T actualValue = default;
                try
                {
                    actualValue = converter.Read(ref reader, typeof(T), Options);
                    actual = true;
                }
                catch (JsonException)
                {
                    actual = false;
                }

                Assert.True(expected == actual, $"{typeof(T).Name}: '{text}' expected match={expected}, got {actual}");
                if (expected) Assert.Equal(expectedValue, actualValue);
            }
        }
    }

    private static string Serialize<T>(JsonConverter<T> converter, T value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) converter.Write(writer, value, Options);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    [Theory]
    [InlineData(Roles.Pending, "\"pending_status\"")]
    [InlineData(Roles.Contested, "\"own_json\"")]
    [InlineData(Roles.StandardOnly, "\"standard_json\"")]
    [InlineData(Roles.DisplayBoth, "\"DisplayBoth\"")]
    [InlineData(Roles.DescriptionOnly, "\"DescriptionOnly\"")]
    [InlineData(Roles.DisplayOnly, "\"DisplayOnly\"")]
    [InlineData(Roles.JsonOnly, "\"json-only\"")]
    [InlineData(Roles.Plain, "\"Plain\"")]
    public void RoleAttributes_DriveTheConverter(Roles value, string json)
    {
        var options = new JsonSerializerOptions { Converters = { RolesExtensions.JsonConverter } };
        Assert.Equal(json, JsonSerializer.Serialize(value, options));
        Assert.Equal(value, JsonSerializer.Deserialize<Roles>(json, options));
        Assert.Equal(value, JsonSerializer.Deserialize<Roles>(json.ToUpperInvariant(), options));

        // The member name is still accepted on read, as for every enum.
        Assert.Equal(value, JsonSerializer.Deserialize<Roles>("\"" + Enum.GetName(value) + "\"", options));

        var map = new Dictionary<Roles, int> { [value] = 1 };
        Assert.Equal("{" + json + ":1}", JsonSerializer.Serialize(map, options));
        Assert.Equal(map, JsonSerializer.Deserialize<Dictionary<Roles, int>>("{" + json + ":1}", options));
    }

    [Fact]
    public void Read_Lenient_NonAsciiNamesStillIgnoreCase()
    {
        // Tricky has non-ASCII serialized names, so it has no byte matcher; the char path handles all of it.
        Assert.Equal(Tricky.Unicode, JsonSerializer.Deserialize<Tricky>(Encoding.UTF8.GetBytes("\"CAFÉ\""), Options));
        Assert.Equal(Tricky.Html, JsonSerializer.Deserialize<Tricky>("\"HTML\"", Options));
    }

    [Fact]
    public void Read_SplitAcrossBufferSegments_Works()
    {
        // A value that straddles two segments has no contiguous span, so it skips the UTF-8 fast path.
        var json = Encoding.UTF8.GetBytes("\"on-hold\"");
        var first = new Segment(json.AsMemory(0, 4));
        var last = first.Append(json.AsMemory(4));
        var reader = new Utf8JsonReader(new System.Buffers.ReadOnlySequence<byte>(first, 0, last, last.Memory.Length));
        Assert.True(reader.Read());
        Assert.Equal(Status.OnHold, StatusExtensions.JsonConverter.Read(ref reader, typeof(Status), Options));
    }

    private sealed class Segment : System.Buffers.ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory) => Memory = memory;

        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;
            return next;
        }
    }

    [Fact]
    public void SpecialCharacters_RoundTrip()
    {
        foreach (var value in Enum.GetValues<Tricky>())
        {
            var json = JsonSerializer.Serialize(value, Options);
            Assert.Equal(value, JsonSerializer.Deserialize<Tricky>(json, Options));
        }

        // Unescaped non-ASCII UTF-8 hits the zero-allocation byte matcher.
        Assert.Equal(Tricky.Unicode, JsonSerializer.Deserialize<Tricky>(Encoding.UTF8.GetBytes("\"café\""), Options));
    }

    [Fact]
    public void Output_MatchesJsonStringEnumConverter_ForPlainEnums()
    {
        var reference = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        foreach (var value in Enum.GetValues<Color>())
        {
            Assert.Equal(JsonSerializer.Serialize(value, reference), JsonSerializer.Serialize(value, Options));
        }

        // Including serialized-name attributes, which JsonStringEnumConverter honours via JsonStringEnumMemberName.
        Assert.Equal(
            JsonSerializer.Serialize(Status.Archived, new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } }),
            JsonSerializer.Serialize(Status.Archived, Options));
    }

    [Fact]
    public void ExtremeUnderlyingValues_RoundTrip()
    {
        Assert.Equal("\"Max\"", JsonSerializer.Serialize(ULongs.Max, Options));
        Assert.Equal(ULongs.Max, JsonSerializer.Deserialize<ULongs>("18446744073709551615", Options));
        Assert.Equal((ULongs)12345, JsonSerializer.Deserialize<ULongs>(JsonSerializer.Serialize((ULongs)12345, Options), Options));
        Assert.Equal("\"Min\"", JsonSerializer.Serialize(Longs.Min, Options));
        Assert.Equal(Longs.Min, JsonSerializer.Deserialize<Longs>("-9223372036854775808", Options));
    }

    [Fact]
    public void FlagCombinations_RoundTripAsNumbers()
    {
        var combo = Permissions.Read | Permissions.Execute;
        Assert.Equal("5", JsonSerializer.Serialize(combo, Options));
        Assert.Equal(combo, JsonSerializer.Deserialize<Permissions>("5", Options));
        Assert.Equal("\"ReadWrite\"", JsonSerializer.Serialize(Permissions.ReadWrite, Options));
    }

    [Fact]
    public void DictionaryKeys_UseSerializedNames()
    {
        var map = new Dictionary<Status, int> { [Status.Pending] = 1, [Status.OnHold] = 2, [(Status)99] = 3 };
        var json = JsonSerializer.Serialize(map, Options);
        Assert.Equal("{\"pending_status\":1,\"on-hold\":2,\"99\":3}", json);
        Assert.Equal(map, JsonSerializer.Deserialize<Dictionary<Status, int>>(json, Options));
    }

    [Fact]
    public void ConverterAttributeOnEnum_IsHonoured()
    {
        Assert.Equal("\"first-one\"", JsonSerializer.Serialize(AttributedJson.First));
        Assert.Equal(AttributedJson.First, JsonSerializer.Deserialize<AttributedJson>("\"first-one\""));
        Assert.Equal(AttributedJson.Second, JsonSerializer.Deserialize<AttributedJson>("\"Second\""));
    }

    [Fact]
    public void EnumAsProperty_RoundTrips()
    {
        var model = new Model(Status.OnHold, [Color.Red, Color.Blue]);
        var json = JsonSerializer.Serialize(model, Options);
        Assert.Equal("{\"State\":\"on-hold\",\"Colors\":[\"Red\",\"Blue\"]}", json);
        var back = JsonSerializer.Deserialize<Model>(json, Options)!;
        Assert.Equal(model.State, back.State);
        Assert.Equal(model.Colors, back.Colors);
    }

    public sealed record Model(Status State, Color[] Colors);
}
