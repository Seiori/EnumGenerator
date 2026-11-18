using Seiori.EnumGenerator;

// Names chosen to collide with BCL types the generated code uses.
namespace EnumGenerator.Tests.Enums.Collisions
{
    [EnumExtensions]
    public enum Type
    {
        A,
        B,
    }

    [EnumExtensions]
    public enum JsonException
    {
        A,
    }
}

[EnumExtensions]
public enum GlobalNamespaceEnum
{
    A,
    B,
}
