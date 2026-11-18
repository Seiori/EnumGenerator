using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace Seiori.EnumGenerator;

/// <summary>Formats values as C# source that is valid for every supported language version (C# 7.3+).</summary>
internal static class Literals
{
    public static string EscapeIdentifier(string name) =>
        SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

    /// <summary>A regular (non-verbatim) string literal. Escapes anything that is not safe to emit raw.</summary>
    public static string String(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                default:
                    if (NeedsUnicodeEscape(c)) AppendUnicodeEscape(sb, c);
                    else sb.Append(c);
                    break;
            }
        }

        return sb.Append('"').ToString();
    }

    public static string Char(char c)
    {
        switch (c)
        {
            case '\'': return "'\\''";
            case '\\': return "'\\\\'";
        }

        if (c is >= ' ' and <= '~') return "'" + c + "'";
        var sb = new StringBuilder(8).Append('\'');
        AppendUnicodeEscape(sb, c);
        return sb.Append('\'').ToString();
    }

    public static string Byte(byte b) => "0x" + b.ToString("X2", CultureInfo.InvariantCulture);

    public static string Int32(int value) =>
        value == int.MinValue ? "(-2147483647 - 1)" : value.ToString(CultureInfo.InvariantCulture);

    public static string Int64(long value) =>
        value == long.MinValue ? "(-9223372036854775807L - 1L)" : value.ToString(CultureInfo.InvariantCulture) + "L";

    public static string UInt64(ulong value) => value.ToString(CultureInfo.InvariantCulture) + "UL";

    public static string UInt32(uint value) => value.ToString(CultureInfo.InvariantCulture) + "u";

    /// <summary>A constant expression of the enum type with the given value, without naming a member.</summary>
    public static string EnumCast(string enumType, string underlyingType, bool isSigned, ulong bits)
    {
        var number = isSigned ? Int64(unchecked((long)bits)) : UInt64(bits);
        return "((" + enumType + ")(" + underlyingType + ")" + number + ")";
    }

    private static bool NeedsUnicodeEscape(char c) =>
        c < 0x20
        || (c >= 0x7F && c <= 0x9F)
        || c == 0x2028 // line separator: would end a C# string literal
        || c == 0x2029 // paragraph separator: likewise
        || c == 0xFEFF // byte-order mark: invisible
        || char.IsSurrogate(c);

    private static void AppendUnicodeEscape(StringBuilder sb, char c) =>
        sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
}
