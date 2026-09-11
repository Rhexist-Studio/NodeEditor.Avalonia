namespace NodeEditor.Avalonia.Models;

public sealed class NodePinDefinition
{
    public string Name { get; }
    public int Index { get; }

    public NodePinDefinition(string name, int index)
    {
        Name = name;
        Index = index;
    }
}
