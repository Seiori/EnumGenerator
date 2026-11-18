using Microsoft.CodeAnalysis;

namespace Seiori.EnumGenerator;

internal static class Diagnostics
{
    private const string Category = "Seiori.EnumGenerator";

    public static readonly DiagnosticDescriptor InaccessibleEnum = new(
        id: "SEG001",
        title: "Enum is not accessible to generated code",
        messageFormat: "Cannot generate extensions for '{0}': the enum and all of its containing types must be public or internal (not private, protected or file-local)",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor GenericContainingType = new(
        id: "SEG002",
        title: "Enum is nested in a generic type",
        messageFormat: "Cannot generate extensions for '{0}': enums nested in generic types are not supported",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidClassName = new(
        id: "SEG003",
        title: "Invalid extension class name",
        messageFormat: "'{0}' is not a valid C# identifier for ExtensionClassName",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidNamespace = new(
        id: "SEG004",
        title: "Invalid extension class namespace",
        messageFormat: "'{0}' is not a valid C# namespace for ExtensionClassNamespace",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
