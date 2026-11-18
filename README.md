# Seiori.EnumGenerator

A C# source generator that turns enums into fast, allocation-free code at compile time: `ToString`, `IsDefined`,
`Parse`/`TryParse`, display and serialized names, and a reflection-free, AOT-safe `System.Text.Json` converter.

```bash
dotnet add package Seiori.EnumGenerator
```

The package is compile-time only (`developmentDependency`); nothing is added to your output or to your own package's dependencies.

## Quick start

Mark the enums you want with `[EnumExtensions]`, and optionally name individual members:

```csharp
using Seiori.EnumGenerator;

[EnumExtensions]
public enum Status
{
    [EnumDisplay("Ready to Start")]                       // name shown to people
    [EnumDescription("Created, but nobody has begun.")]   // what it means
    [EnumJson("pending_status")]                          // name in JSON
    Pending,

    [EnumDisplay("In Progress")]
    Active,

    Completed,
}
```

A static class `StatusExtensions` is generated next to it:

```csharp
string name    = Status.Active.GetString();            // "Active"          (replaces ToString)
string display = Status.Pending.GetDisplayValue();     // "Ready to Start"
string about   = Status.Pending.GetDescription();      // "Created, but nobody has begun."
string wire    = Status.Pending.GetEnumMemberValue();  // "pending_status"

bool ok = StatusExtensions.TryParse("Active", out Status s);               // case-sensitive
ok = StatusExtensions.TryParse("active", ignoreCase: true, out s);
ok = StatusExtensions.TryParseEnumMemberValue("pending_status", out s);    // by serialized name
bool defined = StatusExtensions.IsDefined((Status)42);                     // false

var options = new JsonSerializerOptions { Converters = { StatusExtensions.JsonConverter } };
JsonSerializer.Serialize(Status.Pending, options);                         // "\"pending_status\""
```

## Naming members

A member has three pieces of text you can set, each with one attribute and one method. All are optional; a member without one uses its own name.

| What | Attribute | Read it with |
| :--- | :--- | :--- |
| The name shown to people | `[EnumDisplay("...")]` | `GetDisplayValue()` |
| What it is or does | `[EnumDescription("...")]` | `GetDescription()` |
| The name in JSON | `[EnumJson("...")]` | `GetEnumMemberValue()`, `TryParseEnumMemberValue(...)`, and the JSON converter in both directions |

The three are independent: setting one never changes another, and none of them changes `GetString()`, which is always the member name.

These attributes work on every target framework, need no extra references, and leave nothing in your compiled assembly.

### Also understood

Enums already annotated for other libraries work unchanged. Each standard attribute feeds exactly one of the three:

| Standard attribute | Treated as |
| :--- | :--- |
| `[Display(Name = "...")]` | The name shown to people |
| `[Display(Description = "...")]`, `[Description("...")]` | What it is or does |
| `[JsonStringEnumMemberName("...")]`, `[EnumMember(Value = "...")]`, `[JsonPropertyName("...")]` | The name in JSON |

When a member has both, the package's own attribute wins; within a row, the order shown is the priority. Keep a standard attribute when something other than this package's generated code also reads the enum: the built-in `JsonStringEnumConverter` only knows `[JsonStringEnumMemberName]`, and UI frameworks only know `[Display]`.

## Generated API

For an enum `MyEnum`, `MyEnumExtensions` contains:

| Member | Description |
| :--- | :--- |
| `Length` | `const int`: the number of members, including aliases. |
| `GetValues()` / `GetNames()` | All members/names in the same order as `Enum.GetValues`/`GetNames`. Returns a `ReadOnlySpan<T>` (no allocation). |
| `IsDefined(MyEnum)` | Same result as `Enum.IsDefined`. Compiles to a single range compare or bitmask test when the values allow it. |
| `IsDefined(string)` / `IsDefined(ReadOnlySpan<char>)` | Whether the text is a member name (case-sensitive). |
| `value.GetString()` | The member name; same result as `ToString()`. |
| `value.GetDisplayValue()` | The name shown to people, or the member name. See [Naming members](#naming-members). |
| `value.GetDescription()` | What the member is or does, or the member name. |
| `value.GetEnumMemberValue()` | The name in JSON, or the member name. |
| `value.GetStringUpperCase()` / `GetStringLowerCase()` | The member name in upper or lower case (invariant), precomputed. |
| `value.GetUnderlyingValue()` | The value as its underlying type (`int`, `byte`, …). |
| `value.HasFlagFast(flag)` | `[Flags]` enums only. Never boxes. |
| `TryParse(string or ReadOnlySpan<char>, [bool ignoreCase,] out MyEnum)` | Parses a member name. Case-sensitive unless `ignoreCase` is set. |
| `Parse(string or ReadOnlySpan<char>, [bool ignoreCase])` | Like `TryParse`, but throws `ArgumentException` when the name isn't found. |
| `TryParseEnumMemberValue(string or ReadOnlySpan<char>, [bool ignoreCase,] out MyEnum)` | Parses the serialized name (the `GetEnumMemberValue` text). |
| `TryParseEnumMemberValueUtf8(ReadOnlySpan<byte>, out MyEnum)` | Parses a UTF-8 serialized name, with no transcoding. |
| `JsonConverter` / `MyEnumJsonConverter` | A cached `System.Text.Json` converter. |

Undefined values fall back exactly as `ToString()` does: the number for normal enums, and `"A, B"` for `[Flags]` combinations.

Combinations of a `[Flags]` enum are remembered: the first `GetString()` for `Read | Execute` formats it, and every later call returns that same string from a table without allocating. This applies when all of the enum's values fit in one byte (up to eight flags, a table of at most 256 entries per text method). Larger or negative flag sets are formatted by `Enum.ToString()` on every call.

## System.Text.Json

The converter needs no reflection and works with Native AOT and trimming. You can register it once:

```csharp
options.Converters.Add(StatusExtensions.JsonConverter);
```

or attach it to the enum:

```csharp
[EnumExtensions]
[JsonConverter(typeof(StatusExtensions.StatusJsonConverter))]
public enum Status { ... }
```

- **Writing** uses pre-encoded `JsonEncodedText`, so nothing is escaped or transcoded per call. Undefined values (including `[Flags]` combinations) are written as numbers, so they round-trip.
- **Reading** first matches the raw UTF-8 bytes against the serialized names, with no allocation. If that fails, it falls back to case-insensitive serialized names, then case-insensitive member names, then integers. Escaped JSON strings (`"\u0041"`) are supported. The fallback doesn't allocate either, so camelCase or lower-case payloads produce no garbage: when every name is ASCII, differently-cased text is matched directly on its UTF-8 bytes, and anything else (escapes, non-ASCII names) is unescaped into a stack buffer with System.Text.Json 7 or later.
- **Dictionary keys** (`Dictionary<MyEnum, T>`) use the same names.
- Pre-encoded output uses the default `JavaScriptEncoder`, as `JsonStringEnumConverter` does. A custom `JsonSerializerOptions.Encoder` does not apply to enum values.

## Configuration

```csharp
[EnumExtensions(ExtensionClassName = "StatusHelpers", ExtensionClassNamespace = "MyApp.Helpers")]
public enum Status { ... }
```

- The default class name is `{Enum}Extensions`. For nested enums it is prefixed with the containing types (`Outer.Inner` becomes `OuterInnerExtensions`), so two nested enums with the same name can't collide.
- The generated class is `public` or `internal` to match the enum, and is declared `partial`, so you can add your own members to it.
- `[EnumExtensions]`, `[EnumDisplay]`, `[EnumDescription]` and `[EnumJson]` are `[Conditional("SEIORI_ENUMGENERATOR_USAGES")]`, so they don't appear in your compiled assembly.
- If two projects share internals (`InternalsVisibleTo`) and both use the generator, the compiler reports `CS0436`. Define `SEIORI_ENUMGENERATOR_EXCLUDE_ATTRIBUTE` in the downstream project to fix it.

### Diagnostics

| ID | Meaning |
| :--- | :--- |
| `SEG001` | The enum, or a type that contains it, is `private`, `protected` or `file`-local, so a top-level class can't reach it. |
| `SEG002` | The enum is nested in a generic type. |
| `SEG003` | `ExtensionClassName` is not a valid identifier. |
| `SEG004` | `ExtensionClassNamespace` is not a valid namespace. |

## Compatibility

- **Compiler:** Roslyn 4.3.1 or later (.NET 6.0.400 SDK, Visual Studio 2022 17.3, or any current Rider).
- **Language:** C# 7.3 or later. The output adapts: nullable annotations are only emitted on C# 8+.
- **Frameworks:**
  - Any target with `System.Memory` (built into .NET Core 2.1+) gets the span-based, allocation-free API.
  - Plain `netstandard2.0` or .NET Framework targets get `string` overloads, and `GetValues`/`GetNames` return copies.
  - The JSON converter is generated only when `System.Text.Json` is referenced.

## Performance

Results from `EnumGenerator.Benchmarks` (BenchmarkDotNet, .NET 11, x64):

| Operation | `System.Enum` / built-in | Generated | Allocated (built-in → generated) |
| :--- | ---: | ---: | :--- |
| `ToString()` → `GetString()` | 11.3 ns | 0.2 ns | 24 B → **0 B** |
| The same, with a different value on every call | 12.9 ns | 0.4 ns | 24 B → **0 B** |
| `[Flags]` combination, `ToString()` → `GetString()` | 20.0 ns | 0.4 ns | 63 B → **0 B** |
| `Enum.IsDefined` (contiguous) | 3.1 ns | 0.2 ns | 0 B → 0 B |
| `Enum.IsDefined` (sparse) | 2.9 ns | 0.2 ns | 0 B → 0 B |
| `Enum.TryParse` (hit) | 9.3 ns | 2.7 ns | 0 B → 0 B |
| `Enum.TryParse` (ignore case) | 13.2 ns | 2.8 ns | 0 B → 0 B |
| `Enum.TryParse` (miss) | 41.6 ns | 2.7 ns | 0 B → 0 B |
| `Enum.GetValues<T>()` | 18.7 ns | ~0 ns | 72 B → **0 B** |
| JSON deserialize (record with 6 enum values) | 500 ns | 410 ns | equal |
| JSON deserialize, lower-case enum values | 488 ns | 434 ns | equal |
| JSON serialize (record with 6 enum values) | 188 ns | 204 ns | equal |

Sub-nanosecond results mean the call was inlined to a few instructions; they are at the limit of what the benchmark can measure.

For serialization, the built-in `JsonStringEnumConverter` is slightly faster, by about 3 ns per value. System.Text.Json validates the writer after every converter it doesn't ship itself, and the built-in one is exempt. The generated converter is faster to read, whether the text matches a serialized name exactly or only ignoring case; it is reflection-free and safe for Native AOT and trimming. The three JSON rows are the mean of two runs; they vary by a few percent from run to run.

How the speed is achieved:

- **`IsDefined`:** contiguous values compile to one unsigned compare (`(uint)(v - min) <= range`). Values within 64 of each other use a single bitmask test. Only widely spread values fall back to a `switch`.
- **Parsing:** a `switch` on length, then on the character position that best separates the remaining names, followed by one vectorized comparison. A miss usually costs a single jump.
- **Strings:** when the values are dense, each method is a bounds check and a load from a `string[]` indexed by `value - min`, small enough to be inlined into the caller. A `switch` compiles to a jump table, which the CPU mispredicts whenever consecutive values differ; the table costs the same for every input. Widely spread values still use a `switch` of literals. Either way there is no reflection and no allocation. For a small `[Flags]` enum the table has a slot for every combination, filled in the first time that combination is formatted.
- **JSON:** UTF-8 names are stored as `ReadOnlySpan<byte>` data in the assembly, and output text is pre-encoded and looked up the same way as strings. Text that differs only by case is compared a machine word at a time against constants, with the case bit set in the letter positions. The converter instance is created on first use, so enums that never touch JSON never load it.
- **Code size:** methods that would have identical bodies forward to one implementation. An enum without `[Display]`/`[Description]` or serialized-name attributes gets one name lookup and one set of parsers instead of several copies: less source for your compiler, less IL in your assembly, less code to JIT.
- **Compile time:** the generator uses `ForAttributeWithMetadataName` and fully value-equatable models, so the IDE skips unrelated files and unrelated edits produce no output. Tests check that this caching works.

Run them yourself:

```bash
dotnet run -c Release --project EnumGenerator.Benchmarks -- --filter "*"
```

## Differences from `System.Enum`

- `TryParse`/`Parse` accept member names only. They don't trim whitespace, parse numeric strings, or parse comma-separated flags.
- With `ignoreCase`, names that differ only by case (`Value`/`VALUE`) resolve to the first one declared.
- For aliased values (`None = 0, Default = 0`), `GetString` returns the first member declared.

## Migrating from 1.x

- Generation is now **opt-in**: add `[EnumExtensions]` (namespace `Seiori.EnumGenerator`) to each enum.
- `TryParse` now matches **member names**, case-sensitively by default, like `Enum.TryParse`. To parse serialized names (the old behavior), use `TryParseEnumMemberValue(value, ignoreCase: true, out result)`.
- The converter type moved inside the extension class: `StatusJsonConverter` is now `StatusExtensions.StatusJsonConverter`. `StatusExtensions.JsonConverter` is unchanged.
- `GetEnumMemberValue` now also reads `[EnumMember(Value)]` and `[JsonStringEnumMemberName]`.
- **`[Description]` is no longer a display name.** In 1.x `GetDisplayValue()` returned the `[Description]` text when there was no `[Display(Name)]`. It now returns the member name, and the text is available from the new `GetDescription()`. If you used `[Description]` as a display name, either call `GetDescription()` instead or change the attribute to `[EnumDisplay("...")]`.
- New: `[EnumDisplay]`, `[EnumDescription]` and `[EnumJson]` set a member's display name, description and JSON name. The standard attributes keep working; see [Naming members](#naming-members).

## Building

```bash
dotnet build EnumGenerator.slnx
dotnet test --project EnumGenerator.Tests
```

To check the package itself, pack it and run the smoke test, which installs the packed `.nupkg` into a separate project. CI does this on every push, and the publish workflow does it before pushing to NuGet.

```bash
dotnet pack EnumGenerator/EnumGenerator.csproj -c Release -o ./nupkg
dotnet run -c Release --project EnumGenerator.PackageSmokeTest
```

The smoke test installs the highest version it finds in `./nupkg`, so empty that folder first if it holds older packages.

The test suite covers:
- Runtime parity with `System.Enum` for every underlying type, aliases, flags, and sparse and extreme values.
- JSON behavior.
- Zero-allocation guarantees.
- Compiling the generated code for 23 hazardous enum shapes under net10/C# latest, net10/C# 7.3 and netstandard2.0/C# 7.3, with overflow checking and XML-doc validation on.
- Incremental-cache behavior.

## License

[MIT](LICENSE). Code the generator writes into your project is yours, with no conditions attached.
