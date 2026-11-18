using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Seiori.EnumGenerator;
using Xunit;

namespace EnumGenerator.Tests.Generator;

public class GeneratorTests
{
    private const string Using = "using Seiori.EnumGenerator;\n";

    /// <summary>Consumer sources that broke 1.x (and other hazards). Value: (source, needs .NET-only attributes).</summary>
    private static readonly Dictionary<string, (string Source, bool NeedsModernBcl)> Cases = new()
    {
        ["internal enum"] = (Using + "namespace App { [EnumExtensions] enum Color { Red, Green } }", false),
        ["aliased values"] = (Using + "namespace App { [EnumExtensions] public enum Level { None = 0, Default = 0, High = 1, Highest = 1 } }", false),
        ["empty enum"] = (Using + "namespace App { [EnumExtensions] public enum Marker { } }", false),
        ["single member"] = (Using + "namespace App { [EnumExtensions] public enum One { Only = 42 } }", false),
        ["nested enums"] = (Using + """
            namespace App
            {
                public class Outer
                {
                    [EnumExtensions] public enum Inner { A }
                    internal class Middle { [EnumExtensions] protected internal enum Deep { B } }
                }
            }
            """, false),
        ["same name in two namespaces"] = (Using + """
            namespace A { [EnumExtensions] public enum Kind { X } }
            namespace B { [EnumExtensions] public enum Kind { Y } }
            """, false),
        ["names differing only by case"] = (Using + "namespace App { [EnumExtensions] public enum Status { A } [EnumExtensions] public enum status { B } }", false),
        ["enums named like BCL types"] = (Using + """
            namespace App
            {
                [EnumExtensions] public enum Type { A }
                [EnumExtensions] public enum String { A }
                [EnumExtensions] public enum MemoryExtensions { A }
                [EnumExtensions] public enum ArgumentException { A }
                [EnumExtensions] public enum CultureInfo { A }
                [EnumExtensions] public enum JsonException { A }
                [EnumExtensions] public enum Utf8JsonReader { A }
            }
            """, false),
        ["user namespace named System"] = (Using + """
            namespace App.System
            {
                [EnumExtensions] public enum Kind { A, B }
                public class Type { }
                public static class MemoryExtensions { }
                public class ArgumentException { }
            }
            """, false),
        ["keyword identifiers"] = (Using + "namespace @class.@event { [EnumExtensions] public enum @int { @void, @class, value, result } }", false),
        ["members named like generated locals"] = (Using + """
            namespace App
            {
                [EnumExtensions]
                public enum E { value, result, number, offset, text, reader, writer, options, typeToConvert, isPropertyName,
                                name, flag, ignoreCase, s_values, s_names, s_text0, Length, JsonConverter, GetString,
                                FormatUndefined, TryParse, Utf8EnumMemberValue0 }
            }
            """, false),
        ["quotes and backslashes"] = (Using + """
            using System.ComponentModel;
            namespace App
            {
                [EnumExtensions]
                public enum Q
                {
                    [Description("Say \"hi\" \\ C:\\temp")] A,
                    [Description("tab\tnewline\nnull\0")] B,
                    [Description(@"verbatim ""quoted"" \n")] C,
                }
            }
            """, false),
        ["unicode separators and surrogates"] = (Using + $$"""
            using System.ComponentModel;
            using System.Text.Json.Serialization;
            namespace App
            {
                [EnumExtensions]
                public enum U
                {
                    [JsonPropertyName("a{{Harness.Escape("2028")}}b{{Harness.Escape("2029")}}c")] A,
                    [JsonPropertyName("{{Harness.Escape("D800")}}")] B,
                    [Description("{{Harness.Escape("FEFF")}}{{Harness.Escape("0085")}}")] C,
                    [JsonPropertyName("{{Harness.Escape("D83D")}}{{Harness.Escape("DE00")}}")] D,
                }
            }
            """, true),
        ["serialized-name attributes"] = (Using + """
            using System.ComponentModel;
            using System.ComponentModel.DataAnnotations;
            using System.Runtime.Serialization;
            using System.Text.Json.Serialization;
            namespace App
            {
                [EnumExtensions]
                public enum S
                {
                    [Display(Name = "Shown")] [EnumMember(Value = "x")] A,
                    [JsonPropertyName("x")] B,
                    [JsonStringEnumMemberName("A")] x,
                    [EnumMember] [Display] [Description(null)] D,
                    [JsonPropertyName("")] E,
                }
            }
            """, true),
        ["own role attributes"] = (Using + """
            using System.ComponentModel;
            namespace App
            {
                [EnumExtensions]
                public enum R
                {
                    [EnumDisplay("Shown")] [EnumDescription("Explained")] [EnumJson("x")] A,
                    [EnumDisplay("say \"hi\"\n")] [EnumDescription("")] [EnumJson("")] B,
                    [EnumDisplay(null)] [EnumDescription(null)] [EnumJson(null)] C,
                    [EnumDescription("wins")] [Description("loses")] D,
                    [EnumJson(Names.E)] E,
                }
                public static class Names { public const string E = "from-a-constant"; }
            }
            """, false),
        ["obsolete members"] = (Using + """
            using System;
            namespace App
            {
                [EnumExtensions]
                public enum E { A, [Obsolete] B, [Obsolete("x", true)] C, [Obsolete("y", false)] D, [Obsolete("z", true)] Alias = C }
                [EnumExtensions]
                public enum L : long { [Obsolete("min", true)] Min = long.MinValue, [Obsolete("max", true)] Max = long.MaxValue }
                [EnumExtensions]
                public enum UL : ulong { [Obsolete("max", true)] Max = ulong.MaxValue }
                [EnumExtensions]
                public enum I : int { [Obsolete("min", true)] Min = int.MinValue }
            }
            """, false),
        ["underlying types at extremes"] = (Using + """
            namespace App
            {
                [EnumExtensions] public enum S8 : sbyte { Min = sbyte.MinValue, Zero = 0, Max = sbyte.MaxValue }
                [EnumExtensions] public enum U8 : byte { Zero = 0, Max = byte.MaxValue }
                [EnumExtensions] public enum S16 : short { Min = short.MinValue, Max = short.MaxValue }
                [EnumExtensions] public enum U16 : ushort { Zero = 0, Max = ushort.MaxValue }
                [EnumExtensions] public enum S32 : int { Min = int.MinValue, Max = int.MaxValue }
                [EnumExtensions] public enum U32 : uint { Zero = 0, Max = uint.MaxValue }
                [EnumExtensions] public enum S64 : long { Min = long.MinValue, Max = long.MaxValue }
                [EnumExtensions] public enum U64 : ulong { Zero = 0, Max = ulong.MaxValue }
            }
            """, false),
        ["dense and sparse layouts"] = (Using + """
            namespace App
            {
                [EnumExtensions] public enum Offset { A = 10, B, C, D }
                [EnumExtensions] public enum MinStart { A = int.MinValue, B, C }
                [EnumExtensions] public enum Bitmask { A = -5, B = 0, C = 58 }
                [EnumExtensions] public enum Switch { A = -5, B = 0, C = 59, D = 100000 }
                [EnumExtensions] public enum LongMask : long { A = long.MinValue, B = long.MinValue + 63 }
                [EnumExtensions] public enum LongDense : long { A = long.MaxValue - 2, B, C }
                [EnumExtensions] public enum ULongTop : ulong { A = ulong.MaxValue - 1, B = ulong.MaxValue }
                [EnumExtensions] public enum UIntWrap : uint { A = 0x7FFFFFFF, B = 0x80000000 }
            }
            """, false),
        ["flags"] = (Using + """
            using System;
            namespace App
            {
                [Flags, EnumExtensions] public enum P { None = 0, A = 1, B = 2, AB = A | B }
                [Flags, EnumExtensions] public enum PL : ulong { None = 0, Top = 1UL << 63 }
                [Flags, EnumExtensions] public enum Gaps : byte { A = 1, B = 2, C = 4, H = 0x80 }
                [Flags, EnumExtensions] public enum GapsLong : long { A = 1, D = 8 }
                [Flags, EnumExtensions] public enum GapsSigned : sbyte { A = 1, D = 8, All = -1 }
                [Flags, EnumExtensions] public enum NineBits { A = 1, I = 0x100 }
            }
            """, false),
        ["many members"] = (Using + "namespace App { [EnumExtensions] public enum Big { "
                            + string.Join(", ", Enumerable.Range(0, 300).Select(i => (i % 3 == 0 ? "Member" : "M") + i.ToString("D3")))
                            + " } }", false),
        ["custom class name and namespace"] = (Using + """
            namespace App
            {
                [EnumExtensions(ExtensionClassName = "ModeHelpers", ExtensionClassNamespace = "Other.Place")]
                public enum Mode { A }
                public static class ModeExtensions { }
                [EnumExtensions(ExtensionClassNamespace = "")]
                public enum ToGlobal { A }
                [EnumExtensions(ExtensionClassName = "class")]
                public enum KeywordClass { A }
            }
            """, false),
        ["global namespace"] = ("[Seiori.EnumGenerator.EnumExtensions] public enum G { A, B }", false),
        ["user partial extension class"] = (Using + """
            namespace App
            {
                [EnumExtensions] public enum Mode { A }
                public static partial class ModeExtensions { public static int Custom(this Mode m) => 1; }
            }
            """, false),
    };

    public static TheoryData<string, Target> CaseMatrix()
    {
        var data = new TheoryData<string, Target>();
        foreach (var (name, (_, modern)) in Cases)
        {
            data.Add(name, Target.Net100Latest);
            data.Add(name, Target.Net100CSharp73);
            if (!modern) data.Add(name, Target.NetStandard20CSharp73);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CaseMatrix))]
    public void GeneratedCode_CompilesCleanly(string caseName, Target target)
    {
        var result = Harness.Run(target, Cases[caseName].Source);

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.True(result.Run.GeneratedTrees.Length > 1, "expected generated extensions");

        // Match by path: the output compilation's trees are not the same instances as the run result's.
        var generatedPaths = result.Run.GeneratedTrees.Select(t => t.FilePath).ToHashSet();
        Assert.Equal(generatedPaths.Count, result.Output.SyntaxTrees.Count(t => generatedPaths.Contains(t.FilePath)));

        var problems = result.Output.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error
                        || (d.Severity == DiagnosticSeverity.Warning
                            && d.Location.SourceTree is { } tree
                            && generatedPaths.Contains(tree.FilePath)))
            .ToList();
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));

        using var stream = new MemoryStream();
        Assert.True(result.Output.Emit(stream).Success);
    }

    [Theory]
    [InlineData("namespace App { public class C { [EnumExtensions] private enum E { A } } }", "SEG001")]
    [InlineData("namespace App { public class C { [EnumExtensions] protected enum E { A } } }", "SEG001")]
    [InlineData("namespace App { public class C { [EnumExtensions] private protected enum E { A } } }", "SEG001")]
    [InlineData("namespace App { internal class C { private class D { [EnumExtensions] public enum E { A } } } }", "SEG001")]
    [InlineData("namespace App { [EnumExtensions] file enum E { A } }", "SEG001")]
    [InlineData("namespace App { public class C<T> { [EnumExtensions] public enum E { A } } }", "SEG002")]
    [InlineData("namespace App { [EnumExtensions(ExtensionClassName = \"1Bad\")] public enum E { A } }", "SEG003")]
    [InlineData("namespace App { [EnumExtensions(ExtensionClassName = \"has space\")] public enum E { A } }", "SEG003")]
    [InlineData("namespace App { [EnumExtensions(ExtensionClassNamespace = \"a..b\")] public enum E { A } }", "SEG004")]
    [InlineData("namespace App { [EnumExtensions(ExtensionClassNamespace = \"a.1b\")] public enum E { A } }", "SEG004")]
    public void Unsupported_ReportsDiagnosticOnTheEnum(string source, string id)
    {
        var fullSource = Using + source;
        var result = Harness.Run(Target.Net100Latest, fullSource);

        var diagnostic = Assert.Single(result.GeneratorDiagnostics);
        Assert.Equal(id, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        var span = diagnostic.Location.SourceSpan;
        Assert.Equal("E", fullSource.Substring(span.Start, span.Length)); // squiggle sits on the enum's name
        Assert.Single(result.Run.GeneratedTrees); // only the marker attribute
    }

    [Fact]
    public void EnumsWithoutTheAttribute_AreIgnored()
    {
        var result = Harness.Run(Target.Net100Latest, "namespace App { public enum E { A } [System.Flags] public enum F { A = 1 } }");

        Assert.Empty(result.GeneratorDiagnostics);
        var tree = Assert.Single(result.Run.GeneratedTrees);
        Assert.EndsWith(AttributeSource.HintName, tree.FilePath, StringComparison.Ordinal);
    }

    [Fact]
    public void Output_AdaptsToConsumerCapabilities()
    {
        const string source = Using + "namespace App { [EnumExtensions] public enum Color { Red, Green } }";

        var modern = Harness.Run(Target.Net100Latest, source).GeneratedFor("App.Color.");
        Assert.Contains("#nullable enable", modern);
        Assert.Contains("ReadOnlySpan<char>", modern);
        Assert.Contains("TryParseEnumMemberValueUtf8", modern);
        Assert.Contains("JsonConverter<global::App.Color>", modern);
        Assert.Contains("ReadAsPropertyName", modern);
        Assert.Contains("reader.ValueIsEscaped", modern);
        Assert.Contains("reader.CopyString(buffer)", modern);
        Assert.Contains("JsonConverter => JsonConverterHolder.Instance;", modern);

        var oldLanguage = Harness.Run(Target.Net100CSharp73, source).GeneratedFor("App.Color.");
        Assert.DoesNotContain("#nullable", oldLanguage);
        Assert.DoesNotContain("string?", oldLanguage);
        Assert.Contains("ReadOnlySpan<char>", oldLanguage);

        var netStandard = Harness.Run(Target.NetStandard20CSharp73, source).GeneratedFor("App.Color.");
        Assert.DoesNotContain("ReadOnlySpan", netStandard);
        Assert.DoesNotContain("System.Text.Json", netStandard);
        Assert.Contains("public static global::App.Color[] GetValues()", netStandard);
    }

    [Fact]
    public void IdenticalMethodBodies_AreForwardedNotDuplicated()
    {
        const string getEnumMemberValue = "GetEnumMemberValue(this global::App.Color value) => GetString(value);";
        const string getDisplayValue = "GetDisplayValue(this global::App.Color value) => GetString(value);";
        const string tryParseSpan = "TryParseEnumMemberValue(global::System.ReadOnlySpan<char> value, out global::App.Color result) => TryParse(value, out result);";

        // No display or serialized names: both are the member name, so nothing is emitted twice.
        var plain = Harness.Run(Target.Net100Latest, Using + "namespace App { [EnumExtensions] public enum Color { Red, Green } }")
            .GeneratedFor("App.Color.");
        Assert.Contains(getEnumMemberValue, plain);
        Assert.Contains(getDisplayValue, plain);
        Assert.Contains("GetDescription(this global::App.Color value) => GetString(value);", plain);
        Assert.Contains(tryParseSpan, plain);
        Assert.DoesNotContain("TryParseEnumMemberValueIgnoreCaseCore", plain);
        Assert.DoesNotContain(".TryParse(chars, true, out result)", plain); // the reader does not try the same names twice

        // A serialized name makes that family distinct again; display names are still the member names.
        var named = Harness.Run(Target.Net100Latest, Using + """
            using System.Text.Json.Serialization;
            namespace App { [EnumExtensions] public enum Color { [JsonStringEnumMemberName("r")] Red, Green } }
            """).GeneratedFor("App.Color.");
        Assert.DoesNotContain(getEnumMemberValue, named);
        Assert.Contains(getDisplayValue, named);
        Assert.DoesNotContain(tryParseSpan, named);
        Assert.Contains("TryParseEnumMemberValueIgnoreCaseCore", named);
        Assert.Contains(".TryParse(chars, true, out result)", named);

        // An attribute that only repeats the member name changes nothing.
        var redundant = Harness.Run(Target.Net100Latest, Using + """
            using System.Text.Json.Serialization;
            namespace App { [EnumExtensions] public enum Color { [JsonStringEnumMemberName("Red")] Red, Green } }
            """).GeneratedFor("App.Color.");
        Assert.Contains(getEnumMemberValue, redundant);
        Assert.Contains(tryParseSpan, redundant);
    }

    [Fact]
    public void JsonConverter_MatchesCaseInsensitivelyOnUtf8_OnlyWhenEveryNameIsAscii()
    {
        const string read = "global::System.Buffers.Binary.BinaryPrimitives.Read";

        var ascii = Harness.Run(Target.Net100Latest, Using + """
            using System.Text.Json.Serialization;
            namespace App { [EnumExtensions] public enum Color { [JsonStringEnumMemberName("r")] Red, Green, [JsonStringEnumMemberName("a_1")] X } }
            """).GeneratedFor("App.Color.");
        Assert.Contains("TryParseLenientUtf8(reader.ValueSpan, out result)", ascii);

        // "r": one byte, with the case bit set.
        Assert.Contains("if ((value[0] | 0x20) == 0x72)", ascii);

        // "Green": two overlapping 32-bit words, "gree" and "reen", little-endian.
        Assert.Contains(
            "if ((" + read + "UInt32LittleEndian(value) | 0x20202020u) == 0x65657267u && (" + read + "UInt32LittleEndian(value.Slice(1)) | 0x20202020u) == 0x6E656572u)",
            ascii);

        // "a_1": only the letter gets the case bit; '_' and '1' must match exactly.
        Assert.Contains("(" + read + "UInt16LittleEndian(value) | 0x0020) == 0x5F61 && " + read + "UInt16LittleEndian(value.Slice(1)) == 0x315F", ascii);

        // One non-ASCII name and ASCII case-folding no longer equals OrdinalIgnoreCase: char-based path only.
        var unicode = Harness.Run(Target.Net100Latest, Using + """
            using System.Text.Json.Serialization;
            namespace App { [EnumExtensions] public enum Color { [JsonStringEnumMemberName("café")] Red, Green } }
            """).GeneratedFor("App.Color.");
        Assert.DoesNotContain("TryParseLenientUtf8", unicode);
        Assert.DoesNotContain("BinaryPrimitives", unicode);
        Assert.Contains("reader.CopyString(buffer)", unicode);
    }

    [Fact]
    public void StringMethods_UseATableForDenseValuesAndASwitchForSparseOnes()
    {
        var dense = Harness.Run(Target.Net100Latest, Using + "namespace App { [EnumExtensions] public enum E { A = 10, B, C } }")
            .GeneratedFor("App.E.");
        Assert.Contains("var table = s_names;", dense); // contiguous and unaliased: the names array is the table
        Assert.Contains("var index = unchecked((uint)((int)value - 10));", dense);
        Assert.DoesNotContain("case global::App.E.A: return", dense);

        var gaps = Harness.Run(Target.Net100Latest, Using + "namespace App { [EnumExtensions] public enum E { A = 1, B = 2, D = 4 } }")
            .GeneratedFor("App.E.");
        Assert.Contains("string?[] s_getStringTable", gaps);
        Assert.Contains("if (text != null) return text;", gaps);

        var sparse = Harness.Run(Target.Net100Latest, Using + "namespace App { [EnumExtensions] public enum E { A = 0, B = 1000 } }")
            .GeneratedFor("App.E.");
        Assert.Contains("case global::App.E.A: return \"A\";", sparse);
        Assert.DoesNotContain("var table =", sparse);
    }

    [Fact]
    public void FlagCombinations_AreCachedOnlyWhenEveryCombinationFitsASmallTable()
    {
        const string cache = "FormatUndefined(table, value, 0)";

        // Three bits: eight slots, three of them declared.
        var small = Harness.Run(Target.Net100Latest, Using + "namespace App { [System.Flags, EnumExtensions] public enum E { A = 1, B = 2, C = 4 } }")
            .GeneratedFor("App.E.");
        Assert.Contains("var table = new string?[8];", small);
        Assert.Contains("table[4] = \"C\";", small);
        Assert.Contains(cache, small);
        Assert.Contains("FormatUndefined(table, value, 1)", small);  // upper case keeps its own texts
        Assert.Contains("FormatUndefined(table, value, -1)", small);

        // The table runs from zero to "all flags set", so it is only as long as the highest bit needs.
        var twoBits = Harness.Run(Target.Net100Latest, Using + "namespace App { [System.Flags, EnumExtensions] public enum E { A = 1, H = 0x80 } }")
            .GeneratedFor("App.E.");
        Assert.Contains("var table = new string?[130];", twoBits);

        // Eight bits is the limit: 256 slots.
        var eight = Harness.Run(Target.Net100Latest, Using + """
            namespace App
            {
                [System.Flags, EnumExtensions]
                public enum E { B0 = 1, B1 = 2, B2 = 4, B3 = 8, B4 = 16, B5 = 32, B6 = 64, B7 = 128 }
            }
            """).GeneratedFor("App.E.");
        Assert.Contains("var table = new string?[256];", eight);
        Assert.Contains(cache, eight);

        // A ninth bit, a negative member, or no [Flags]: no combination table.
        foreach (var body in new[]
                 {
                     "[System.Flags, EnumExtensions] public enum E { A = 1, I = 0x100 }",
                     "[System.Flags, EnumExtensions] public enum E { A = 1, All = -1 }",
                     "[EnumExtensions] public enum E { A = 1, B = 2, C = 4 }",
                 })
        {
            var output = Harness.Run(Target.Net100Latest, Using + "namespace App { " + body + " }").GeneratedFor("App.E.");
            Assert.DoesNotContain("FormatUndefined(table", output);
        }

        // Every combination already declared: nothing to remember, so the plain table is used.
        var complete = Harness.Run(Target.Net100Latest, Using + "namespace App { [System.Flags, EnumExtensions] public enum E { None, A, B, AB } }")
            .GeneratedFor("App.E.");
        Assert.DoesNotContain("FormatUndefined(table", complete);
        Assert.Contains("var table = s_names;", complete);
    }

    [Fact]
    public void NetStandardStringMatcher_ParsesCorrectlyAtRuntime()
    {
        // The string-based matcher (no spans) is only used by netstandard2.0 consumers, so load and run it here.
        const string source = Using + """
            namespace App
            {
                [EnumExtensions]
                public enum Shape { Abc, Abd, Xbc, Abe, Square, Circle, Triangle, A }
            }
            """;
        var result = Harness.Run(Target.NetStandard20CSharp73, source);
        using var stream = new MemoryStream();
        Assert.True(result.Output.Emit(stream).Success);

        var assembly = Assembly.Load(stream.ToArray());
        var shape = assembly.GetType("App.Shape")!;
        var extensions = assembly.GetType("App.ShapeExtensions")!;
        var tryParse = extensions.GetMethod("TryParse", [typeof(string), shape.MakeByRefType()])!;
        var tryParseIgnoreCase = extensions.GetMethod("TryParse", [typeof(string), typeof(bool), shape.MakeByRefType()])!;
        var getString = extensions.GetMethod("GetString")!;
        var getValues = extensions.GetMethod("GetValues")!;

        foreach (var name in Enum.GetNames(shape))
        {
            var args = new object?[] { name, null };
            Assert.True((bool)tryParse.Invoke(null, args)!);
            Assert.Equal(Enum.Parse(shape, name), args[1]);
            Assert.Equal(name, getString.Invoke(null, [args[1]]));

            args = [name.ToUpperInvariant(), true, null];
            Assert.True((bool)tryParseIgnoreCase.Invoke(null, args)!);
            Assert.Equal(Enum.Parse(shape, name), args[2]);
        }

        foreach (var bad in new[] { "", "Ab", "Abf", "abc", "Squares", "B", null })
        {
            Assert.False((bool)tryParse.Invoke(null, [bad, null])!);
        }

        Assert.Equal(Enum.GetValues(shape), (Array)getValues.Invoke(null, null)!);
    }

    [Fact]
    public void MarkerAttribute_IsConditional()
    {
        var result = Harness.Run(Target.Net100Latest, "class C { }");
        var attribute = result.GeneratedFor(AttributeSource.HintName);
        Assert.Contains("Conditional(\"SEIORI_ENUMGENERATOR_USAGES\")", attribute);
        Assert.Contains("internal sealed class EnumExtensionsAttribute", attribute);
        Assert.Contains("internal sealed class EnumDisplayAttribute", attribute);
        Assert.Contains("internal sealed class EnumDescriptionAttribute", attribute);
        Assert.Contains("internal sealed class EnumJsonAttribute", attribute);
        Assert.Equal(4, attribute.Split("Conditional(\"SEIORI_ENUMGENERATOR_USAGES\")").Length - 1);
    }

    [Fact]
    public void RoleAttributes_WorkWithoutSystemTextJson_AndAreMatchedByNamespace()
    {
        // netstandard2.0 has no System.Text.Json or [JsonStringEnumMemberName]; [EnumJson] still names the member.
        var netStandard = Harness.Run(Target.NetStandard20CSharp73, Using + """
            namespace App { [EnumExtensions] public enum E { [EnumDisplay("Shown")] [EnumJson("wire")] A, B } }
            """).GeneratedFor("App.E.");
        Assert.Contains("\"wire\"", netStandard);
        Assert.Contains("\"Shown\"", netStandard);

        // A look-alike declared elsewhere is somebody else's attribute and is ignored.
        var lookAlike = Harness.Run(Target.Net100Latest, """
            namespace Other
            {
                public sealed class EnumJsonAttribute : System.Attribute { public EnumJsonAttribute(string name) { } }
                public sealed class EnumDisplayAttribute : System.Attribute { public EnumDisplayAttribute(string text) { } }
            }
            namespace App
            {
                [Seiori.EnumGenerator.EnumExtensions]
                public enum E { [Other.EnumDisplay("Shown")] [Other.EnumJson("wire")] A, B }
            }
            """);
        Assert.Empty(lookAlike.GeneratorDiagnostics);
        var ignored = lookAlike.GeneratedFor("App.E.");
        Assert.DoesNotContain("\"wire\"", ignored);
        Assert.DoesNotContain("\"Shown\"", ignored);
    }

    [Fact]
    public void GeneratedCodeAttribute_CarriesTheGeneratorsOwnVersion()
    {
        // The release pipeline sets the version at build time; the stamp must follow it, not a constant.
        var informational = typeof(EnumExtensionsGenerator).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        var expected = informational.Split('+')[0];
        Assert.Matches(@"^\d+\.\d+\.\d+", expected);

        var output = Harness.Run(Target.Net100Latest, Using + "namespace App { [EnumExtensions] public enum E { A } }")
            .GeneratedFor("App.E.");
        Assert.Contains("GeneratedCode(\"Seiori.EnumGenerator\", \"" + expected + "\")", output);
    }

    [Fact]
    public void HintNames_AreUniqueAndStable()
    {
        const string source = Using + "namespace App { [EnumExtensions] public enum Status { A } [EnumExtensions] public enum status { B } }";
        var first = Harness.Run(Target.Net100Latest, source).Run.GeneratedTrees.Select(t => Path.GetFileName(t.FilePath)).ToList();
        var second = Harness.Run(Target.Net100Latest, source).Run.GeneratedTrees.Select(t => Path.GetFileName(t.FilePath)).ToList();

        Assert.Equal(3, first.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(first, second);
    }
}
