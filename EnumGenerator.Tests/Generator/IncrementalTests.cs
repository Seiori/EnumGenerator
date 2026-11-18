using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Seiori.EnumGenerator;
using Xunit;

namespace EnumGenerator.Tests.Generator;

/// <summary>
/// The generator runs on every keystroke in the IDE. These tests pin down that unrelated or cosmetic edits are
/// served from cache instead of re-emitting source (1.x used ImmutableArray in its model, which compares by
/// reference, so every edit regenerated everything).
/// </summary>
public class IncrementalTests
{
    private const string EnumSource = """
        using Seiori.EnumGenerator;
        using System.ComponentModel;
        namespace App
        {
            [EnumExtensions]
            public enum Color { [Description("Rouge")] Red, Green, Blue }
        }
        """;

    private const string OtherSource = "namespace App { public class Foo { public void M() { } } }";

    [Fact]
    public void EditingAnotherFile_IsFullyCached()
    {
        var (driver, compilation) = Start();
        var other = compilation.SyntaxTrees.Last();

        var edited = compilation.ReplaceSyntaxTree(
            other, other.WithChangedText(SourceText.From("namespace App { public class Foo { public void M() { var x = 1; } } }")));
        var result = driver.RunGenerators(edited).GetRunResult();

        AssertOutputsReusedFromCache(result);
    }

    [Fact]
    public void CosmeticEditToTheEnumFile_ProducesAnEqualModel()
    {
        var (driver, compilation) = Start();
        var enumTree = compilation.SyntaxTrees.First();

        var edited = compilation.ReplaceSyntaxTree(
            enumTree, enumTree.WithChangedText(SourceText.From("// a comment\n" + EnumSource.Replace("Green,", "Green ,"))));
        var result = driver.RunGenerators(edited).GetRunResult();

        AssertOutputsReusedFromCache(result);
    }

    [Fact]
    public void ChangingTheEnum_Regenerates()
    {
        var (driver, compilation) = Start();
        var enumTree = compilation.SyntaxTrees.First();

        var edited = compilation.ReplaceSyntaxTree(
            enumTree, enumTree.WithChangedText(SourceText.From(EnumSource.Replace("Blue", "Blue, Yellow"))));
        var result = Assert.Single(driver.RunGenerators(edited).GetRunResult().Results);

        var reasons = result.TrackedSteps[TrackingNames.Models].SelectMany(s => s.Outputs).Select(o => o.Reason);
        Assert.Contains(IncrementalStepRunReason.Modified, reasons);
        Assert.Contains("Yellow", result.GeneratedSources.Single(s => s.HintName.StartsWith("App.Color.", StringComparison.Ordinal)).SourceText.ToString());
    }

    [Fact]
    public void ChangingARoleAttribute_Regenerates()
    {
        const string source = """
            using Seiori.EnumGenerator;
            namespace App
            {
                [EnumExtensions]
                public enum Color { [EnumJson("r")] Red, Green }
            }
            """;
        var compilation = Harness.CreateCompilation(Target.Net100Latest, source, OtherSource);
        var driver = Harness.CreateDriver(compilation).RunGenerators(compilation);
        var enumTree = compilation.SyntaxTrees.First();

        var edited = compilation.ReplaceSyntaxTree(
            enumTree, enumTree.WithChangedText(SourceText.From(source.Replace("\"r\"", "\"rouge\""))));
        var result = Assert.Single(driver.RunGenerators(edited).GetRunResult().Results);

        var reasons = result.TrackedSteps[TrackingNames.Models].SelectMany(s => s.Outputs).Select(o => o.Reason);
        Assert.Contains(IncrementalStepRunReason.Modified, reasons);
        Assert.Contains("\"rouge\"", result.GeneratedSources.Single(s => s.HintName.StartsWith("App.Color.", StringComparison.Ordinal)).SourceText.ToString());
    }

    [Fact]
    public void ModelsFromSeparateCompilations_AreValueEqual()
    {
        var first = Harness.Run(Target.Net100Latest, EnumSource).Run.Results[0];
        var second = Harness.Run(Target.Net100Latest, EnumSource).Run.Results[0];

        var a = first.TrackedSteps[TrackingNames.Models].SelectMany(s => s.Outputs).Single().Value;
        var b = second.TrackedSteps[TrackingNames.Models].SelectMany(s => s.Outputs).Single().Value;

        Assert.NotSame(a, b);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    private static (GeneratorDriver Driver, CSharpCompilation Compilation) Start()
    {
        var compilation = Harness.CreateCompilation(Target.Net100Latest, EnumSource, OtherSource);
        var driver = Harness.CreateDriver(compilation).RunGenerators(compilation);
        return (driver, compilation);
    }

    private static void AssertOutputsReusedFromCache(GeneratorDriverRunResult run)
    {
        var result = Assert.Single(run.Results);

        foreach (var output in result.TrackedSteps[TrackingNames.Models].SelectMany(s => s.Outputs))
        {
            Assert.True(output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged, $"Models: {output.Reason}");
        }

        foreach (var output in result.TrackedOutputSteps.SelectMany(kv => kv.Value).SelectMany(s => s.Outputs))
        {
            Assert.True(output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged, $"Output: {output.Reason}");
        }
    }
}
