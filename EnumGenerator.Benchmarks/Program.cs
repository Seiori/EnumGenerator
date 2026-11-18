using System.Text.Json;
using System.Text.Json.Serialization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Seiori.EnumGenerator;

namespace EnumGenerator.Benchmarks;

// Run:  dotnet run -c Release --project EnumGenerator.Benchmarks -- --filter * --inProcess
//
// Input fields are deliberately not readonly, so the JIT can never treat a benchmark's input as a constant.
public static class Program
{
    public static void Main(string[] args) => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}

[EnumExtensions]
public enum Month
{
    January = 1,
    February,
    March,
    April,
    May,
    June,
    July,
    August,
    September,
    October,
    November,
    December,
}

[EnumExtensions]
public enum Sparse
{
    A = -10,
    B = 0,
    C = 5,
    D = 50,
}

[Flags]
[EnumExtensions]
public enum Access
{
    None = 0,
    Read = 1,
    Write = 2,
    Execute = 4,
    Delete = 8,
}

public sealed record Payload(Month Start, Month End, Month[] Months);

/// <summary>Every combination of four flags, in random order: most are not declared members.</summary>
[MemoryDiagnoser]
public class FlagsBenchmarks
{
    private const int N = 1024;
    private Access[] _values = [];

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(42);
        _values = new Access[N];
        for (var i = 0; i < N; i++) _values[i] = (Access)random.Next(0, 16);
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = N)]
    public int Enum_ToString()
    {
        var total = 0;
        foreach (var value in _values) total += value.ToString().Length;
        return total;
    }

    [Benchmark(OperationsPerInvoke = N)]
    public int GetString()
    {
        var total = 0;
        foreach (var value in _values) total += value.GetString().Length;
        return total;
    }

    [Benchmark(OperationsPerInvoke = N)]
    public int GetStringUpperCase()
    {
        var total = 0;
        foreach (var value in _values) total += value.GetStringUpperCase().Length;
        return total;
    }
}

[MemoryDiagnoser]
public class ToStringBenchmarks
{
    private Month _value = Month.September;

    [Benchmark(Baseline = true)]
    public string Enum_ToString() => _value.ToString();

    [Benchmark]
    public string GetString() => _value.GetString();
}

/// <summary>
/// The same operations over values that change from call to call. A benchmark that passes one value forever lets
/// the branch predictor learn it, which flatters anything built on a jump table.
/// </summary>
[MemoryDiagnoser]
public class VaryingValuesBenchmarks
{
    private const int N = 1024;
    private static readonly JsonSerializerOptions Options = new();
    private readonly System.Buffers.ArrayBufferWriter<byte> _buffer = new(64 * 1024);
    private Utf8JsonWriter _writer = null!;
    private Month[] _months = [];

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(42);
        _months = new Month[N];
        for (var i = 0; i < N; i++) _months[i] = (Month)random.Next(1, 13);
        _writer = new Utf8JsonWriter(_buffer);
        _jsonArray = JsonSerializer.SerializeToUtf8Bytes(_months.Select(static m => m.ToString()));
        _jsonArrayLowerCase = JsonSerializer.SerializeToUtf8Bytes(_months.Select(static m => m.ToString().ToLowerInvariant()));
    }

    private byte[] _jsonArray = [];
    private byte[] _jsonArrayLowerCase = [];

    /// <summary>Tokenizing included: the reader's own cost per string is part of both numbers.</summary>
    [Benchmark(OperationsPerInvoke = N)]
    public int JsonConverter_Read() => ReadAll(_jsonArray);

    [Benchmark(OperationsPerInvoke = N)]
    public int JsonConverter_Read_LowerCase() => ReadAll(_jsonArrayLowerCase);

    private static int ReadAll(byte[] json)
    {
        var converter = MonthExtensions.JsonConverter;
        var reader = new Utf8JsonReader(json);
        var total = 0;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.String) total += (int)converter.Read(ref reader, typeof(Month), Options);
        }

        return total;
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = N)]
    public int Enum_ToString()
    {
        var total = 0;
        foreach (var month in _months) total += month.ToString().Length;
        return total;
    }

    [Benchmark(OperationsPerInvoke = N)]
    public int GetString()
    {
        var total = 0;
        foreach (var month in _months) total += month.GetString().Length;
        return total;
    }

    [Benchmark(OperationsPerInvoke = N)]
    public int GetStringUpperCase()
    {
        var total = 0;
        foreach (var month in _months) total += month.GetStringUpperCase().Length;
        return total;
    }

    [Benchmark(OperationsPerInvoke = N)]
    public int JsonConverter_Write()
    {
        var converter = MonthExtensions.JsonConverter;
        _buffer.ResetWrittenCount();
        _writer.Reset();
        _writer.WriteStartArray();
        foreach (var month in _months) converter.Write(_writer, month, Options);
        _writer.WriteEndArray();
        _writer.Flush();
        return _buffer.WrittenCount;
    }
}

[MemoryDiagnoser]
public class IsDefinedBenchmarks
{
    private Month _month = Month.September;
    private Sparse _sparse = Sparse.C;

    [Benchmark(Baseline = true)]
    public bool Enum_IsDefined_Contiguous() => Enum.IsDefined(_month);

    [Benchmark]
    public bool IsDefined_Contiguous() => MonthExtensions.IsDefined(_month);

    [Benchmark]
    public bool Enum_IsDefined_Sparse() => Enum.IsDefined(_sparse);

    [Benchmark]
    public bool IsDefined_Sparse() => SparseExtensions.IsDefined(_sparse);
}

[MemoryDiagnoser]
public class ParseBenchmarks
{
    private string _name = "September";
    private string _lower = "september";
    private string _missing = "Septembre";

    [Benchmark(Baseline = true)]
    public Month Enum_TryParse() => Enum.TryParse(_name, out Month m) ? m : default;

    [Benchmark]
    public Month TryParse() => MonthExtensions.TryParse(_name, out var m) ? m : default;

    [Benchmark]
    public Month Enum_TryParse_IgnoreCase() => Enum.TryParse(_lower, true, out Month m) ? m : default;

    [Benchmark]
    public Month TryParse_IgnoreCase() => MonthExtensions.TryParse(_lower, true, out var m) ? m : default;

    [Benchmark]
    public bool Enum_TryParse_Miss() => Enum.TryParse(_missing, out Month _);

    [Benchmark]
    public bool TryParse_Miss() => MonthExtensions.TryParse(_missing, out _);
}

[MemoryDiagnoser]
public class GetValuesBenchmarks
{
    [Benchmark(Baseline = true)]
    public int Enum_GetValues() => Enum.GetValues<Month>().Length;

    [Benchmark]
    public int GetValues() => MonthExtensions.GetValues().Length;
}

[MemoryDiagnoser]
public class JsonBenchmarks
{
    private static readonly JsonSerializerOptions BuiltIn = new() { Converters = { new JsonStringEnumConverter() } };
    private static readonly JsonSerializerOptions Generated = new() { Converters = { MonthExtensions.JsonConverter } };

    private readonly Payload _payload = new(Month.March, Month.November, [Month.January, Month.June, Month.July, Month.December]);
    private byte[] _json = [];
    private byte[] _jsonLowerCase = [];

    [GlobalSetup]
    public void Setup()
    {
        _json = JsonSerializer.SerializeToUtf8Bytes(_payload, BuiltIn);

        // Same payload with lower-case enum values (property names untouched), as a camelCase API would send.
        var text = System.Text.Encoding.UTF8.GetString(_json);
        foreach (var name in Enum.GetNames<Month>()) text = text.Replace("\"" + name + "\"", "\"" + name.ToLowerInvariant() + "\"");
        _jsonLowerCase = System.Text.Encoding.UTF8.GetBytes(text);
    }

    [Benchmark(Baseline = true)]
    public byte[] Serialize_JsonStringEnumConverter() => JsonSerializer.SerializeToUtf8Bytes(_payload, BuiltIn);

    [Benchmark]
    public byte[] Serialize_Generated() => JsonSerializer.SerializeToUtf8Bytes(_payload, Generated);

    [Benchmark]
    public Payload? Deserialize_JsonStringEnumConverter() => JsonSerializer.Deserialize<Payload>(_json, BuiltIn);

    [Benchmark]
    public Payload? Deserialize_Generated() => JsonSerializer.Deserialize<Payload>(_json, Generated);

    [Benchmark]
    public Payload? Deserialize_LowerCase_JsonStringEnumConverter() => JsonSerializer.Deserialize<Payload>(_jsonLowerCase, BuiltIn);

    [Benchmark]
    public Payload? Deserialize_LowerCase_Generated() => JsonSerializer.Deserialize<Payload>(_jsonLowerCase, Generated);
}
