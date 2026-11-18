using EnumGenerator.Tests.Enums;
using EnumGenerator.Tests.Renamed;
using Xunit;
using Collisions = EnumGenerator.Tests.Enums.Collisions;

namespace EnumGenerator.Tests;

/// <summary>Every generated enum must behave exactly like the System.Enum API it replaces.</summary>
public class ParityTests
{
    [Fact]
    public void Color_MatchesSystemEnum() => Parity.Check(
        ColorExtensions.GetValues(), ColorExtensions.GetNames(), static v => v.GetString(),
        ColorExtensions.IsDefined, ColorExtensions.TryParse, ColorExtensions.TryParse);

    [Fact]
    public void Status_MatchesSystemEnum() => Parity.Check(
        StatusExtensions.GetValues(), StatusExtensions.GetNames(), static v => v.GetString(),
        StatusExtensions.IsDefined, StatusExtensions.TryParse, StatusExtensions.TryParse);

    [Fact]
    public void Sparse_MatchesSystemEnum() => Parity.Check(
        SparseExtensions.GetValues(), SparseExtensions.GetNames(), static v => v.GetString(),
        SparseExtensions.IsDefined, SparseExtensions.TryParse, SparseExtensions.TryParse);

    [Fact]
    public void VerySparse_MatchesSystemEnum() => Parity.Check(
        VerySparseExtensions.GetValues(), VerySparseExtensions.GetNames(), static v => v.GetString(),
        VerySparseExtensions.IsDefined, VerySparseExtensions.TryParse, VerySparseExtensions.TryParse);

    [Fact]
    public void DenseGaps_MatchesSystemEnum() => Parity.Check(
        DenseGapsExtensions.GetValues(), DenseGapsExtensions.GetNames(), static v => v.GetString(),
        DenseGapsExtensions.IsDefined, DenseGapsExtensions.TryParse, DenseGapsExtensions.TryParse);

    [Fact]
    public void DenseNegative_MatchesSystemEnum() => Parity.Check(
        DenseNegativeExtensions.GetValues(), DenseNegativeExtensions.GetNames(), static v => v.GetString(),
        DenseNegativeExtensions.IsDefined, DenseNegativeExtensions.TryParse, DenseNegativeExtensions.TryParse);

    [Fact]
    public void Aliased_MatchesSystemEnum() => Parity.Check(
        AliasedExtensions.GetValues(), AliasedExtensions.GetNames(), static v => v.GetString(),
        AliasedExtensions.IsDefined, AliasedExtensions.TryParse, AliasedExtensions.TryParse, hasAliases: true);

    [Fact]
    public void Bytes_MatchesSystemEnum() => Parity.Check(
        BytesExtensions.GetValues(), BytesExtensions.GetNames(), static v => v.GetString(),
        BytesExtensions.IsDefined, BytesExtensions.TryParse, BytesExtensions.TryParse);

    [Fact]
    public void SBytes_MatchesSystemEnum() => Parity.Check(
        SBytesExtensions.GetValues(), SBytesExtensions.GetNames(), static v => v.GetString(),
        SBytesExtensions.IsDefined, SBytesExtensions.TryParse, SBytesExtensions.TryParse);

    [Fact]
    public void Shorts_MatchesSystemEnum() => Parity.Check(
        ShortsExtensions.GetValues(), ShortsExtensions.GetNames(), static v => v.GetString(),
        ShortsExtensions.IsDefined, ShortsExtensions.TryParse, ShortsExtensions.TryParse);

    [Fact]
    public void UShorts_MatchesSystemEnum() => Parity.Check(
        UShortsExtensions.GetValues(), UShortsExtensions.GetNames(), static v => v.GetString(),
        UShortsExtensions.IsDefined, UShortsExtensions.TryParse, UShortsExtensions.TryParse);

    [Fact]
    public void UInts_MatchesSystemEnum() => Parity.Check(
        UIntsExtensions.GetValues(), UIntsExtensions.GetNames(), static v => v.GetString(),
        UIntsExtensions.IsDefined, UIntsExtensions.TryParse, UIntsExtensions.TryParse);

    [Fact]
    public void Longs_MatchesSystemEnum() => Parity.Check(
        LongsExtensions.GetValues(), LongsExtensions.GetNames(), static v => v.GetString(),
        LongsExtensions.IsDefined, LongsExtensions.TryParse, LongsExtensions.TryParse);

    [Fact]
    public void LongsDense_MatchesSystemEnum() => Parity.Check(
        LongsDenseExtensions.GetValues(), LongsDenseExtensions.GetNames(), static v => v.GetString(),
        LongsDenseExtensions.IsDefined, LongsDenseExtensions.TryParse, LongsDenseExtensions.TryParse);

    [Fact]
    public void ULongs_MatchesSystemEnum() => Parity.Check(
        ULongsExtensions.GetValues(), ULongsExtensions.GetNames(), static v => v.GetString(),
        ULongsExtensions.IsDefined, ULongsExtensions.TryParse, ULongsExtensions.TryParse);

    [Fact]
    public void Permissions_MatchesSystemEnum() => Parity.Check(
        PermissionsExtensions.GetValues(), PermissionsExtensions.GetNames(), static v => v.GetString(),
        PermissionsExtensions.IsDefined, PermissionsExtensions.TryParse, PermissionsExtensions.TryParse);

    [Fact]
    public void Toppings_MatchesSystemEnum() => Parity.Check(
        ToppingsExtensions.GetValues(), ToppingsExtensions.GetNames(), static v => v.GetString(),
        ToppingsExtensions.IsDefined, ToppingsExtensions.TryParse, ToppingsExtensions.TryParse);

    [Fact]
    public void WideFlags_MatchesSystemEnum() => Parity.Check(
        WideFlagsExtensions.GetValues(), WideFlagsExtensions.GetNames(), static v => v.GetString(),
        WideFlagsExtensions.IsDefined, WideFlagsExtensions.TryParse, WideFlagsExtensions.TryParse);

    [Fact]
    public void SignedFlags_MatchesSystemEnum() => Parity.Check(
        SignedFlagsExtensions.GetValues(), SignedFlagsExtensions.GetNames(), static v => v.GetString(),
        SignedFlagsExtensions.IsDefined, SignedFlagsExtensions.TryParse, SignedFlagsExtensions.TryParse);

    [Fact]
    public void LongFlags_MatchesSystemEnum() => Parity.Check(
        LongFlagsExtensions.GetValues(), LongFlagsExtensions.GetNames(), static v => v.GetString(),
        LongFlagsExtensions.IsDefined, LongFlagsExtensions.TryParse, LongFlagsExtensions.TryParse);

    [Fact]
    public void Empty_MatchesSystemEnum() => Parity.Check(
        EmptyExtensions.GetValues(), EmptyExtensions.GetNames(), static v => v.GetString(),
        EmptyExtensions.IsDefined, EmptyExtensions.TryParse, EmptyExtensions.TryParse);

    [Fact]
    public void Keywords_MatchesSystemEnum() => Parity.Check(
        KeywordsExtensions.GetValues(), KeywordsExtensions.GetNames(), static v => v.GetString(),
        KeywordsExtensions.IsDefined, KeywordsExtensions.TryParse, KeywordsExtensions.TryParse);

    [Fact]
    public void Tricky_MatchesSystemEnum() => Parity.Check(
        TrickyExtensions.GetValues(), TrickyExtensions.GetNames(), static v => v.GetString(),
        TrickyExtensions.IsDefined, TrickyExtensions.TryParse, TrickyExtensions.TryParse);

    [Fact]
    public void SameLength_MatchesSystemEnum() => Parity.Check(
        SameLengthExtensions.GetValues(), SameLengthExtensions.GetNames(), static v => v.GetString(),
        SameLengthExtensions.IsDefined, SameLengthExtensions.TryParse, SameLengthExtensions.TryParse);

    [Fact]
    public void CaseVariants_MatchesSystemEnum() => Parity.Check(
        CaseVariantsExtensions.GetValues(), CaseVariantsExtensions.GetNames(), static v => v.GetString(),
        CaseVariantsExtensions.IsDefined, CaseVariantsExtensions.TryParse, CaseVariantsExtensions.TryParse,
        ignoreCaseIsAmbiguous: true);

    [Fact]
    public void Nested_MatchesSystemEnum()
    {
        Parity.Check(
            OuterInnerExtensions.GetValues(), OuterInnerExtensions.GetNames(), static v => v.GetString(),
            OuterInnerExtensions.IsDefined, OuterInnerExtensions.TryParse, OuterInnerExtensions.TryParse);
        Parity.Check(
            OuterMiddleDeepExtensions.GetValues(), OuterMiddleDeepExtensions.GetNames(), static v => v.GetString(),
            OuterMiddleDeepExtensions.IsDefined, OuterMiddleDeepExtensions.TryParse, OuterMiddleDeepExtensions.TryParse);
    }

    [Fact]
    public void Internal_MatchesSystemEnum() => Parity.Check(
        InternalEnumExtensions.GetValues(), InternalEnumExtensions.GetNames(), static v => v.GetString(),
        InternalEnumExtensions.IsDefined, InternalEnumExtensions.TryParse, InternalEnumExtensions.TryParse);

    [Fact]
    public void Renamed_MatchesSystemEnum() => Parity.Check(
        RenamedHelpers.GetValues(), RenamedHelpers.GetNames(), static v => v.GetString(),
        RenamedHelpers.IsDefined, RenamedHelpers.TryParse, RenamedHelpers.TryParse);

    [Fact]
    public void Obsolete_MatchesSystemEnum() => Parity.Check(
        WithObsoleteExtensions.GetValues(), WithObsoleteExtensions.GetNames(), static v => v.GetString(),
        WithObsoleteExtensions.IsDefined, WithObsoleteExtensions.TryParse, WithObsoleteExtensions.TryParse);

    [Fact]
    public void Collisions_MatchSystemEnum()
    {
        Parity.Check(
            Collisions.TypeExtensions.GetValues(), Collisions.TypeExtensions.GetNames(), Collisions.TypeExtensions.GetString,
            Collisions.TypeExtensions.IsDefined, Collisions.TypeExtensions.TryParse, Collisions.TypeExtensions.TryParse);
        Parity.Check(
            Collisions.JsonExceptionExtensions.GetValues(), Collisions.JsonExceptionExtensions.GetNames(), Collisions.JsonExceptionExtensions.GetString,
            Collisions.JsonExceptionExtensions.IsDefined, Collisions.JsonExceptionExtensions.TryParse, Collisions.JsonExceptionExtensions.TryParse);
        Parity.Check(
            GlobalNamespaceEnumExtensions.GetValues(), GlobalNamespaceEnumExtensions.GetNames(), static v => v.GetString(),
            GlobalNamespaceEnumExtensions.IsDefined, GlobalNamespaceEnumExtensions.TryParse, GlobalNamespaceEnumExtensions.TryParse);
    }
}
