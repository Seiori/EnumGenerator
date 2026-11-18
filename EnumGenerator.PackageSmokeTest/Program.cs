using System.Reflection;
using System.Text.Json;
using Seiori.EnumGenerator;

// Exercises one of everything the package generates. Exits non-zero on the first mismatch, failing the pipeline.
var failures = 0;
var options = new JsonSerializerOptions { Converters = { StatusExtensions.JsonConverter } };

Check("GetString", Status.Active.GetString(), "Active");
Check("GetDisplayValue", Status.Pending.GetDisplayValue(), "Ready to Start");
Check("GetDescription", Status.Pending.GetDescription(), "Created, but nobody has begun.");
Check("GetEnumMemberValue", Status.Pending.GetEnumMemberValue(), "pending_status");
Check("IsDefined", StatusExtensions.IsDefined((Status)42), false);
Check("TryParse", StatusExtensions.TryParse("Active", out var parsed) && parsed == Status.Active, true);
Check("GetValues", StatusExtensions.GetValues().Length, 2);
Check("JSON write", JsonSerializer.Serialize(Status.Pending, options), "\"pending_status\"");
Check("JSON read", JsonSerializer.Deserialize<Status>("\"ACTIVE\"", options), Status.Active);
Check("Flags", (Access.Read | Access.Write).GetString(), "Read, Write");

// The package is compile-time only: its attributes must not survive into this assembly, nor a reference to it.
Check("attributes in metadata", typeof(Status).GetCustomAttributes(false).Length, 0);
Check("reference to the generator",
    Assembly.GetExecutingAssembly().GetReferencedAssemblies().Any(a => a.Name == "Seiori.EnumGenerator"), false);

Console.WriteLine(failures == 0 ? "Package smoke test passed." : $"Package smoke test FAILED: {failures} check(s).");
return failures;

void Check<T>(string what, T actual, T expected)
{
    if (EqualityComparer<T>.Default.Equals(actual, expected)) return;
    failures++;
    Console.Error.WriteLine($"{what}: expected '{expected}', got '{actual}'");
}

[EnumExtensions]
public enum Status
{
    [EnumDisplay("Ready to Start")]
    [EnumDescription("Created, but nobody has begun.")]
    [EnumJson("pending_status")]
    Pending,
    Active,
}

[Flags]
[EnumExtensions]
public enum Access
{
    None = 0,
    Read = 1,
    Write = 2,
    Execute = 4,
}
