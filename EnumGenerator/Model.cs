using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Seiori.EnumGenerator;

/// <summary>
/// Value-equatable array wrapper. <see cref="System.Collections.Immutable.ImmutableArray{T}"/> compares by
/// reference, which defeats incremental-generator caching; this compares element-wise.
/// </summary>
internal readonly struct EquatableArray<T>(T[] array) : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
    where T : IEquatable<T>
{
    private readonly T[]? _array = array;

    public int Count => _array?.Length ?? 0;

    public T this[int index] => _array![index];

    public bool Equals(EquatableArray<T> other)
    {
        var a = _array ?? [];
        var b = other._array ?? [];
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(a[i], b[i])) return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        if (_array is null) return 0;
        var hash = 17;
        foreach (var item in _array)
        {
            hash = unchecked(hash * 31 + EqualityComparer<T>.Default.GetHashCode(item));
        }

        return hash;
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(_array ?? [])).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);

    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);
}

/// <summary>Compilation-wide facts that decide which APIs can be emitted.</summary>
internal readonly record struct GenerationOptions(
    bool HasSpan,
    bool HasSystemTextJson,
    bool HasJsonPropertyNameSupport,
    bool HasValueIsEscaped,
    bool HasCopyString,
    bool HasSpanNumberParsing,
    bool HasNullableAnnotations)
{
    public static GenerationOptions From(Compilation compilation)
    {
        var hasSpan = compilation.GetTypeByMetadataName("System.ReadOnlySpan`1") is not null
                      && compilation.GetTypeByMetadataName("System.MemoryExtensions") is not null;

        var converter = hasSpan ? compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonConverter`1") : null;
        var reader = converter is null ? null : compilation.GetTypeByMetadataName("System.Text.Json.Utf8JsonReader");
        var hasStj = reader is not null
                     && compilation.GetTypeByMetadataName("System.Text.Json.JsonEncodedText") is not null;

        // Utf8JsonReader.CopyString (System.Text.Json 7+) lets the lenient read path unescape into a stack buffer.
        var hasCopyString = hasStj && !reader!.GetMembers("CopyString").IsEmpty;

        return new GenerationOptions(
            HasSpan: hasSpan,
            HasSystemTextJson: hasStj,
            HasJsonPropertyNameSupport: hasStj && !converter!.GetMembers("ReadAsPropertyName").IsEmpty,
            HasValueIsEscaped: hasStj && !reader!.GetMembers("ValueIsEscaped").IsEmpty,
            HasCopyString: hasCopyString,
            HasSpanNumberParsing: hasCopyString && HasSpanTryParse(compilation),
            HasNullableAnnotations: compilation is CSharpCompilation { LanguageVersion: >= LanguageVersion.CSharp8 });
    }

    /// <summary>
    /// Whether <c>int.TryParse(ReadOnlySpan&lt;char&gt;, NumberStyles, IFormatProvider, out int)</c> exists. Every
    /// integral type gained it together (.NET Core 2.1), but System.Text.Json 7+ also runs on frameworks without it.
    /// </summary>
    private static bool HasSpanTryParse(Compilation compilation)
    {
        foreach (var member in compilation.GetSpecialType(SpecialType.System_Int32).GetMembers("TryParse"))
        {
            if (member is IMethodSymbol { Parameters.Length: 4 } method
                && method.Parameters[0].Type is INamedTypeSymbol { Name: "ReadOnlySpan", TypeArguments.Length: 1 } span
                && span.TypeArguments[0].SpecialType == SpecialType.System_Char)
            {
                return true;
            }
        }

        return false;
    }
}

/// <param name="Name">The member's identifier as written (no <c>@</c>), used for string output.</param>
/// <param name="ReferByValue">
/// The member cannot be named in generated code (<c>[Obsolete(..., error: true)]</c>), so the emitter writes it as
/// a cast of its value.
/// </param>
/// <param name="Bits">The member's value, sign-extended to 64 bits.</param>
internal readonly record struct MemberModel(
    string Name,
    bool ReferByValue,
    ulong Bits,
    string? DisplayName,
    string? Description,
    string? SerializedName);

internal sealed record EnumModel(
    string HintName,
    string FullyQualifiedName,
    string DisplayName,
    string Namespace,
    string ClassName,
    bool IsPublic,
    string UnderlyingType,
    bool IsSigned,
    int UnderlyingSize,
    bool IsFlags,
    EquatableArray<MemberModel> Members);

internal sealed record LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
{
    public Location ToLocation() => Location.Create(FilePath, TextSpan, LineSpan);

    public static LocationInfo? From(Location location) =>
        location.SourceTree is null
            ? null
            : new LocationInfo(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);
}

internal sealed record DiagnosticInfo(DiagnosticDescriptor Descriptor, LocationInfo? Location, string Argument)
{
    public Diagnostic ToDiagnostic() => Diagnostic.Create(Descriptor, Location?.ToLocation(), Argument);
}

internal sealed record ParseResult(EnumModel? Model, DiagnosticInfo? Diagnostic);
