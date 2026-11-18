using System.Text;

namespace Seiori.EnumGenerator;

internal sealed class SourceWriter
{
    private readonly StringBuilder _builder = new(8192);
    private int _indent;

    public void Line()
    {
        _builder.Append('\n');
    }

    public void Line(string text)
    {
        _builder.Append(' ', _indent * 4).Append(text).Append('\n');
    }

    public void Open(string header)
    {
        Line(header);
        Line("{");
        _indent++;
    }

    public void Open()
    {
        Line("{");
        _indent++;
    }

    public void Close(string suffix = "")
    {
        _indent--;
        Line("}" + suffix);
    }

    public void Indent() => _indent++;

    public void Outdent() => _indent--;

    public override string ToString() => _builder.ToString();
}
