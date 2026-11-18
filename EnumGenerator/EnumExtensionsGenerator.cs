using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Seiori.EnumGenerator;

/// <summary>
/// Generates extension methods for enums annotated with <c>[Seiori.EnumGenerator.EnumExtensions]</c>.
/// </summary>
/// <remarks>
/// Pipeline cost is kept minimal for IDE responsiveness:
/// <list type="bullet">
/// <item><c>ForAttributeWithMetadataName</c> lets Roslyn skip every syntax tree that cannot contain the attribute.</item>
/// <item>The transform produces fully value-equatable models, so unrelated edits hit the cache and emit nothing.</item>
/// <item>Compilation-wide feature detection is reduced to a small equatable record before it is combined.</item>
/// </list>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class EnumExtensionsGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static ctx =>
            ctx.AddSource(AttributeSource.HintName, SourceText.From(AttributeSource.Source, Encoding.UTF8)));

        var options = context.CompilationProvider
            .Select(static (compilation, _) => GenerationOptions.From(compilation))
            .WithTrackingName(TrackingNames.Options);

        var results = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeSource.MetadataName,
                predicate: static (node, _) => node is EnumDeclarationSyntax,
                transform: static (ctx, ct) => Parser.Parse(ctx, ct))
            .WithTrackingName(TrackingNames.Parse);

        var diagnostics = results
            .Where(static result => result.Diagnostic is not null)
            .Select(static (result, _) => result.Diagnostic!);

        context.RegisterSourceOutput(diagnostics, static (spc, diagnostic) => spc.ReportDiagnostic(diagnostic.ToDiagnostic()));

        var models = results
            .Where(static result => result.Model is not null)
            .Select(static (result, _) => result.Model!)
            .WithTrackingName(TrackingNames.Models);

        context.RegisterSourceOutput(
            models.Combine(options).WithTrackingName(TrackingNames.Output),
            static (spc, pair) => spc.AddSource(
                pair.Left.HintName,
                SourceText.From(Emitter.Emit(pair.Left, pair.Right), Encoding.UTF8)));
    }
}

internal static class TrackingNames
{
    public const string Options = nameof(Options);
    public const string Parse = nameof(Parse);
    public const string Models = nameof(Models);
    public const string Output = nameof(Output);
}
