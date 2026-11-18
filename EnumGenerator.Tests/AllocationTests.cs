using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using EnumGenerator.Tests.Enums;
using Xunit;

namespace EnumGenerator.Tests;

/// <summary>The hot paths must never touch the GC heap.</summary>
public class AllocationTests
{
    private static readonly byte[] JsonPending = "\"pending_status\""u8.ToArray();
    private static readonly JsonSerializerOptions Options = new();

    [Fact]
    public void StringConversion_DoesNotAllocate() => AssertNoAllocation(static () =>
    {
        Consume(Status.OnHold.GetString());
        Consume(Status.OnHold.GetDisplayValue());
        Consume(Status.Active.GetDescription());
        Consume(Status.OnHold.GetEnumMemberValue());
        Consume(Status.OnHold.GetStringUpperCase());
        Consume(Status.OnHold.GetStringLowerCase());
    });

    [Fact]
    public void FlagCombinations_DoNotAllocateAfterTheFirstCall() => AssertNoAllocation(static () =>
    {
        // None of these is a declared member: each is formatted once (during warm-up) and then read from a table.
        Consume((Permissions.Read | Permissions.Execute).GetString());
        Consume((Permissions.Write | Permissions.Execute).GetString());
        Consume((Permissions.Read | Permissions.Execute).GetStringUpperCase());
        Consume((Permissions.Read | Permissions.Execute).GetStringLowerCase());
        Consume((Permissions.Read | Permissions.Execute).GetDisplayValue());
        Consume((Toppings.Cheese | Toppings.Pepper).GetString());
        Consume((Toppings.Cheese | Toppings.Pepper).GetDisplayValue());
        Consume(((Toppings)0).GetString());
        Consume((LongFlags.A | LongFlags.D).GetString());
    });

    [Fact]
    public void Lookups_DoNotAllocate() => AssertNoAllocation(static () =>
    {
        Consume(StatusExtensions.IsDefined(Status.Completed));
        Consume(SparseExtensions.IsDefined(Sparse.Fifty));
        Consume(VerySparseExtensions.IsDefined(VerySparse.C));
        Consume(StatusExtensions.GetValues().Length);
        Consume(StatusExtensions.GetNames().Length);
        Consume(Permissions.All.HasFlagFast(Permissions.Read));
    });

    [Fact]
    public void Parsing_DoesNotAllocate() => AssertNoAllocation(static () =>
    {
        Consume(StatusExtensions.TryParse("Completed", out var a));
        Consume(StatusExtensions.TryParse("completed", ignoreCase: true, out a));
        Consume(StatusExtensions.TryParse("Missing".AsSpan(), out a));
        Consume(StatusExtensions.TryParseEnumMemberValue("on-hold", out a));
        Consume(StatusExtensions.TryParseEnumMemberValue("ON-HOLD", ignoreCase: true, out a));
        Consume(StatusExtensions.TryParseEnumMemberValueUtf8("pending_status"u8, out a));
        Consume(StatusExtensions.Parse("Active".AsSpan()));
        Consume(a);
    });

    [Fact]
    public void JsonConverter_FastPaths_DoNotAllocate()
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using var writer = new Utf8JsonWriter(buffer);
        var converter = StatusExtensions.JsonConverter;

        AssertNoAllocation(() =>
        {
            var reader = new Utf8JsonReader(JsonPending);
            reader.Read();
            Consume(converter.Read(ref reader, typeof(Status), Options));

            buffer.ResetWrittenCount();
            writer.Reset();
            converter.Write(writer, Status.Pending, Options);
            writer.Flush();
        });

        Assert.Equal(JsonPending, buffer.WrittenSpan.ToArray());
    }

    [Fact]
    public void JsonConverter_LenientRead_DoesNotAllocate()
    {
        // Text that misses the exact UTF-8 match: different case (serialized name, then member name) and escapes.
        var escaped = "\"on\\u002Dhold\""u8.ToArray();
        byte[][] inputs = ["\"PENDING_STATUS\""u8.ToArray(), "\"archived\""u8.ToArray(), escaped];
        var converter = StatusExtensions.JsonConverter;

        AssertNoAllocation(() =>
        {
            foreach (var json in inputs)
            {
                var reader = new Utf8JsonReader(json);
                reader.Read();
                Consume(converter.Read(ref reader, typeof(Status), Options));
            }
        });

        var check = new Utf8JsonReader(escaped);
        check.Read();
        Assert.Equal(Status.OnHold, converter.Read(ref check, typeof(Status), Options));
    }

    private static void AssertNoAllocation(Action action)
    {
        // Warm up: static constructors, tiered JIT and lazy runtime state allocate once.
        for (var i = 0; i < 200; i++) action();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1_000; i++) action();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    /// <summary>Keeps results observable without boxing (GC.KeepAlive(object) would box and allocate).</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Consume<T>(T value) => Sink<T>.Value = value;

    private static class Sink<T>
    {
        public static T? Value;
    }
}
