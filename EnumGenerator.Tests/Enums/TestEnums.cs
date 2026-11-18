using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Seiori.EnumGenerator;

namespace EnumGenerator.Tests.Enums;

/// <summary>Contiguous from zero: IsDefined is a single unsigned compare.</summary>
[EnumExtensions]
public enum Color
{
    Red,
    Green,
    Blue,
}

[EnumExtensions]
public enum Status
{
    [Display(Name = "Ready to Start")]
    [JsonPropertyName("pending_status")]
    Pending,

    [Description("Working on it")]
    Active,

    [Description("Finished and closed")]
    [Display(Name = "All done")]
    Completed,

    [EnumMember(Value = "on-hold")]
    OnHold,

    [EnumMember(Value = "ignored: JsonStringEnumMemberName wins")]
    [JsonStringEnumMemberName("stj_name")]
    Archived,
}

/// <summary>Gaps within a 64-wide window: IsDefined uses a bitmask.</summary>
[EnumExtensions]
public enum Sparse
{
    MinusTen = -10,
    Zero = 0,
    Five = 5,
    Fifty = 50,
}

/// <summary>Wide spread: IsDefined falls back to a switch.</summary>
[EnumExtensions]
public enum VerySparse
{
    Min = int.MinValue,
    A = -1000,
    B = 3,
    C = 1_000_000,
    Max = int.MaxValue,
}

/// <summary>Dense with gaps and a non-zero minimum: string lookups index a table that has empty slots.</summary>
[EnumExtensions]
public enum DenseGaps
{
    [Description("first")]
    A = 3,
    B = 4,

    [JsonStringEnumMemberName("dee")]
    D = 6,
    F = 9,
}

/// <summary>The package's own role attributes, alone and competing with the standard ones.</summary>
[EnumExtensions]
public enum Roles
{
    [EnumDisplay("Ready to start")]
    [EnumDescription("Waiting to be picked up")]
    [EnumJson("pending_status")]
    Pending,

    /// <summary>Own attributes win over every standard one.</summary>
    [EnumDisplay("own display")]
    [EnumDescription("own description")]
    [Display(Name = "standard display", Description = "display description")]
    [Description("standard description")]
    [EnumJson("own_json")]
    [JsonStringEnumMemberName("stj_json")]
    [EnumMember(Value = "enum_member_json")]
    [JsonPropertyName("property_json")]
    Contested,

    /// <summary>Standard attributes still work when the own ones are absent. [Description] is a description only.</summary>
    [Description("standard only")]
    [JsonPropertyName("standard_json")]
    StandardOnly,

    /// <summary>[Display] feeds two roles from its two properties, and its Description beats [Description].</summary>
    [Display(Name = "Shown", Description = "from Display")]
    [Description("loses to Display")]
    DisplayBoth,

    /// <summary>Each role is independent: one text never leaks into another role.</summary>
    [EnumDisplay("Display only")]
    DisplayOnly,

    [EnumDescription("Description only")]
    DescriptionOnly,

    [EnumJson("json-only")]
    JsonOnly,

    Plain,
}

/// <summary>Serialized names that, ignoring case, are other members' names: serialized names must win.</summary>
[EnumExtensions]
public enum Crossed
{
    [JsonStringEnumMemberName("second")]
    First,

    [JsonStringEnumMemberName("FIRST")]
    Second,
    Third,

    [JsonStringEnumMemberName("with_underscore")]
    Fourth,

    [JsonStringEnumMemberName("with-hyphen-42")]
    Fifth,
}

/// <summary>A name whose escaped JSON form is longer than the converter's stack buffer.</summary>
[EnumExtensions]
public enum LongNames
{
    Short,
    ThisMemberNameIsFarLongerThanTwentyTwoCharacters,
}

/// <summary>Dense around zero in a narrow signed type: the table index is the value minus a negative minimum.</summary>
[EnumExtensions]
public enum DenseNegative : sbyte
{
    MinusTwo = -2,
    MinusOne = -1,
    One = 1,
    Two = 2,
}

[EnumExtensions]
public enum Aliased
{
    None = 0,
    Default = 0,
    First = 1,
    Primary = 1,
    Second = 2,
}

[EnumExtensions]
public enum Bytes : byte
{
    Zero = 0,
    Mid = 128,
    Max = 255,
}

[EnumExtensions]
public enum SBytes : sbyte
{
    Min = sbyte.MinValue,
    MinusOne = -1,
    Max = sbyte.MaxValue,
}

[EnumExtensions]
public enum Shorts : short
{
    Min = short.MinValue,
    Zero = 0,
    Max = short.MaxValue,
}

[EnumExtensions]
public enum UShorts : ushort
{
    Zero = 0,
    Max = ushort.MaxValue,
}

[EnumExtensions]
public enum UInts : uint
{
    Zero = 0,
    High = 0x80000000,
    Max = uint.MaxValue,
}

[EnumExtensions]
public enum Longs : long
{
    Min = long.MinValue,
    MinusOne = -1,
    Zero = 0,
    Max = long.MaxValue,
}

/// <summary>Contiguous and negative in a 64-bit enum.</summary>
[EnumExtensions]
public enum LongsDense : long
{
    A = -2,
    B = -1,
    C = 0,
    D = 1,
}

[EnumExtensions]
public enum ULongs : ulong
{
    Zero = 0,
    One = 1,
    Max = ulong.MaxValue,
}

[Flags]
[EnumExtensions]
public enum Permissions
{
    None = 0,
    Read = 1,
    Write = 2,
    Execute = 4,
    ReadWrite = Read | Write,
    All = Read | Write | Execute,
}

/// <summary>No zero member and three separate bits: every combination, and zero, is formatted at run time.</summary>
[Flags]
[EnumExtensions]
public enum Toppings : byte
{
    [EnumDisplay("Extra cheese")]
    Cheese = 1,
    Ham = 2,
    Olives = 4,
    Pepper = 0x80,
}

/// <summary>Bits too far apart for a combination table: combinations go through Enum.ToString each time.</summary>
[Flags]
[EnumExtensions]
public enum WideFlags
{
    None = 0,
    Low = 1,
    High = 1 << 20,
}

/// <summary>A negative member (all bits set), as <c>~0</c> or <c>-1</c> masks produce.</summary>
[Flags]
[EnumExtensions]
public enum SignedFlags : short
{
    A = 1,
    B = 2,
    All = -1,
}

/// <summary>A 64-bit flags enum with small values: the table index is a ulong.</summary>
[Flags]
[EnumExtensions]
public enum LongFlags : long
{
    A = 1,
    B = 2,
    D = 8,
}

[EnumExtensions]
public enum Empty
{
}

[EnumExtensions]
public enum Keywords
{
    @class,
    @int,
    @namespace,
    value,
    result,
}

[EnumExtensions]
public enum Tricky
{
    [Display(Name = "Say \"hi\"", Description = "It says \"hi\"\tand tabs")]
    [JsonPropertyName("C:\\temp")]
    Quote,

    [Display(Name = "Café ☕")]
    [JsonPropertyName("café")]
    Unicode,

    [JsonPropertyName("")]
    EmptyName,

    [JsonPropertyName("Line\nBreak")]
    Newline,

    [JsonPropertyName("<script>&'")]
    Html,
}

/// <summary>Same lengths with shared prefixes: exercises the nested character dispatch.</summary>
[EnumExtensions]
public enum SameLength
{
    Abc,
    Abd,
    Xbc,
    Abe,
    Bbc,
    Aac,
}

[EnumExtensions]
public enum CaseVariants
{
    Value,
    VALUE,
    Other,
}

[EnumExtensions(ExtensionClassName = "RenamedHelpers", ExtensionClassNamespace = "EnumGenerator.Tests.Renamed")]
public enum Renamed
{
    A,
    B,
}

public class Outer
{
    [EnumExtensions]
    public enum Inner
    {
        X,
        Y,
    }

    internal class Middle
    {
        [EnumExtensions]
        public enum Deep
        {
            P,
            Q,
        }
    }
}

[EnumExtensions]
internal enum InternalEnum
{
    One,
    Two,
}

[EnumExtensions]
public enum WithObsolete
{
    Current,

    [Obsolete("old")]
    Old,

    [Obsolete("gone", true)]
    Gone,
}

[EnumExtensions]
[JsonConverter(typeof(AttributedJsonExtensions.AttributedJsonJsonConverter))]
public enum AttributedJson
{
    [JsonStringEnumMemberName("first-one")]
    First,
    Second,
}
