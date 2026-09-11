namespace NodeEditor.Avalonia.Models;

public sealed class ExportedNode
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public object? Value { get; set; }
    public IReadOnlyList<string?> Inputs { get; set; } = [];
    public IReadOnlyList<IReadOnlyList<string>> Outputs { get; set; } = [];
}
