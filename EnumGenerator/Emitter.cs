using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Seiori.EnumGenerator;

/// <summary>
/// Writes the extension class for one enum. Output targets C# 7.3 so it compiles in every consumer project:
/// no switch expressions, no file-scoped namespaces, no target-typed <c>new</c>, and nullable annotations
/// only when the consumer's language version supports them. Every type is <c>global::</c>-qualified so
/// user types (an enum called <c>Type</c>, a namespace called <c>System</c>) cannot capture a reference.
/// </summary>
internal static class Emitter
{
    private static readonly string Version = GetVersion();
    private const string SpanOfChar = "global::System.ReadOnlySpan<char>";
    private const string SpanOfByte = "global::System.ReadOnlySpan<byte>";
    private const string Mem = "global::System.MemoryExtensions";
    private const string Invariant = "global::System.Globalization.CultureInfo.InvariantCulture";
    private const string OrdinalIgnoreCase = "global::System.StringComparison.OrdinalIgnoreCase";
    private const string Json = "global::System.Text.Json";
    private const string BinaryPrimitives = "global::System.Buffers.Binary.BinaryPrimitives";
    private const string AggressiveInlining =
        "[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]";
    private const string NoInlining =
        "[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]";

    /// <summary>Stack buffer (in chars) for the JSON converter's lenient read path.</summary>
    private const int SlowReadBufferLength = 128;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private enum MatchMode
    {
        Span,
        String,
        Utf8,

        /// <summary>UTF-8 bytes compared ignoring ASCII case; only used when every token is pure ASCII.</summary>
        Utf8IgnoreCase,
    }

    private sealed class Candidate(string token, string expression)
    {
        public string Token { get; } = token;
        public string Expression { get; } = expression;
        public byte[]? Utf8 { get; set; }
        public string? Utf8Property { get; set; }
    }

    /// <summary>
    /// A member plus the C# expression that evaluates to it. The expression is built here because the parser runs
    /// on every keystroke and the emitter only when the enum changes.
    /// </summary>
    private sealed class Member(MemberModel model, string expression)
    {
        public string Name { get; } = model.Name;
        public string Expression { get; } = expression;
        public ulong Bits { get; } = model.Bits;
        public string? DisplayName { get; } = model.DisplayName;
        public string? Description { get; } = model.Description;
        public string? SerializedName { get; } = model.SerializedName;
    }

    /// <summary>
    /// How the distinct values sit on the number line, which decides between arithmetic (range compare, bitmask,
    /// table lookup) and a <c>switch</c>.
    /// </summary>
    private sealed class ValueLayout
    {
        /// <summary>Largest "all flags set" value that gets a combination table: 256 slots (2 KB) per text method.</summary>
        private const ulong MaxFlagCombination = 0xFF;

        private ValueLayout(
            List<ulong> values, ulong min, ulong range, bool isSmall, string offset, string rangeLiteral, bool forceTable = false)
        {
            Values = values;
            Min = min;
            Range = range;
            IsContiguous = range == (ulong)(values.Count - 1);
            OffsetType = isSmall ? "uint" : "ulong";
            Offset = offset;
            RangeLiteral = rangeLiteral;

            // One reference per slot: only worth it when a quarter of the slots are filled, or the table is tiny.
            UseTable = forceTable || range < (ulong)Math.Max(16, 4 * values.Count);
            TableLength = UseTable ? (int)range + 1 : 0;
        }

        /// <summary>The distinct values, ordered numerically in the underlying type's signedness.</summary>
        public List<ulong> Values { get; }

        public ulong Min { get; }

        /// <summary>Largest value minus smallest value.</summary>
        public ulong Range { get; }

        public bool IsContiguous { get; }

        /// <summary><c>uint</c> or <c>ulong</c>: the type of <see cref="Offset"/>.</summary>
        public string OffsetType { get; }

        /// <summary>
        /// An expression for <c>value - Min</c> as an unsigned number. It wraps, so values below the minimum become
        /// huge offsets and fail any <c>&lt;= Range</c> compare.
        /// </summary>
        public string Offset { get; }

        public string RangeLiteral { get; }

        /// <summary>Whether per-value data is a table indexed by <see cref="Offset"/> rather than a <c>switch</c>.</summary>
        public bool UseTable { get; }

        public int TableLength { get; }

        public int IndexOf(ulong bits) => (int)unchecked(bits - Min);

        public static ValueLayout? Create(EnumModel model, List<Member> distinct)
        {
            if (distinct.Count == 0) return null;

            var values = distinct.Select(static m => m.Bits).ToList();
            values.Sort(model.IsSigned
                ? static (a, b) => unchecked((long)a).CompareTo(unchecked((long)b))
                : static (a, b) => a.CompareTo(b));

            var min = values[0];
            var range = unchecked(values[values.Count - 1] - min);
            var isSmall = model.UnderlyingSize <= 4;

            string offset;
            string rangeLiteral;
            if (isSmall)
            {
                var min32 = unchecked((int)min);
                offset = min32 switch
                {
                    0 => "unchecked((uint)(int)value)",
                    < 0 and > int.MinValue => "unchecked((uint)((int)value + " + Literals.Int32(-min32) + "))",
                    _ => "unchecked((uint)((int)value - " + Literals.Int32(min32) + "))",
                };
                rangeLiteral = Literals.UInt32((uint)range);
            }
            else
            {
                var min64 = unchecked((long)min);
                offset = min64 switch
                {
                    0 => "unchecked((ulong)(long)value)",
                    < 0 and > long.MinValue => "unchecked((ulong)((long)value + " + Literals.Int64(-min64) + "))",
                    _ => "unchecked((ulong)((long)value - " + Literals.Int64(min64) + "))",
                };
                rangeLiteral = Literals.UInt64(range);
            }

            return new ValueLayout(values, min, range, isSmall, offset, rangeLiteral);
        }

        /// <summary>
        /// For a small <c>[Flags]</c> enum: a layout from zero to "all flags set", so a table has a slot for every
        /// combination. Returns <see langword="null"/> otherwise, including for any negative member (its bits are
        /// sign-extended, so the table would be huge).
        /// </summary>
        public static ValueLayout? CreateForFlagCombinations(EnumModel model, List<Member> distinct)
        {
            if (!model.IsFlags || distinct.Count == 0) return null;

            var all = 0UL;
            foreach (var member in distinct) all |= member.Bits;
            if (all > MaxFlagCombination) return null;

            var values = distinct.Select(static m => m.Bits).ToList();
            values.Sort();

            var isSmall = model.UnderlyingSize <= 4;
            return new ValueLayout(
                values,
                min: 0,
                range: all,
                isSmall,
                offset: isSmall ? "unchecked((uint)(int)value)" : "unchecked((ulong)(long)value)",
                rangeLiteral: isSmall ? Literals.UInt32((uint)all) : Literals.UInt64(all),
                forceTable: true);
        }
    }

    private sealed class Context
    {
        public Context(EnumModel model, GenerationOptions options)
        {
            Model = model;
            Options = options;
            Enum = model.FullyQualifiedName;
            Underlying = model.UnderlyingType;
            NullableString = options.HasNullableAnnotations ? "string?" : "string";
            Default = "default(" + model.FullyQualifiedName + ")";
            ExtensionClass = model.Namespace.Length == 0
                ? "global::" + model.ClassName
                : "global::" + model.Namespace + "." + model.ClassName;

            Members = new List<Member>(model.Members.Count);
            foreach (var member in model.Members)
            {
                Members.Add(new Member(member, member.ReferByValue
                    ? Literals.EnumCast(Enum, model.UnderlyingType, model.IsSigned, member.Bits)
                    : Enum + "." + Literals.EscapeIdentifier(member.Name)));
            }

            Sorted = Members.OrderBy(static m => m.Bits).ToList();
            Distinct = DistinctByValue(Members);
            Layout = ValueLayout.Create(model, Distinct);

            var combinations = ValueLayout.CreateForFlagCombinations(model, Distinct);
            StringLayout = combinations ?? Layout;
            CachesFlagCombinations = combinations is { IsContiguous: false };
            SerializedNamesAreNames = Members.All(static m => m.SerializedName is null || m.SerializedName == m.Name);
        }

        public EnumModel Model { get; }
        public GenerationOptions Options { get; }
        public string Enum { get; }
        public string Underlying { get; }
        public string NullableString { get; }
        public string Default { get; }
        public string ExtensionClass { get; }

        /// <summary>All members in declaration order.</summary>
        public List<Member> Members { get; }

        /// <summary>All members, stably ordered by unsigned value, matching <see cref="System.Enum.GetValues(Type)"/>.</summary>
        public List<Member> Sorted { get; }

        /// <summary>The first-declared member for each distinct value; aliases would be duplicate switch cases.</summary>
        public List<Member> Distinct { get; }

        /// <summary><see langword="null"/> for an enum with no members.</summary>
        public ValueLayout? Layout { get; }

        /// <summary>
        /// The layout the text methods index their tables by: <see cref="Layout"/>, or for a small <c>[Flags]</c>
        /// enum one that also has slots for combinations.
        /// </summary>
        public ValueLayout? StringLayout { get; }

        /// <summary>
        /// The text tables have empty slots for undeclared flag combinations, filled at run time the first time
        /// each one is formatted.
        /// </summary>
        public bool CachesFlagCombinations { get; }

        /// <summary>
        /// No serialized name differs from its member name, so serialized-name methods forward to their
        /// member-name counterparts instead of being emitted twice.
        /// </summary>
        public bool SerializedNamesAreNames { get; }
    }

    public static string Emit(EnumModel model, GenerationOptions options)
    {
        var ctx = new Context(model, options);
        var w = new SourceWriter();

        w.Line("// <auto-generated/>");
        if (options.HasNullableAnnotations) w.Line("#nullable enable");
        w.Line("#pragma warning disable CS0612, CS0618, CS1591");
        w.Line();

        var hasNamespace = model.Namespace.Length > 0;
        if (hasNamespace) w.Open("namespace " + model.Namespace);

        w.Line("/// <summary>Generated helpers for <see cref=\"" + ctx.Enum + "\"/>.</summary>");
        w.Line("[global::System.CodeDom.Compiler.GeneratedCode(\"Seiori.EnumGenerator\", \"" + Version + "\")]");
        w.Line("[global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]");
        w.Open((model.IsPublic ? "public" : "internal") + " static partial class " + model.ClassName);

        EmitCollections(w, ctx);
        EmitIsDefined(w, ctx);
        EmitStringMethods(w, ctx);
        EmitValueHelpers(w, ctx);
        EmitNameParsing(w, ctx);
        EmitMemberValueParsing(w, ctx, out var utf8Candidates);
        if (options.HasSystemTextJson) EmitJsonConverter(w, ctx);
        EmitUtf8Tokens(w, utf8Candidates);

        w.Close();
        if (hasNamespace) w.Close();

        return w.ToString();
    }

    private static void EmitCollections(SourceWriter w, Context ctx)
    {
        var e = ctx.Enum;
        w.Line("/// <summary>The number of members declared by the enum, including aliases.</summary>");
        w.Line("public const int Length = " + ctx.Sorted.Count.ToString(CultureInfo.InvariantCulture) + ";");
        w.Line();

        EmitArray(w, e, "s_values", ctx.Sorted.Select(static m => m.Expression));
        EmitArray(w, "string", "s_names", ctx.Sorted.Select(static m => Literals.String(m.Name)));

        if (ctx.Options.HasSpan)
        {
            w.Line("/// <summary>All members ordered by value, like <see cref=\"global::System.Enum.GetValues(global::System.Type)\"/>. Allocation-free.</summary>");
            w.Line("public static global::System.ReadOnlySpan<" + e + "> GetValues() => s_values;");
            w.Line();
            w.Line("/// <summary>All member names ordered by value, like <see cref=\"global::System.Enum.GetNames(global::System.Type)\"/>. Allocation-free.</summary>");
            w.Line("public static global::System.ReadOnlySpan<string> GetNames() => s_names;");
        }
        else
        {
            w.Line("/// <summary>All members ordered by value, like <see cref=\"global::System.Enum.GetValues(global::System.Type)\"/>. Returns a new array.</summary>");
            w.Line("public static " + e + "[] GetValues() => (" + e + "[])s_values.Clone();");
            w.Line();
            w.Line("/// <summary>All member names ordered by value, like <see cref=\"global::System.Enum.GetNames(global::System.Type)\"/>. Returns a new array.</summary>");
            w.Line("public static string[] GetNames() => (string[])s_names.Clone();");
        }

        w.Line();
    }

    private static void EmitArray(SourceWriter w, string elementType, string name, IEnumerable<string> items)
    {
        var list = items.ToList();
        if (list.Count == 0)
        {
            w.Line("private static readonly " + elementType + "[] " + name + " = new " + elementType + "[0];");
            w.Line();
            return;
        }

        w.Line("private static readonly " + elementType + "[] " + name + " = new " + elementType + "[]");
        w.Open();
        foreach (var item in list) w.Line(item + ",");
        w.Close(";");
        w.Line();
    }

    private static void EmitIsDefined(SourceWriter w, Context ctx)
    {
        w.Line("/// <summary>Returns <see langword=\"true\"/> when <paramref name=\"value\"/> equals a declared member, like <see cref=\"global::System.Enum.IsDefined(global::System.Type, object)\"/>.</summary>");

        var layout = ctx.Layout;
        if (layout is null)
        {
            w.Line("public static bool IsDefined(" + ctx.Enum + " value) => false;");
            w.Line();
            return;
        }

        if (!layout.IsContiguous && layout.Range >= 64)
        {
            // Sparse: let the JIT build a jump table or binary search.
            w.Open("public static bool IsDefined(" + ctx.Enum + " value)");
            w.Open("switch (value)");
            foreach (var member in ctx.Distinct) w.Line("case " + member.Expression + ":");
            w.Indent();
            w.Line("return true;");
            w.Outdent();
            w.Line("default:");
            w.Indent();
            w.Line("return false;");
            w.Outdent();
            w.Close();
            w.Close();
            w.Line();
            return;
        }

        // Dense: a single unsigned range compare, plus a bitmask test when there are gaps.
        w.Line(AggressiveInlining);
        if (layout.IsContiguous)
        {
            w.Line("public static bool IsDefined(" + ctx.Enum + " value) => " + layout.Offset + " <= " + layout.RangeLiteral + ";");
        }
        else
        {
            var mask = 0UL;
            foreach (var value in layout.Values) mask |= 1UL << layout.IndexOf(value);

            w.Open("public static bool IsDefined(" + ctx.Enum + " value)");
            w.Line("var offset = " + layout.Offset + ";");
            w.Line("return offset <= " + layout.RangeLiteral + " && ((0x" + mask.ToString("X", CultureInfo.InvariantCulture) + "UL >> (int)offset) & 1UL) != 0;");
            w.Close();
        }

        w.Line();
    }

    private static void EmitStringMethods(SourceWriter w, Context ctx)
    {
        var flags = ctx.Model.IsFlags;
        var emitted = new List<StringMethod>();

        EmitStringMethod(w, ctx, emitted,
            "Returns the member name, like <see cref=\"object.ToString\"/> but without reflection or allocation.",
            "GetString", static m => m.Name, "FormatUndefined(value)");

        EmitStringMethod(w, ctx, emitted,
            "Returns the serialized name from <c>[EnumJson]</c>, <c>[JsonStringEnumMemberName]</c>, <c>[EnumMember(Value)]</c> or <c>[JsonPropertyName]</c> (in that priority), falling back to the member name.",
            "GetEnumMemberValue", static m => m.SerializedName ?? m.Name, "FormatUndefined(value)");

        EmitStringMethod(w, ctx, emitted,
            "Returns the name shown to people, from <c>[EnumDisplay]</c> or <c>[Display(Name)]</c> (in that priority), falling back to the member name.",
            "GetDisplayValue", static m => m.DisplayName ?? m.Name, "FormatUndefined(value)");

        EmitStringMethod(w, ctx, emitted,
            "Returns what the member is or does, from <c>[EnumDescription]</c>, <c>[Display(Description)]</c> or <c>[Description]</c> (in that priority), falling back to the member name.",
            "GetDescription", static m => m.Description ?? m.Name, "FormatUndefined(value)");

        EmitStringMethod(w, ctx, emitted,
            "Returns the member name in upper case (invariant culture).",
            "GetStringUpperCase", static m => m.Name.ToUpperInvariant(),
            flags ? "FormatUndefined(value).ToUpperInvariant()" : "FormatUndefined(value)", casing: 1);

        EmitStringMethod(w, ctx, emitted,
            "Returns the member name in lower case (invariant culture).",
            "GetStringLowerCase", static m => m.Name.ToLowerInvariant(),
            flags ? "FormatUndefined(value).ToLowerInvariant()" : "FormatUndefined(value)", casing: -1);

        // The cold path, kept out of line so the table lookups that fall back to it stay small when inlined.
        if (ctx.CachesFlagCombinations)
        {
            var layout = ctx.StringLayout!;
            w.Line("// Formats an undeclared value and remembers it in the table. Racing threads store equal strings.");
            w.Line(NoInlining);
            w.Open("private static string FormatUndefined(" + ctx.NullableString + "[] table, " + ctx.Enum + " value, int casing)");
            w.Line("string text = value.ToString();");
            w.Line("if (casing > 0) text = text.ToUpperInvariant();");
            w.Line("else if (casing < 0) text = text.ToLowerInvariant();");
            w.Line();
            w.Line("var index = " + layout.Offset + ";");
            w.Line("if (index < unchecked((" + layout.OffsetType + ")table.Length)) "
                   + (layout.OffsetType == "uint" ? "table[index]" : "table[unchecked((int)index)]") + " = text;");
            w.Line("return text;");
            w.Close();
            w.Line();
            return;
        }

        w.Line(NoInlining);
        w.Open("private static string FormatUndefined(" + ctx.Enum + " value)");
        w.Line(flags
            ? "return value.ToString();"
            : "return ((" + ctx.Underlying + ")value).ToString(" + Invariant + ");");
        w.Close();
        w.Line();
    }

    /// <summary>A value-to-string method that has been emitted, so a later identical one can forward to it.</summary>
    private sealed class StringMethod(string name, List<string> strings, string fallback)
    {
        public string Name { get; } = name;
        public List<string> Strings { get; } = strings;
        public string Fallback { get; } = fallback;
    }

    private static void EmitStringMethod(
        SourceWriter w, Context ctx, List<StringMethod> emitted, string summary, string name,
        Func<Member, string> selector, string fallback, int casing = 0)
    {
        var strings = ctx.Distinct.Select(selector).ToList();
        summary = "/// <summary>" + summary + "</summary>";

        // Identical bodies are emitted once; later methods forward to the first.
        foreach (var earlier in emitted)
        {
            if (earlier.Fallback == fallback && earlier.Strings.SequenceEqual(strings))
            {
                w.Line(summary);
                w.Line(AggressiveInlining);
                w.Line("public static string " + name + "(this " + ctx.Enum + " value) => " + earlier.Name + "(value);");
                w.Line();
                return;
            }
        }

        emitted.Add(new StringMethod(name, strings, fallback));
        if (ctx.StringLayout is { UseTable: true } layout)
        {
            EmitStringTable(w, ctx, layout, summary, name, strings, fallback, casing);
        }
        else
        {
            EmitStringSwitch(w, ctx, summary, name, strings, fallback);
        }
    }

    /// <summary>
    /// Dense values: index a string table by <c>value - min</c>. A <c>switch</c> is a jump table, which mispredicts
    /// whenever consecutive values differ; this is a bounds check and a load, small enough to inline.
    /// </summary>
    private static void EmitStringTable(
        SourceWriter w, Context ctx, ValueLayout layout, string summary, string name, List<string> strings,
        string fallback, int casing)
    {
        var entries = new string?[layout.TableLength];
        for (var i = 0; i < strings.Count; i++) entries[layout.IndexOf(ctx.Distinct[i].Bits)] = strings[i];

        var hasGaps = !layout.IsContiguous;
        string table;
        if (!hasGaps && entries.SequenceEqual(ctx.Sorted.Select(static m => m.Name)))
        {
            table = "s_names"; // already exactly this table: no second array
        }
        else if (ctx.CachesFlagCombinations)
        {
            // Most slots start empty, so set only the declared members rather than list every slot.
            table = "s_" + char.ToLowerInvariant(name[0]) + name.Substring(1) + "Table";
            var create = "Create" + name + "Table";
            var slots = layout.TableLength.ToString(CultureInfo.InvariantCulture);
            w.Line("private static readonly " + ctx.NullableString + "[] " + table + " = " + create + "();");
            w.Line();
            w.Open("private static " + ctx.NullableString + "[] " + create + "()");
            w.Line("var table = new " + ctx.NullableString + "[" + slots + "];");
            for (var i = 0; i < entries.Length; i++)
            {
                if (entries[i] is { } entry)
                {
                    w.Line("table[" + i.ToString(CultureInfo.InvariantCulture) + "] = " + Literals.String(entry) + ";");
                }
            }

            w.Line("return table;");
            w.Close();
            w.Line();
            fallback = "FormatUndefined(table, value, " + casing.ToString(CultureInfo.InvariantCulture) + ")";
        }
        else
        {
            table = "s_" + char.ToLowerInvariant(name[0]) + name.Substring(1) + "Table";
            EmitArray(w, hasGaps ? ctx.NullableString : "string", table,
                entries.Select(static entry => entry is null ? "null" : Literals.String(entry)));
        }

        var length = "unchecked((" + layout.OffsetType + ")table.Length)";
        var element = layout.OffsetType == "uint" ? "table[index]" : "table[unchecked((int)index)]";

        w.Line(summary);
        w.Line(AggressiveInlining);
        w.Open("public static string " + name + "(this " + ctx.Enum + " value)");
        w.Line("var table = " + table + ";");
        w.Line("var index = " + layout.Offset + ";");
        if (hasGaps)
        {
            w.Open("if (index < " + length + ")");
            w.Line("var text = " + element + ";");
            w.Line("if (text != null) return text;");
            w.Close();
            w.Line();
            w.Line("return " + fallback + ";");
        }
        else
        {
            w.Line("return index < " + length + " ? " + element + " : " + fallback + ";");
        }

        w.Close();
        w.Line();
    }

    /// <summary>Sparse values (or none): a <c>switch</c> returning literals.</summary>
    private static void EmitStringSwitch(
        SourceWriter w, Context ctx, string summary, string name, List<string> strings, string fallback)
    {
        w.Line(summary);
        w.Open("public static string " + name + "(this " + ctx.Enum + " value)");
        w.Open("switch (value)");
        for (var i = 0; i < strings.Count; i++)
        {
            w.Line("case " + ctx.Distinct[i].Expression + ": return " + Literals.String(strings[i]) + ";");
        }

        w.Line("default: return " + fallback + ";");
        w.Close();
        w.Close();
        w.Line();
    }

    private static void EmitValueHelpers(SourceWriter w, Context ctx)
    {
        w.Line("/// <summary>Returns the underlying numeric value.</summary>");
        w.Line(AggressiveInlining);
        w.Line("public static " + ctx.Underlying + " GetUnderlyingValue(this " + ctx.Enum + " value) => (" + ctx.Underlying + ")value;");
        w.Line();

        if (ctx.Model.IsFlags)
        {
            w.Line("/// <summary>Returns <see langword=\"true\"/> when every bit in <paramref name=\"flag\"/> is set in <paramref name=\"value\"/>. Never boxes.</summary>");
            w.Line(AggressiveInlining);
            w.Line("public static bool HasFlagFast(this " + ctx.Enum + " value, " + ctx.Enum + " flag) => (value & flag) == flag;");
            w.Line();
        }
    }

    private static void EmitNameParsing(SourceWriter w, Context ctx)
    {
        var candidates = ctx.Members.Select(static m => new Candidate(m.Name, m.Expression)).ToList();
        EmitTryParseFamily(w, ctx, "TryParse", "member name", candidates);

        var e = ctx.Enum;
        var span = ctx.Options.HasSpan;

        w.Line("/// <summary>Returns <see langword=\"true\"/> when <paramref name=\"name\"/> is a member name (case-sensitive).</summary>");
        w.Line("public static bool IsDefined(" + ctx.NullableString + " name) => TryParse(name, out _);");
        w.Line();
        if (span)
        {
            w.Line("/// <summary>Returns <see langword=\"true\"/> when <paramref name=\"name\"/> is a member name (case-sensitive).</summary>");
            w.Line("public static bool IsDefined(" + SpanOfChar + " name) => TryParse(name, out _);");
            w.Line();
        }

        w.Line("/// <summary>Converts a member name to its value (case-sensitive). Throws <see cref=\"global::System.ArgumentException\"/> when not found.</summary>");
        w.Line("public static " + e + " Parse(string value) => Parse(value, false);");
        w.Line();
        w.Line("/// <summary>Converts a member name to its value. Throws <see cref=\"global::System.ArgumentException\"/> when not found.</summary>");
        w.Open("public static " + e + " Parse(string value, bool ignoreCase)");
        w.Line("if (value is null) throw new global::System.ArgumentNullException(nameof(value));");
        w.Line(e + " result;");
        w.Line("if (TryParse(value, ignoreCase, out result)) return result;");
        w.Line("throw CreateParseException(value);");
        w.Close();
        w.Line();

        if (span)
        {
            w.Line("/// <summary>Converts a member name to its value (case-sensitive). Throws <see cref=\"global::System.ArgumentException\"/> when not found.</summary>");
            w.Line("public static " + e + " Parse(" + SpanOfChar + " value) => Parse(value, false);");
            w.Line();
            w.Line("/// <summary>Converts a member name to its value. Throws <see cref=\"global::System.ArgumentException\"/> when not found.</summary>");
            w.Open("public static " + e + " Parse(" + SpanOfChar + " value, bool ignoreCase)");
            w.Line(e + " result;");
            w.Line("if (TryParse(value, ignoreCase, out result)) return result;");
            w.Line("throw CreateParseException(value.ToString());");
            w.Close();
            w.Line();
        }

        w.Line(NoInlining);
        w.Line("private static global::System.ArgumentException CreateParseException(string value) =>");
        w.Indent();
        w.Line("new global::System.ArgumentException(\"Requested value '\" + value + \"' was not found in enum \" + "
               + Literals.String("'" + ctx.Model.DisplayName + "'.") + ", nameof(value));");
        w.Outdent();
        w.Line();
    }

    private static void EmitMemberValueParsing(SourceWriter w, Context ctx, out List<Candidate> utf8Candidates)
    {
        const string description = "serialized name (see <see cref=\"GetEnumMemberValue\"/>)";
        var candidates = ctx.Members
            .Select(static m => new Candidate(m.SerializedName ?? m.Name, m.Expression))
            .ToList();
        if (ctx.SerializedNamesAreNames)
        {
            // Same tokens as TryParse: forward rather than emit both matchers a second time.
            EmitTryParseForwarders(w, ctx, "TryParseEnumMemberValue", "TryParse", description);
        }
        else
        {
            EmitTryParseFamily(w, ctx, "TryParseEnumMemberValue", description, candidates);
        }

        utf8Candidates = [];
        if (!ctx.Options.HasSpan) return;

        // UTF-8 matching lets the JSON converter compare the reader's raw bytes without transcoding or allocating.
        foreach (var candidate in DistinctByToken(candidates, StringComparer.Ordinal))
        {
            byte[] bytes;
            try
            {
                bytes = StrictUtf8.GetBytes(candidate.Token);
            }
            catch (EncoderFallbackException)
            {
                continue; // Lone surrogates have no UTF-8 form; the UTF-16 fallback path still handles them.
            }

            candidate.Utf8 = bytes;
            candidate.Utf8Property = "Utf8EnumMemberValue" + utf8Candidates.Count.ToString(CultureInfo.InvariantCulture);
            utf8Candidates.Add(candidate);
        }

        w.Line("/// <summary>Converts a UTF-8 encoded serialized name (case-sensitive, unescaped) to its value. Allocation-free.</summary>");
        w.Open("public static bool TryParseEnumMemberValueUtf8(" + SpanOfByte + " value, out " + ctx.Enum + " result)");
        EmitExactMatcher(w, utf8Candidates, MatchMode.Utf8);
        w.Line("result = " + ctx.Default + ";");
        w.Line("return false;");
        w.Close();
        w.Line();
    }

    private static void EmitTryParseForwarders(SourceWriter w, Context ctx, string name, string target, string description)
    {
        Forward(ctx.NullableString + " value", "value", " (case-sensitive)");
        Forward(ctx.NullableString + " value, bool ignoreCase", "value, ignoreCase", string.Empty);
        if (ctx.Options.HasSpan)
        {
            Forward(SpanOfChar + " value", "value", " (case-sensitive)");
            Forward(SpanOfChar + " value, bool ignoreCase", "value, ignoreCase", string.Empty);
        }

        void Forward(string parameters, string arguments, string qualifier)
        {
            w.Line("/// <summary>Converts a " + description + " to its value" + qualifier + ". This enum declares no custom serialized names, so this is <c>" + target + "</c>.</summary>");
            w.Line(AggressiveInlining);
            w.Line("public static bool " + name + "(" + parameters + ", out " + ctx.Enum + " result) => " + target + "(" + arguments + ", out result);");
            w.Line();
        }
    }

    private static void EmitTryParseFamily(
        SourceWriter w, Context ctx, string name, string description, List<Candidate> candidates)
    {
        var e = ctx.Enum;
        var exact = DistinctByToken(candidates, StringComparer.Ordinal);
        var ignoreCase = DistinctByToken(candidates, StringComparer.OrdinalIgnoreCase);
        var core = name + "IgnoreCaseCore";
        var nullCheck = "if (value is null) { result = " + ctx.Default + "; return false; }";

        if (ctx.Options.HasSpan)
        {
            w.Line("/// <summary>Converts a " + description + " to its value (case-sensitive). Allocation-free.</summary>");
            w.Line(AggressiveInlining);
            w.Open("public static bool " + name + "(" + ctx.NullableString + " value, out " + e + " result)");
            w.Line(nullCheck);
            w.Line("return " + name + "(" + Mem + ".AsSpan(value), out result);");
            w.Close();
            w.Line();

            w.Line("/// <summary>Converts a " + description + " to its value. Allocation-free.</summary>");
            w.Open("public static bool " + name + "(" + ctx.NullableString + " value, bool ignoreCase, out " + e + " result)");
            w.Line(nullCheck);
            w.Line("return ignoreCase ? " + core + "(" + Mem + ".AsSpan(value), out result) : " + name + "(" + Mem + ".AsSpan(value), out result);");
            w.Close();
            w.Line();

            w.Line("/// <summary>Converts a " + description + " to its value (case-sensitive). Allocation-free.</summary>");
            w.Open("public static bool " + name + "(" + SpanOfChar + " value, out " + e + " result)");
            EmitExactMatcher(w, exact, MatchMode.Span);
            w.Line("result = " + ctx.Default + ";");
            w.Line("return false;");
            w.Close();
            w.Line();

            w.Line("/// <summary>Converts a " + description + " to its value. Allocation-free.</summary>");
            w.Line(AggressiveInlining);
            w.Line("public static bool " + name + "(" + SpanOfChar + " value, bool ignoreCase, out " + e + " result) =>");
            w.Indent();
            w.Line("ignoreCase ? " + core + "(value, out result) : " + name + "(value, out result);");
            w.Outdent();
            w.Line();

            w.Open("private static bool " + core + "(" + SpanOfChar + " value, out " + e + " result)");
            EmitIgnoreCaseMatcher(w, ignoreCase, MatchMode.Span);
            w.Line("result = " + ctx.Default + ";");
            w.Line("return false;");
            w.Close();
            w.Line();
        }
        else
        {
            w.Line("/// <summary>Converts a " + description + " to its value (case-sensitive).</summary>");
            w.Open("public static bool " + name + "(" + ctx.NullableString + " value, out " + e + " result)");
            w.Line(nullCheck);
            EmitExactMatcher(w, exact, MatchMode.String);
            w.Line("result = " + ctx.Default + ";");
            w.Line("return false;");
            w.Close();
            w.Line();

            w.Line("/// <summary>Converts a " + description + " to its value.</summary>");
            w.Open("public static bool " + name + "(" + ctx.NullableString + " value, bool ignoreCase, out " + e + " result)");
            w.Line(nullCheck);
            w.Line("return ignoreCase ? " + core + "(value, out result) : " + name + "(value, out result);");
            w.Close();
            w.Line();

            w.Open("private static bool " + core + "(string value, out " + e + " result)");
            EmitIgnoreCaseMatcher(w, ignoreCase, MatchMode.String);
            w.Line("result = " + ctx.Default + ";");
            w.Line("return false;");
            w.Close();
            w.Line();
        }
    }

    /// <summary>
    /// Case-sensitive matcher: switch on length, then on the character position that best splits the remaining
    /// candidates (recursively), and confirm with one full comparison. A miss usually costs one or two jumps.
    /// </summary>
    private static void EmitExactMatcher(SourceWriter w, List<Candidate> candidates, MatchMode mode)
    {
        if (candidates.Count == 0) return;

        w.Open("switch (value.Length)");
        foreach (var group in candidates.GroupBy(c => TokenLength(c, mode)).OrderBy(static g => g.Key))
        {
            w.Line("case " + group.Key.ToString(CultureInfo.InvariantCulture) + ":");
            w.Indent();
            EmitDispatch(w, group.ToList(), mode);
            w.Line("break;");
            w.Outdent();
        }

        w.Close();
    }

    private static void EmitDispatch(SourceWriter w, List<Candidate> group, MatchMode mode)
    {
        if (group.Count > 1)
        {
            var length = TokenLength(group[0], mode);
            int bestIndex = -1, bestCount = 1;
            for (var i = 0; i < length; i++)
            {
                var count = group.Select(c => Unit(c, mode, i)).Distinct().Count();
                if (count > bestCount)
                {
                    bestCount = count;
                    bestIndex = i;
                }
            }

            if (bestIndex >= 0)
            {
                // Ignoring case, the switch key is the byte with bit 0x20 set: both cases of a letter land on the
                // same label. Other bytes can share a label too, which is fine: the final comparison decides.
                var probe = "value[" + bestIndex.ToString(CultureInfo.InvariantCulture) + "]";
                w.Open("switch (" + (mode == MatchMode.Utf8IgnoreCase ? probe + " | 0x20" : probe) + ")");
                foreach (var sub in group.GroupBy(c => Unit(c, mode, bestIndex)).OrderBy(static g => g.Key))
                {
                    w.Line("case " + UnitLiteral(sub.Key, mode) + ":");
                    w.Indent();
                    EmitDispatch(w, sub.ToList(), mode);
                    w.Line("break;");
                    w.Outdent();
                }

                w.Close();
                return;
            }
        }

        foreach (var candidate in group)
        {
            w.Open("if (" + ExactEquals(candidate, mode) + ")");
            w.Line("result = " + candidate.Expression + ";");
            w.Line("return true;");
            w.Close();
        }
    }

    /// <summary>
    /// Case-insensitive matcher: switch on length (ordinal case-insensitive equality is per-char, so lengths must
    /// match), then compare the few candidates of that length with vectorized ordinal-ignore-case equality.
    /// </summary>
    private static void EmitIgnoreCaseMatcher(SourceWriter w, List<Candidate> candidates, MatchMode mode)
    {
        if (candidates.Count == 0) return;

        w.Open("switch (value.Length)");
        foreach (var group in candidates.GroupBy(static c => c.Token.Length).OrderBy(static g => g.Key))
        {
            w.Line("case " + group.Key.ToString(CultureInfo.InvariantCulture) + ":");
            w.Indent();
            foreach (var candidate in group)
            {
                var literal = Literals.String(candidate.Token);
                var test = mode == MatchMode.Span
                    ? Mem + ".Equals(value, " + Mem + ".AsSpan(" + literal + "), " + OrdinalIgnoreCase + ")"
                    : "string.Equals(value, " + literal + ", " + OrdinalIgnoreCase + ")";
                w.Open("if (" + test + ")");
                w.Line("result = " + candidate.Expression + ";");
                w.Line("return true;");
                w.Close();
            }

            w.Line("break;");
            w.Outdent();
        }

        w.Close();
    }

    private static string ExactEquals(Candidate candidate, MatchMode mode) => mode switch
    {
        MatchMode.Span => Mem + ".SequenceEqual(value, " + Mem + ".AsSpan(" + Literals.String(candidate.Token) + "))",
        MatchMode.String => "string.Equals(value, " + Literals.String(candidate.Token) + ")",
        MatchMode.Utf8IgnoreCase => AsciiIgnoreCaseEquals(candidate.Utf8!),
        _ => Mem + ".SequenceEqual(value, " + candidate.Utf8Property + ")",
    };

    private static int TokenLength(Candidate candidate, MatchMode mode) =>
        mode is MatchMode.Utf8 or MatchMode.Utf8IgnoreCase ? candidate.Utf8!.Length : candidate.Token.Length;

    private static int Unit(Candidate candidate, MatchMode mode, int index) => mode switch
    {
        MatchMode.Utf8 => candidate.Utf8![index],
        MatchMode.Utf8IgnoreCase => candidate.Utf8![index] | 0x20,
        _ => candidate.Token[index],
    };

    private static string UnitLiteral(int unit, MatchMode mode) =>
        mode is MatchMode.Utf8 or MatchMode.Utf8IgnoreCase ? Literals.Byte((byte)unit) : Literals.Char((char)unit);

    private static void EmitUtf8Tokens(SourceWriter w, List<Candidate> candidates)
    {
        // `ReadOnlySpan<byte> P => new byte[] { ... }` compiles to a pointer into the assembly's data section:
        // no allocation, no static constructor.
        foreach (var candidate in candidates)
        {
            var bytes = string.Join(", ", candidate.Utf8!.Select(Literals.Byte));
            w.Line("private static " + SpanOfByte + " " + candidate.Utf8Property + " => new byte[] { " + bytes + " };");
        }
    }

    /// <summary>
    /// Candidates for matching differently-cased JSON text on its UTF-8 bytes: serialized names first, then member
    /// names, as the char-based fallback does. Returns <see langword="null"/> unless every token is ASCII: only
    /// then does "ASCII letters differ by case" agree with <c>OrdinalIgnoreCase</c> for every input that can match.
    /// </summary>
    private static List<Candidate>? BuildLenientUtf8Candidates(Context ctx)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<Candidate>();
        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var member in ctx.Members)
            {
                var token = pass == 0 ? member.SerializedName ?? member.Name : member.Name;
                foreach (var c in token)
                {
                    if (c > 0x7F) return null;
                }

                if (seen.Add(token)) result.Add(new Candidate(token, member.Expression) { Utf8 = StrictUtf8.GetBytes(token) });
            }
        }

        return result;
    }

    /// <summary>
    /// A condition that is true when <c>value</c> (already known to have the token's length) equals the ASCII
    /// token ignoring ASCII case. Reads little-endian words, the last one overlapping, and compares each against
    /// a constant after setting bit 0x20 in the letter positions only. For a letter, <c>(b | 0x20) == lower</c>
    /// holds for exactly its two cases; other positions compare as is, and a non-ASCII byte can never match.
    /// </summary>
    private static string AsciiIgnoreCaseEquals(byte[] token)
    {
        var length = token.Length;
        if (length == 0) return "value.Length == 0";

        var size = length >= 8 ? 8 : length >= 4 ? 4 : length >= 2 ? 2 : 1;
        var parts = new List<string>();
        for (var offset = 0; offset + size <= length; offset += size) parts.Add(Word(offset));
        if (length % size != 0) parts.Add(Word(length - size));
        return string.Join(" && ", parts);

        string Word(int offset)
        {
            ulong expected = 0, mask = 0;
            for (var i = 0; i < size; i++)
            {
                var b = token[offset + i];
                var isLetter = (b | 0x20) is >= (byte)'a' and <= (byte)'z';
                expected |= (ulong)(isLetter ? b | 0x20 : b) << (8 * i);
                if (isLetter) mask |= 0x20UL << (8 * i);
            }

            var (digits, suffix) = size switch
            {
                8 => ("X16", "UL"),
                4 => ("X8", "u"),
                2 => ("X4", string.Empty),
                _ => ("X2", string.Empty),
            };
            var source = offset == 0 ? "value" : "value.Slice(" + offset.ToString(CultureInfo.InvariantCulture) + ")";
            var read = size switch
            {
                8 => BinaryPrimitives + ".ReadUInt64LittleEndian(" + source + ")",
                4 => BinaryPrimitives + ".ReadUInt32LittleEndian(" + source + ")",
                2 => BinaryPrimitives + ".ReadUInt16LittleEndian(" + source + ")",
                _ => "value[" + offset.ToString(CultureInfo.InvariantCulture) + "]",
            };
            var left = mask == 0
                ? read
                : "(" + read + " | 0x" + mask.ToString(digits, CultureInfo.InvariantCulture) + suffix + ")";
            return left + " == 0x" + expected.ToString(digits, CultureInfo.InvariantCulture) + suffix;
        }
    }

    private static void EmitJsonConverter(SourceWriter w, Context ctx)
    {
        var lenientUtf8 = BuildLenientUtf8Candidates(ctx);
        var e = ctx.Enum;
        var model = ctx.Model;
        var simpleName = model.DisplayName.Substring(model.DisplayName.LastIndexOf('.') + 1);
        var converterName = Literals.EscapeIdentifier(simpleName + "JsonConverter");
        var converter = ctx.ExtensionClass + "." + converterName;

        var (tryGet, numberType) = model.UnderlyingType switch
        {
            "sbyte" => ("TryGetSByte", "int"),
            "byte" => ("TryGetByte", "uint"),
            "short" => ("TryGetInt16", "int"),
            "ushort" => ("TryGetUInt16", "uint"),
            "uint" => ("TryGetUInt32", "uint"),
            "long" => ("TryGetInt64", "long"),
            "ulong" => ("TryGetUInt64", "ulong"),
            _ => ("TryGetInt32", "int"),
        };
        var numberStyles = "global::System.Globalization.NumberStyles." + (model.IsSigned ? "AllowLeadingSign" : "None");
        var notEscaped = ctx.Options.HasValueIsEscaped
            ? "!reader.ValueIsEscaped"
            : Mem + ".IndexOf(reader.ValueSpan, (byte)'\\\\') < 0";

        // A holder class creates the instance on first use. As a field of the extension class, the first
        // GetValues() or GetString() call would load System.Text.Json and allocate a converter nobody asked for.
        w.Line("/// <summary>A cached instance of the reflection-free, AOT-safe System.Text.Json converter for <see cref=\"" + e + "\"/>.</summary>");
        w.Line("public static " + converter + " JsonConverter => JsonConverterHolder.Instance;");
        w.Line();
        w.Open("private static class JsonConverterHolder");
        w.Line("internal static readonly " + converter + " Instance = new " + converter + "();");
        w.Close();
        w.Line();

        w.Line("/// <summary>");
        w.Line("/// Reflection-free, AOT-safe System.Text.Json converter. Writes <see cref=\"GetEnumMemberValue\"/> from pre-encoded");
        w.Line("/// text and reads serialized names straight from UTF-8 without allocating. Also accepts member names");
        w.Line("/// (case-insensitively) and integer values. Undefined values are written as numbers.");
        w.Line("/// </summary>");
        w.Open("public sealed class " + converterName + " : global::System.Text.Json.Serialization.JsonConverter<" + e + ">");

        // Contiguous values index a table of pre-encoded text, like the string methods; anything else selects a
        // field in a switch. (A table with gaps would need a second lookup to tell a gap from an empty name.)
        var textTable = ctx.Layout is { UseTable: true, IsContiguous: true };
        if (textTable)
        {
            var entries = new string[ctx.Layout!.TableLength];
            foreach (var member in ctx.Distinct)
            {
                entries[ctx.Layout.IndexOf(member.Bits)] =
                    Json + ".JsonEncodedText.Encode(" + Literals.String(member.SerializedName ?? member.Name) + ")";
            }

            EmitArray(w, Json + ".JsonEncodedText", "s_texts", entries);
        }
        else
        {
            for (var i = 0; i < ctx.Distinct.Count; i++)
            {
                var member = ctx.Distinct[i];
                w.Line("private static readonly " + Json + ".JsonEncodedText s_text" + i.ToString(CultureInfo.InvariantCulture)
                       + " = " + Json + ".JsonEncodedText.Encode(" + Literals.String(member.SerializedName ?? member.Name) + ");");
            }

            if (ctx.Distinct.Count > 0) w.Line();
        }

        w.Line("/// <inheritdoc/>");
        w.Open("public override " + e + " Read(ref " + Json + ".Utf8JsonReader reader, global::System.Type typeToConvert, " + Json + ".JsonSerializerOptions options)");
        w.Open("switch (reader.TokenType)");
        w.Line("case " + Json + ".JsonTokenType.String:");
        w.Indent();
        w.Line("return ReadString(ref reader, false);");
        w.Outdent();
        w.Line("case " + Json + ".JsonTokenType.Number:");
        w.Open();
        w.Line(ctx.Underlying + " number;");
        w.Line("if (reader." + tryGet + "(out number)) return (" + e + ")number;");
        w.Line("break;");
        w.Close();
        w.Close();
        w.Line();
        w.Line("throw new " + Json + ".JsonException();");
        w.Close();
        w.Line();

        w.Line("/// <inheritdoc/>");
        w.Open("public override void Write(" + Json + ".Utf8JsonWriter writer, " + e + " value, " + Json + ".JsonSerializerOptions options)");
        EmitJsonWriteSwitch(w, ctx, "WriteStringValue", "writer.WriteNumberValue((" + numberType + ")value);");
        w.Close();
        w.Line();

        if (ctx.Options.HasJsonPropertyNameSupport)
        {
            w.Line("/// <inheritdoc/>");
            w.Line("public override " + e + " ReadAsPropertyName(ref " + Json + ".Utf8JsonReader reader, global::System.Type typeToConvert, " + Json + ".JsonSerializerOptions options) =>");
            w.Indent();
            w.Line("ReadString(ref reader, true);");
            w.Outdent();
            w.Line();

            w.Line("/// <inheritdoc/>");
            w.Open("public override void WriteAsPropertyName(" + Json + ".Utf8JsonWriter writer, " + e + " value, " + Json + ".JsonSerializerOptions options)");
            EmitJsonWriteSwitch(w, ctx, "WritePropertyName",
                "writer.WritePropertyName(((" + ctx.Underlying + ")value).ToString(" + Invariant + "));");
            w.Close();
            w.Line();
        }

        w.Line(AggressiveInlining);
        w.Open("private static " + e + " ReadString(ref " + Json + ".Utf8JsonReader reader, bool isPropertyName)");
        w.Line(e + " result;");
        w.Line("if (!reader.HasValueSequence && " + notEscaped + " && " + ctx.ExtensionClass + ".TryParseEnumMemberValueUtf8(reader.ValueSpan, out result))");
        w.Open();
        w.Line("return result;");
        w.Close();
        w.Line();
        w.Line("return ReadStringSlow(ref reader, isPropertyName);");
        w.Close();
        w.Line();

        var hasLenientUtf8 = lenientUtf8 is { Count: > 0 };
        if (hasLenientUtf8)
        {
            w.Open("private static bool TryParseLenientUtf8(" + SpanOfByte + " value, out " + e + " result)");
            EmitExactMatcher(w, lenientUtf8!, MatchMode.Utf8IgnoreCase);
            w.Line("result = " + ctx.Default + ";");
            w.Line("return false;");
            w.Close();
            w.Line();
        }

        w.Line(NoInlining);
        w.Open("private static " + e + " ReadStringSlow(ref " + Json + ".Utf8JsonReader reader, bool isPropertyName)");
        w.Line(e + " result;");
        w.Line(ctx.Underlying + " number;");

        if (hasLenientUtf8)
        {
            // Differently-cased text (camelCase JSON, PascalCase members) is matched on the raw bytes. Whatever
            // this misses continues below, which gives the same answer for everything this accepts.
            w.Line("if (!reader.HasValueSequence && " + notEscaped + " && TryParseLenientUtf8(reader.ValueSpan, out result))");
            w.Open();
            w.Line("return result;");
            w.Close();
            w.Line();
        }

        if (ctx.Options.HasCopyString)
        {
            // Unescape into a stack buffer instead of a new string. Unescaped UTF-16 is never longer than the
            // token's UTF-8 bytes, so a token that fits in bytes fits in chars; longer ones use the string path.
            var bufferLength = SlowReadBufferLength.ToString(CultureInfo.InvariantCulture);
            var numberText = ctx.Options.HasSpanNumberParsing ? "chars" : "chars.ToString()";
            w.Open("if ((reader.HasValueSequence ? reader.ValueSequence.Length : reader.ValueSpan.Length) <= " + bufferLength + ")");
            w.Line("global::System.Span<char> buffer = stackalloc char[" + bufferLength + "];");
            w.Line(SpanOfChar + " chars = buffer.Slice(0, reader.CopyString(buffer));");
            w.Line("if (" + TryParseLenient(ctx, "chars") + ")");
            w.Open();
            w.Line("return result;");
            w.Close();
            w.Line();
            w.Line("if (isPropertyName && " + ctx.Underlying + ".TryParse(" + numberText + ", " + numberStyles + ", " + Invariant + ", out number))");
            w.Open();
            w.Line("return (" + e + ")number;");
            w.Close();
            w.Line();
            w.Line("throw new " + Json + ".JsonException();");
            w.Close();
            w.Line();
        }

        w.Line(ctx.NullableString + " text = reader.GetString();");
        w.Line("if (" + TryParseLenient(ctx, "text") + ")");
        w.Open();
        w.Line("return result;");
        w.Close();
        w.Line();
        w.Line("if (isPropertyName && text != null && " + ctx.Underlying + ".TryParse(text, " + numberStyles + ", " + Invariant + ", out number))");
        w.Open();
        w.Line("return (" + e + ")number;");
        w.Close();
        w.Line();
        w.Line("throw new " + Json + ".JsonException();");
        w.Close();

        w.Close();
        w.Line();
    }

    /// <summary>Case-insensitive serialized names, then case-insensitive member names (when those are different sets).</summary>
    private static string TryParseLenient(Context ctx, string argument)
    {
        var test = ctx.ExtensionClass + ".TryParseEnumMemberValue(" + argument + ", true, out result)";
        return ctx.SerializedNamesAreNames
            ? test
            : test + " || " + ctx.ExtensionClass + ".TryParse(" + argument + ", true, out result)";
    }

    private static void EmitJsonWriteSwitch(SourceWriter w, Context ctx, string method, string fallback)
    {
        // Select the pre-encoded text, then make a single writer call: one call site keeps the method small
        // (WriteStringValue would otherwise be inlined into every case).
        if (ctx.Distinct.Count == 0)
        {
            w.Line(fallback); // empty enum: every value is undefined
            return;
        }

        if (ctx.Layout is { UseTable: true, IsContiguous: true } layout)
        {
            w.Line("var texts = s_texts;");
            w.Line("var index = " + layout.Offset + ";");
            w.Open("if (index < unchecked((" + layout.OffsetType + ")texts.Length))");
            w.Line("writer." + method + "(" + (layout.OffsetType == "uint" ? "texts[index]" : "texts[unchecked((int)index)]") + ");");
            w.Line("return;");
            w.Close();
            w.Line();
            w.Line(fallback);
            return;
        }

        w.Line(Json + ".JsonEncodedText text;");
        w.Open("switch (value)");
        for (var i = 0; i < ctx.Distinct.Count; i++)
        {
            w.Line("case " + ctx.Distinct[i].Expression + ": text = s_text" + i.ToString(CultureInfo.InvariantCulture) + "; break;");
        }

        w.Line("default:");
        w.Indent();
        w.Line(fallback);
        w.Line("return;");
        w.Outdent();
        w.Close();
        w.Line();
        w.Line("writer." + method + "(text);");
    }

    /// <summary>
    /// The package version this generator was built as, for <c>[GeneratedCode]</c>. Read from the assembly so a
    /// release never has to remember to update a constant; the "+commit" suffix is dropped.
    /// </summary>
    private static string GetVersion()
    {
        var assembly = typeof(Emitter).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (version is null) return assembly.GetName().Version?.ToString(3) ?? "0.0.0";

        var metadata = version.IndexOf('+');
        return metadata < 0 ? version : version.Substring(0, metadata);
    }

    private static List<Member> DistinctByValue(List<Member> members)
    {
        var seen = new HashSet<ulong>();
        var result = new List<Member>(members.Count);
        foreach (var member in members)
        {
            if (seen.Add(member.Bits)) result.Add(member);
        }

        return result;
    }

    private static List<Candidate> DistinctByToken(List<Candidate> candidates, StringComparer comparer)
    {
        var seen = new HashSet<string>(comparer);
        var result = new List<Candidate>(candidates.Count);
        foreach (var candidate in candidates)
        {
            if (seen.Add(candidate.Token)) result.Add(new Candidate(candidate.Token, candidate.Expression));
        }

        return result;
    }
}
