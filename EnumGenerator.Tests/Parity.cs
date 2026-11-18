using System.Runtime.CompilerServices;
using Xunit;

namespace EnumGenerator.Tests;

public delegate bool TryParseSpan<T>(ReadOnlySpan<char> value, out T result);

public delegate bool TryParseString<T>(string? value, bool ignoreCase, out T result);

/// <summary>
/// Checks generated members against the reflection-based <see cref="Enum"/> APIs they replace, over every
/// declared value plus neighbours, underlying-type extremes and a band of small numbers.
/// </summary>
internal static class Parity
{
    public static void Check<T>(
        ReadOnlySpan<T> values,
        ReadOnlySpan<string> names,
        Func<T, string> getString,
        Func<T, bool> isDefined,
        TryParseSpan<T> tryParse,
        TryParseString<T> tryParseIgnoreCase,
        bool hasAliases = false,
        bool ignoreCaseIsAmbiguous = false)
        where T : struct, Enum
    {
        var expectedNames = Enum.GetNames<T>();
        Assert.Equal(Enum.GetValues<T>(), values.ToArray());
        if (hasAliases) Assert.Equal(expectedNames.Order(), names.ToArray().Order());
        else Assert.Equal(expectedNames, names.ToArray());

        foreach (var probe in Probes<T>())
        {
            Assert.Equal(Enum.IsDefined(probe), isDefined(probe));

            var actual = getString(probe);
            if (hasAliases && Enum.IsDefined(probe))
            {
                // Enum.ToString picks an arbitrary alias; any name with this value is correct.
                Assert.Equal(probe, Enum.Parse<T>(actual));
            }
            else
            {
                Assert.Equal(probe.ToString(), actual);
            }
        }

        foreach (var name in expectedNames)
        {
            Assert.True(tryParse(name, out var parsed), name);
            Assert.Equal(Enum.Parse<T>(name), parsed);
            Assert.True(tryParseIgnoreCase(name, false, out parsed), name);
            Assert.Equal(Enum.Parse<T>(name), parsed);

            foreach (var variant in new[] { name.ToUpperInvariant(), name.ToLowerInvariant() })
            {
                Assert.True(tryParseIgnoreCase(variant, true, out parsed), variant);
                if (!ignoreCaseIsAmbiguous) Assert.Equal(Enum.Parse<T>(variant, ignoreCase: true), parsed);
                Assert.Equal(expectedNames.Contains(variant), tryParse(variant, out _));
            }

            foreach (var bad in new[] { name + "x", "x" + name, name.Length > 1 ? name[..^1] : "#", " " + name, name + " " })
            {
                if (expectedNames.Contains(bad, StringComparer.OrdinalIgnoreCase)) continue;
                Assert.False(tryParse(bad, out _), bad);
                Assert.False(tryParseIgnoreCase(bad, true, out _), bad);
            }
        }

        Assert.False(tryParse(ReadOnlySpan<char>.Empty, out _));
        Assert.False(tryParseIgnoreCase(null, true, out _));
        Assert.False(tryParseIgnoreCase(null, false, out _));
    }

    private static IEnumerable<T> Probes<T>() where T : struct, Enum
    {
        var size = Unsafe.SizeOf<T>();
        var signed = Type.GetTypeCode(typeof(T)) is TypeCode.SByte or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64;
        var bits = new HashSet<ulong>();

        for (long i = -70; i <= 70; i++) bits.Add(unchecked((ulong)i));

        var maxUnsigned = size == 8 ? ulong.MaxValue : (1UL << (size * 8)) - 1;
        bits.Add(maxUnsigned);
        bits.Add(signed ? maxUnsigned >> 1 : maxUnsigned - 1);
        bits.Add(signed ? (maxUnsigned >> 1) + 1 : 1);

        foreach (var value in Enum.GetValues<T>())
        {
            var b = ToBits(value);
            bits.Add(b);
            bits.Add(unchecked(b - 1));
            bits.Add(unchecked(b + 1));
        }

        return bits.Select(FromBits<T>).Distinct().ToArray();
    }

    private static ulong ToBits<T>(T value) where T : struct, Enum => Unsafe.SizeOf<T>() switch
    {
        1 => Unsafe.As<T, byte>(ref value),
        2 => Unsafe.As<T, ushort>(ref value),
        4 => Unsafe.As<T, uint>(ref value),
        _ => Unsafe.As<T, ulong>(ref value),
    };

    private static T FromBits<T>(ulong bits) where T : struct, Enum
    {
        switch (Unsafe.SizeOf<T>())
        {
            case 1:
                var b = unchecked((byte)bits);
                return Unsafe.As<byte, T>(ref b);
            case 2:
                var s = unchecked((ushort)bits);
                return Unsafe.As<ushort, T>(ref s);
            case 4:
                var i = unchecked((uint)bits);
                return Unsafe.As<uint, T>(ref i);
            default:
                return Unsafe.As<ulong, T>(ref bits);
        }
    }
}
