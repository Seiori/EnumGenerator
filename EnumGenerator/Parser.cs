using System.Collections.Generic;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Seiori.EnumGenerator;

/// <summary>
/// Turns an attributed enum symbol into a value-equatable <see cref="EnumModel"/>. Roslyn re-runs this for every
/// attributed enum on every edit anywhere in the project, so it allocates only what ends up in the model.
/// </summary>
internal static class Parser
{
    private static readonly ParseResult Empty = new(null, null);

    public static ParseResult Parse(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        if (context.TargetSymbol is not INamedTypeSymbol { TypeKind: TypeKind.Enum } symbol
            || context.TargetNode is not EnumDeclarationSyntax declaration)
        {
            return Empty;
        }

        var displayName = GetDisplayName(symbol);

        // The extension class is always top-level, so every type in the chain must be reachable from it.
        var isPublic = true;
        for (var type = symbol; type is not null; type = type.ContainingType)
        {
            if (type.IsGenericType) return Fail(Diagnostics.GenericContainingType, declaration, displayName);
            if (type.ContainingType is null && IsFileLocal(type, cancellationToken))
            {
                return Fail(Diagnostics.InaccessibleEnum, declaration, displayName);
            }

            switch (type.DeclaredAccessibility)
            {
                case Accessibility.Public:
                    break;
                case Accessibility.Internal:
                case Accessibility.ProtectedOrInternal:
                    isPublic = false;
                    break;
                default:
                    return Fail(Diagnostics.InaccessibleEnum, declaration, displayName);
            }
        }

        string? className = null;
        string? classNamespace = null;
        foreach (var attribute in context.Attributes)
        {
            foreach (var argument in attribute.NamedArguments)
            {
                switch (argument.Key)
                {
                    case "ExtensionClassName":
                        className = argument.Value.Value as string;
                        break;
                    case "ExtensionClassNamespace":
                        classNamespace = argument.Value.Value as string;
                        break;
                }
            }
        }

        if (className is null)
        {
            className = GetDefaultClassName(symbol);
        }
        else if (!SyntaxFacts.IsValidIdentifier(className))
        {
            return Fail(Diagnostics.InvalidClassName, declaration, className);
        }

        string escapedNamespace;
        if (classNamespace is null)
        {
            var depth = 0;
            for (var ns = symbol.ContainingNamespace; ns is { IsGlobalNamespace: false }; ns = ns.ContainingNamespace)
            {
                depth++;
            }

            if (depth == 0)
            {
                escapedNamespace = string.Empty;
            }
            else
            {
                var segments = new string[depth];
                for (var ns = symbol.ContainingNamespace; ns is { IsGlobalNamespace: false }; ns = ns.ContainingNamespace)
                {
                    segments[--depth] = ns.Name;
                }

                escapedNamespace = EscapeNamespace(segments);
            }
        }
        else if (classNamespace.Length == 0)
        {
            escapedNamespace = string.Empty;
        }
        else
        {
            var segments = classNamespace.Split('.');
            foreach (var segment in segments)
            {
                if (!SyntaxFacts.IsValidIdentifier(segment))
                {
                    return Fail(Diagnostics.InvalidNamespace, declaration, classNamespace);
                }
            }

            escapedNamespace = EscapeNamespace(segments);
        }

        var (underlyingType, isSigned, size) = GetUnderlyingType(symbol.EnumUnderlyingType!.SpecialType);
        var fullyQualifiedName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        var isFlags = false;
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass is { Name: "FlagsAttribute" } attributeClass
                && IsInNamespace(attributeClass, "System"))
            {
                isFlags = true;
                break;
            }
        }

        var allMembers = symbol.GetMembers();
        var members = new List<MemberModel>(allMembers.Length);
        foreach (var member in allMembers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (member is not IFieldSymbol { HasConstantValue: true } field) continue;

            var bits = ToBits(field.ConstantValue!);

            // Three roles (display name, description, JSON name), each fed by one of the package's own
            // attributes or by the standard attributes that mean the same thing.
            string? ownDisplay = null, ownDescription = null, ownJson = null;
            string? memberDisplay = null, displayDescription = null, description = null;
            string? stjName = null, enumMember = null, jsonProperty = null;
            var obsoleteError = false;

            foreach (var attribute in field.GetAttributes())
            {
                var attributeClass = attribute.AttributeClass;
                if (attributeClass is null) continue;

                switch (attributeClass.Name)
                {
                    case AttributeSource.DisplayAttributeName when IsInNamespace(attributeClass, AttributeSource.Namespace):
                        ownDisplay = GetFirstConstructorString(attribute);
                        break;
                    case AttributeSource.DescriptionAttributeName when IsInNamespace(attributeClass, AttributeSource.Namespace):
                        ownDescription = GetFirstConstructorString(attribute);
                        break;
                    case AttributeSource.JsonAttributeName when IsInNamespace(attributeClass, AttributeSource.Namespace):
                        ownJson = GetFirstConstructorString(attribute);
                        break;
                    case "DisplayAttribute" when IsInNamespace(attributeClass, "System.ComponentModel.DataAnnotations"):
                        foreach (var argument in attribute.NamedArguments)
                        {
                            if (argument.Key == "Name") memberDisplay = argument.Value.Value as string;
                            else if (argument.Key == "Description") displayDescription = argument.Value.Value as string;
                        }

                        break;
                    case "DescriptionAttribute" when IsInNamespace(attributeClass, "System.ComponentModel"):
                        description = GetFirstConstructorString(attribute);
                        break;
                    case "JsonStringEnumMemberNameAttribute" when IsInNamespace(attributeClass, "System.Text.Json.Serialization"):
                        stjName = GetFirstConstructorString(attribute);
                        break;
                    case "EnumMemberAttribute" when IsInNamespace(attributeClass, "System.Runtime.Serialization"):
                        enumMember = GetNamedString(attribute, "Value");
                        break;
                    case "JsonPropertyNameAttribute" when IsInNamespace(attributeClass, "System.Text.Json.Serialization"):
                        jsonProperty = GetFirstConstructorString(attribute);
                        break;
                    case "ObsoleteAttribute" when IsInNamespace(attributeClass, "System"):
                        obsoleteError = attribute.ConstructorArguments.Length >= 2
                                        && attribute.ConstructorArguments[1].Value is true;
                        break;
                }
            }

            // The package's own attribute wins within each role.
            members.Add(new MemberModel(
                field.Name,
                obsoleteError,
                bits,
                ownDisplay ?? memberDisplay,
                ownDescription ?? displayDescription ?? description,
                ownJson ?? stjName ?? enumMember ?? jsonProperty));
        }

        var model = new EnumModel(
            HintName: GetHintName(symbol),
            FullyQualifiedName: fullyQualifiedName,
            DisplayName: displayName,
            Namespace: escapedNamespace,
            ClassName: Literals.EscapeIdentifier(className),
            IsPublic: isPublic,
            UnderlyingType: underlyingType,
            IsSigned: isSigned,
            UnderlyingSize: size,
            IsFlags: isFlags,
            Members: new EquatableArray<MemberModel>(members.ToArray()));

        return new ParseResult(model, null);
    }

    private static ParseResult Fail(DiagnosticDescriptor descriptor, EnumDeclarationSyntax declaration, string argument) =>
        new(null, new DiagnosticInfo(descriptor, LocationInfo.From(declaration.Identifier.GetLocation()), argument));

    private static bool IsFileLocal(INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is MemberDeclarationSyntax syntax)
            {
                foreach (var modifier in syntax.Modifiers)
                {
                    if (modifier.ValueText == "file") return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="type"/> is declared directly in the dotted namespace <paramref name="ns"/>. Walks
    /// the symbol chain against the text from the innermost segment outwards, so nothing is allocated.
    /// </summary>
    private static bool IsInNamespace(INamedTypeSymbol type, string ns)
    {
        var current = type.ContainingNamespace;
        var end = ns.Length;
        while (true)
        {
            if (current is null || current.IsGlobalNamespace) return false;

            var name = current.Name;
            var start = end - name.Length;
            if (start < 0 || string.CompareOrdinal(ns, start, name, 0, name.Length) != 0) return false;
            if (start == 0) return current.ContainingNamespace is { IsGlobalNamespace: true };
            if (ns[start - 1] != '.') return false;

            end = start - 1;
            current = current.ContainingNamespace;
        }
    }

    private static string? GetNamedString(AttributeData attribute, string name)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == name) return argument.Value.Value as string;
        }

        return null;
    }

    private static string? GetFirstConstructorString(AttributeData attribute) =>
        attribute.ConstructorArguments.Length > 0 ? attribute.ConstructorArguments[0].Value as string : null;

    private static string GetDisplayName(INamedTypeSymbol symbol)
    {
        var name = symbol.Name;
        for (var type = symbol.ContainingType; type is not null; type = type.ContainingType)
        {
            name = type.Name + "." + name;
        }

        return name;
    }

    private static string GetDefaultClassName(INamedTypeSymbol symbol)
    {
        var name = symbol.Name + "Extensions";
        for (var type = symbol.ContainingType; type is not null; type = type.ContainingType)
        {
            name = type.Name + name;
        }

        return name;
    }

    private static string EscapeNamespace(string[] segments)
    {
        for (var i = 0; i < segments.Length; i++)
        {
            segments[i] = Literals.EscapeIdentifier(segments[i]);
        }

        return string.Join(".", segments);
    }

    /// <summary>
    /// Unique per enum. Roslyn compares hint names case-insensitively, so a hash of the exact-case name is
    /// appended to keep enums that differ only by case (e.g. <c>Status</c> and <c>status</c>) apart.
    /// </summary>
    private static string GetHintName(INamedTypeSymbol symbol)
    {
        var path = symbol.Name;
        for (var type = symbol.ContainingType; type is not null; type = type.ContainingType)
        {
            path = type.Name + "+" + path;
        }

        // Raw names: ToDisplayString() would add '@' to keywords, which hint names reject.
        for (var ns = symbol.ContainingNamespace; ns is { IsGlobalNamespace: false }; ns = ns.ContainingNamespace)
        {
            path = ns.Name + "." + path;
        }

        // FNV-1a over UTF-16 code units: deterministic across processes, unlike string.GetHashCode.
        var hash = 2166136261u;
        foreach (var c in path)
        {
            hash = unchecked((hash ^ c) * 16777619u);
        }

        return new StringBuilder(path.Length + 32)
            .Append(path)
            .Append(".EnumExtensions.")
            .Append(hash.ToString("x8"))
            .Append(".g.cs")
            .ToString();
    }

    private static (string Keyword, bool IsSigned, int Size) GetUnderlyingType(SpecialType type) => type switch
    {
        SpecialType.System_SByte => ("sbyte", true, 1),
        SpecialType.System_Byte => ("byte", false, 1),
        SpecialType.System_Int16 => ("short", true, 2),
        SpecialType.System_UInt16 => ("ushort", false, 2),
        SpecialType.System_UInt32 => ("uint", false, 4),
        SpecialType.System_Int64 => ("long", true, 8),
        SpecialType.System_UInt64 => ("ulong", false, 8),
        _ => ("int", true, 4),
    };

    private static ulong ToBits(object value) => value switch
    {
        sbyte v => unchecked((ulong)v),
        byte v => v,
        short v => unchecked((ulong)v),
        ushort v => v,
        int v => unchecked((ulong)v),
        uint v => v,
        long v => unchecked((ulong)v),
        ulong v => v,
        _ => 0,
    };
}
