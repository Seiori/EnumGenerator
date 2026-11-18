using System.Text;
using EnumGenerator.Tests.Enums;
using Xunit;

namespace EnumGenerator.Tests;

public class BehaviorTests
{
    [Fact]
    public void Length_CountsAllMembersIncludingAliases()
    {
        Assert.Equal(3, ColorExtensions.Length);
        Assert.Equal(5, AliasedExtensions.Length);
        Assert.Equal(0, EmptyExtensions.Length);
    }

    [Theory]
    [InlineData(Status.Pending, "Ready to Start", "Pending", "pending_status")]
    [InlineData(Status.Active, "Active", "Working on it", "Active")] // [Description] describes; it is not a display name
    [InlineData(Status.Completed, "All done", "Finished and closed", "Completed")]
    [InlineData(Status.OnHold, "OnHold", "OnHold", "on-hold")]
    [InlineData(Status.Archived, "Archived", "Archived", "stj_name")]
    public void DisplayDescriptionAndSerializedNames_AreSeparateRoles(Status value, string display, string description, string serialized)
    {
        Assert.Equal(display, value.GetDisplayValue());
        Assert.Equal(description, value.GetDescription());
        Assert.Equal(serialized, value.GetEnumMemberValue());
    }

    [Theory]
    [InlineData(Tricky.Quote, "Say \"hi\"", "C:\\temp")]
    [InlineData(Tricky.Unicode, "Café ☕", "café")]
    [InlineData(Tricky.EmptyName, "EmptyName", "")]
    [InlineData(Tricky.Newline, "Newline", "Line\nBreak")]
    [InlineData(Tricky.Html, "Html", "<script>&'")]
    public void SpecialCharacters_AreEmittedVerbatim(Tricky value, string display, string serialized)
    {
        Assert.Equal(display, value.GetDisplayValue());
        Assert.Equal(serialized, value.GetEnumMemberValue());

        Assert.True(TrickyExtensions.TryParseEnumMemberValue(serialized, out var parsed));
        Assert.Equal(value, parsed);
        Assert.True(TrickyExtensions.TryParseEnumMemberValue(serialized.AsSpan(), out parsed));
        Assert.Equal(value, parsed);
        Assert.True(TrickyExtensions.TryParseEnumMemberValue(serialized.ToUpperInvariant(), ignoreCase: true, out parsed));
        Assert.Equal(value, parsed);
        Assert.True(TrickyExtensions.TryParseEnumMemberValueUtf8(Encoding.UTF8.GetBytes(serialized), out parsed));
        Assert.Equal(value, parsed);
    }

    [Theory]
    [InlineData(Roles.Pending, "Ready to start", "Waiting to be picked up", "pending_status")]
    [InlineData(Roles.Contested, "own display", "own description", "own_json")]
    [InlineData(Roles.StandardOnly, "StandardOnly", "standard only", "standard_json")]
    [InlineData(Roles.DisplayBoth, "Shown", "from Display", "DisplayBoth")]
    [InlineData(Roles.DisplayOnly, "Display only", "DisplayOnly", "DisplayOnly")]
    [InlineData(Roles.DescriptionOnly, "DescriptionOnly", "Description only", "DescriptionOnly")]
    [InlineData(Roles.JsonOnly, "JsonOnly", "JsonOnly", "json-only")]
    [InlineData(Roles.Plain, "Plain", "Plain", "Plain")]
    public void RoleAttributes_SetOneTextEach_AndWinOverStandardAttributes(Roles value, string display, string description, string json)
    {
        Assert.Equal(display, value.GetDisplayValue());
        Assert.Equal(description, value.GetDescription());
        Assert.Equal(json, value.GetEnumMemberValue());
        Assert.Equal(Enum.GetName(value), value.GetString()); // the member name is never affected

        Assert.True(RolesExtensions.TryParseEnumMemberValue(json, out var parsed));
        Assert.Equal(value, parsed);
        Assert.True(RolesExtensions.TryParseEnumMemberValueUtf8(Encoding.UTF8.GetBytes(json), out parsed));
        Assert.Equal(value, parsed);
        Assert.True(RolesExtensions.TryParse(Enum.GetName(value), out parsed));
        Assert.Equal(value, parsed);
    }

    [Fact]
    public void RoleAttributes_LosingStandardNamesAreNotSerializedNames()
    {
        Assert.False(RolesExtensions.TryParseEnumMemberValue("stj_json", out _));
        Assert.False(RolesExtensions.TryParseEnumMemberValue("enum_member_json", out _));
        Assert.False(RolesExtensions.TryParseEnumMemberValue("property_json", out _));
    }

    [Fact]
    public void RoleAttributes_AreNotEmittedIntoMetadata()
    {
        // Like [EnumExtensions], they are [Conditional]: the compiled enum carries no trace of the generator.
        Assert.Empty(typeof(Roles).GetField(nameof(Roles.Pending))!.GetCustomAttributes(inherit: false));
        Assert.Empty(typeof(Roles).GetField(nameof(Roles.JsonOnly))!.GetCustomAttributes(inherit: false));

        var contested = typeof(Roles).GetField(nameof(Roles.Contested))!.GetCustomAttributes(inherit: false);
        Assert.NotEmpty(contested);
        Assert.DoesNotContain(contested, a => a.GetType().Namespace == "Seiori.EnumGenerator");
    }

    [Fact]
    public void TryParseEnumMemberValue_MatchesSerializedNamesOnly()
    {
        Assert.True(StatusExtensions.TryParseEnumMemberValue("pending_status", out var value));
        Assert.Equal(Status.Pending, value);
        Assert.True(StatusExtensions.TryParseEnumMemberValue("Active", out value));
        Assert.Equal(Status.Active, value);

        Assert.False(StatusExtensions.TryParseEnumMemberValue("Pending", out _));
        Assert.False(StatusExtensions.TryParseEnumMemberValue("PENDING_STATUS", out _));
        Assert.True(StatusExtensions.TryParseEnumMemberValue("PENDING_STATUS", ignoreCase: true, out value));
        Assert.Equal(Status.Pending, value);
        Assert.False(StatusExtensions.TryParseEnumMemberValueUtf8("Pending"u8, out _));
        Assert.False(StatusExtensions.TryParseEnumMemberValueUtf8(ReadOnlySpan<byte>.Empty, out _));
    }

    [Fact]
    public void TryParse_UsesMemberNamesRegardlessOfSerializedNames()
    {
        // 1.x only accepted "pending_status" here; names now always parse, like Enum.TryParse.
        Assert.True(StatusExtensions.TryParse("Pending", out var value));
        Assert.Equal(Status.Pending, value);
        Assert.False(StatusExtensions.TryParse("pending_status", out _));
    }

    [Fact]
    public void TryParse_CaseOnlyDifferences()
    {
        Assert.True(CaseVariantsExtensions.TryParse("VALUE", out var value));
        Assert.Equal(CaseVariants.VALUE, value);
        Assert.True(CaseVariantsExtensions.TryParse("Value", out value));
        Assert.Equal(CaseVariants.Value, value);
        Assert.True(CaseVariantsExtensions.TryParse("vAlUe", ignoreCase: true, out value));
        Assert.Equal(CaseVariants.Value, value); // first declared wins
        Assert.False(CaseVariantsExtensions.TryParse("vAlUe", out _));
    }

    [Fact]
    public void Parse_ThrowsLikeEnumParse()
    {
        Assert.Equal(Color.Blue, ColorExtensions.Parse("Blue"));
        Assert.Equal(Color.Blue, ColorExtensions.Parse("blue", ignoreCase: true));
        Assert.Equal(Color.Blue, ColorExtensions.Parse("Blue".AsSpan()));
        Assert.Equal(Color.Blue, ColorExtensions.Parse("BLUE".AsSpan(), ignoreCase: true));

        var ex = Assert.Throws<ArgumentException>(() => ColorExtensions.Parse("Purple"));
        Assert.Contains("Purple", ex.Message);
        Assert.Contains("Color", ex.Message);
        Assert.Throws<ArgumentException>(() => ColorExtensions.Parse("blue"));
        Assert.Throws<ArgumentException>(() => ColorExtensions.Parse("Purple".AsSpan()));
        Assert.Throws<ArgumentNullException>(() => ColorExtensions.Parse(null!));
    }

    [Fact]
    public void IsDefined_ByName()
    {
        Assert.True(ColorExtensions.IsDefined("Green"));
        Assert.True(ColorExtensions.IsDefined("Green".AsSpan()));
        Assert.False(ColorExtensions.IsDefined("green"));
        Assert.False(ColorExtensions.IsDefined((string?)null));
    }

    [Fact]
    public void CaseConversions()
    {
        Assert.Equal("ONHOLD", Status.OnHold.GetStringUpperCase());
        Assert.Equal("onhold", Status.OnHold.GetStringLowerCase());
        Assert.Equal("99", ((Status)99).GetStringUpperCase());
        Assert.Equal("READ, EXECUTE", (Permissions.Read | Permissions.Execute).GetStringUpperCase());
        Assert.Equal("read, execute", (Permissions.Read | Permissions.Execute).GetStringLowerCase());
    }

    [Fact]
    public void UndefinedValues_FallBackLikeToString()
    {
        Assert.Equal("99", ((Status)99).GetDisplayValue());
        Assert.Equal("99", ((Status)99).GetDescription());
        Assert.Equal("99", ((Status)99).GetEnumMemberValue());
        Assert.Equal("It says \"hi\"\tand tabs", Tricky.Quote.GetDescription());
        Assert.Equal("Blue", Color.Blue.GetDescription()); // no description anywhere: the member name
        Assert.Equal("-5", ((Color)(-5)).GetString());
        Assert.Equal("Read, Execute", (Permissions.Read | Permissions.Execute).GetString());
    }

    [Fact]
    public void StringLookups_AgreeForEveryValueAroundTheTable()
    {
        // DenseGaps is looked up in tables with empty slots; walk well past both ends and through every gap.
        for (var i = -20; i <= 20; i++)
        {
            var value = (DenseGaps)i;
            var name = Enum.IsDefined(value) ? Enum.GetName(value)! : i.ToString();

            Assert.Equal(name, value.GetString());
            Assert.Equal(name.ToUpperInvariant(), value.GetStringUpperCase());
            Assert.Equal(name.ToLowerInvariant(), value.GetStringLowerCase());
            Assert.Equal(name, value.GetDisplayValue());
            Assert.Equal(value == DenseGaps.A ? "first" : name, value.GetDescription());
            Assert.Equal(value == DenseGaps.D ? "dee" : name, value.GetEnumMemberValue());
        }

        for (int i = sbyte.MinValue; i <= sbyte.MaxValue; i++)
        {
            var value = (DenseNegative)(sbyte)i;
            var name = Enum.IsDefined(value) ? Enum.GetName(value)! : i.ToString();

            Assert.Equal(name, value.GetString());
            Assert.Equal(name, value.GetDisplayValue());
            Assert.Equal(name, value.GetEnumMemberValue());
            Assert.Equal(name.ToUpperInvariant(), value.GetStringUpperCase());
        }
    }

    [Fact]
    public void EnumsWithoutSerializedNames_ParseThemAsMemberNames()
    {
        // Color declares no serialized names, so these overloads forward to TryParse.
        Assert.True(ColorExtensions.TryParseEnumMemberValue("Blue", out var value));
        Assert.Equal(Color.Blue, value);
        Assert.True(ColorExtensions.TryParseEnumMemberValue("Green".AsSpan(), out value));
        Assert.Equal(Color.Green, value);
        Assert.True(ColorExtensions.TryParseEnumMemberValue("bLUE", ignoreCase: true, out value));
        Assert.Equal(Color.Blue, value);
        Assert.True(ColorExtensions.TryParseEnumMemberValue("RED".AsSpan(), ignoreCase: true, out value));
        Assert.Equal(Color.Red, value);
        Assert.True(ColorExtensions.TryParseEnumMemberValueUtf8("Red"u8, out value));
        Assert.Equal(Color.Red, value);

        Assert.False(ColorExtensions.TryParseEnumMemberValue("blue", out _));
        Assert.False(ColorExtensions.TryParseEnumMemberValue("blue", ignoreCase: false, out _));
        Assert.False(ColorExtensions.TryParseEnumMemberValue("Purple".AsSpan(), ignoreCase: true, out _));
        Assert.False(ColorExtensions.TryParseEnumMemberValue((string?)null, out _));
        Assert.False(ColorExtensions.TryParseEnumMemberValue(null, ignoreCase: true, out _));

        Assert.Equal("Blue", Color.Blue.GetEnumMemberValue());
        Assert.Equal("Blue", Color.Blue.GetDisplayValue());
        Assert.Equal("7", ((Color)7).GetEnumMemberValue());
    }

    [Fact]
    public void JsonConverter_IsOneCachedInstance() =>
        Assert.Same(ColorExtensions.JsonConverter, ColorExtensions.JsonConverter);

    [Fact]
    public void FlagCombinations_AreFormattedLikeToString_ForEveryValueOfTheType()
    {
        // Every value a byte can hold, asked twice: the first call formats and remembers, the second reads the table.
        for (var i = 0; i <= byte.MaxValue; i++)
        {
            var value = (Toppings)i;
            var expected = value.ToString();

            for (var pass = 0; pass < 2; pass++)
            {
                Assert.Equal(expected, value.GetString());
                Assert.Equal(expected.ToUpperInvariant(), value.GetStringUpperCase());
                Assert.Equal(expected.ToLowerInvariant(), value.GetStringLowerCase());
                Assert.Equal(value == Toppings.Cheese ? "Extra cheese" : expected, value.GetDisplayValue());
                Assert.Equal(expected, value.GetDescription());
                Assert.Equal(expected, value.GetEnumMemberValue());
            }
        }

        for (var i = -40L; i <= 40; i++)
        {
            Assert.Equal(((LongFlags)i).ToString(), ((LongFlags)i).GetString());
            Assert.Equal(((LongFlags)i).ToString(), ((LongFlags)i).GetString());
            Assert.Equal(((SignedFlags)i).ToString(), ((SignedFlags)i).GetString());
            Assert.Equal(((WideFlags)i).ToString(), ((WideFlags)i).GetString());
        }

        Assert.Equal("Low, High", (WideFlags.Low | WideFlags.High).GetString());
    }

    [Fact]
    public void FlagCombinations_AreRememberedAfterTheFirstCall()
    {
        var combination = Toppings.Ham | Toppings.Olives;
        Assert.Equal("Ham, Olives", combination.GetString());
        Assert.Same(combination.GetString(), combination.GetString());
        Assert.Same(combination.GetStringUpperCase(), combination.GetStringUpperCase());
        Assert.Same(((Toppings)0).GetString(), ((Toppings)0).GetString());

        // Each text method remembers its own text: one never hands back another's.
        Assert.Equal("HAM, OLIVES", combination.GetStringUpperCase());
        Assert.Equal("ham, olives", combination.GetStringLowerCase());
        Assert.Equal("Ham, Olives", combination.GetString());
    }

    [Fact]
    public void FlagCombinations_AreSafeToFormatFromManyThreads()
    {
        // Slots are filled without locking; racing threads must still all read the right text.
        var failures = 0;
        Parallel.For(0, 64, _ =>
        {
            for (var i = 0; i <= byte.MaxValue; i++)
            {
                var value = (Toppings)i;
                if (value.GetString() != value.ToString() || value.GetStringLowerCase() != value.ToString().ToLowerInvariant())
                {
                    Interlocked.Increment(ref failures);
                }
            }
        });

        Assert.Equal(0, failures);
    }

    [Fact]
    public void GetUnderlyingValue_ReturnsExactUnderlyingType()
    {
        long longMin = Longs.Min.GetUnderlyingValue();
        ulong ulongMax = ULongs.Max.GetUnderlyingValue();
        byte byteMax = Bytes.Max.GetUnderlyingValue();
        Assert.Equal(long.MinValue, longMin);
        Assert.Equal(ulong.MaxValue, ulongMax);
        Assert.Equal(byte.MaxValue, byteMax);
    }

    [Fact]
    public void HasFlagFast_MatchesHasFlag()
    {
        for (var a = -1; a < 16; a++)
        {
            for (var b = -1; b < 16; b++)
            {
                Assert.Equal(((Permissions)a).HasFlag((Permissions)b), ((Permissions)a).HasFlagFast((Permissions)b));
            }
        }
    }

    [Fact]
    public void ObsoleteErrorMembers_AreHandledByValue()
    {
        var gone = (WithObsolete)2;
        Assert.Equal("Gone", gone.GetString());
        Assert.True(WithObsoleteExtensions.IsDefined(gone));
        Assert.True(WithObsoleteExtensions.TryParse("Gone", out var parsed));
        Assert.Equal(gone, parsed);
    }

    [Fact]
    public void GeneratedClass_MatchesEnumAccessibility()
    {
        Assert.True(typeof(ColorExtensions).IsPublic);
        Assert.False(typeof(InternalEnumExtensions).IsPublic);
        Assert.False(typeof(OuterMiddleDeepExtensions).IsPublic); // Middle is internal
        Assert.Equal("EnumGenerator.Tests.Renamed", typeof(Renamed.RenamedHelpers).Namespace);
    }

    [Fact]
    public void MarkerAttribute_IsNotEmittedIntoMetadata()
    {
        // [Conditional] keeps consumers free of any reference to the generator at runtime.
        Assert.Empty(typeof(Color).GetCustomAttributes(inherit: false));
    }
}
