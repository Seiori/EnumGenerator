using System.Collections.Immutable;
using Basic.Reference.Assemblies;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Seiori.EnumGenerator;

namespace EnumGenerator.Tests.Generator;

public enum Target
{
    /// <summary>Modern consumer: spans, System.Text.Json, nullable annotations.</summary>
    Net100Latest,

    /// <summary>Modern framework, old language version: no nullable annotations or newer syntax.</summary>
    Net100CSharp73,

    /// <summary>.NET Framework-style consumer: no spans, no System.Text.Json.</summary>
    NetStandard20CSharp73,
}

internal sealed record RunResult(
    Compilation Output,
    ImmutableArray<Diagnostic> GeneratorDiagnostics,
    GeneratorDriverRunResult Run,
    GeneratorDriver Driver)
{
    public string GeneratedFor(string hintNameFragment) =>
        Run.GeneratedTrees.Single(t => t.FilePath.Contains(hintNameFragment, StringComparison.Ordinal)).ToString();
}

internal static class Harness
{
    public static CSharpCompilation CreateCompilation(Target target, params string[] sources)
    {
        var (references, language) = target switch
        {
            Target.Net100Latest => (Net100.References.All, LanguageVersion.Latest),
            Target.Net100CSharp73 => (Net100.References.All, LanguageVersion.CSharp7_3),
            _ => ((IEnumerable<MetadataReference>)NetStandard20.References.All, LanguageVersion.CSharp7_3),
        };

        // DocumentationMode.Diagnose surfaces malformed XML docs/crefs in the generated code.
        var parseOptions = new CSharpParseOptions(language, DocumentationMode.Diagnose);
        return CSharpCompilation.Create(
            "Consumer",
            sources.Select(s => CSharpSyntaxTree.ParseText(s, parseOptions)),
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: language >= LanguageVersion.CSharp8 ? NullableContextOptions.Enable : NullableContextOptions.Disable,
                // Generated arithmetic must be explicitly unchecked to survive <CheckForOverflowUnderflow>.
                checkOverflow: true));
    }

    public static GeneratorDriver CreateDriver(CSharpCompilation compilation) =>
        CSharpGeneratorDriver.Create(
            [new EnumExtensionsGenerator().AsSourceGenerator()],
            parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options,
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

    public static RunResult Run(Target target, params string[] sources)
    {
        var compilation = CreateCompilation(target, sources);
        var driver = CreateDriver(compilation)
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        return new RunResult(output, diagnostics, driver.GetRunResult(), driver);
    }

    /// <summary>A C# <c>\uXXXX</c> escape, built at runtime so this file stays plain ASCII.</summary>
    public static string Escape(string hex) => "\\" + "u" + hex;
}
