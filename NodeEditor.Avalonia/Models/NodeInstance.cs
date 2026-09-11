namespace NodeEditor.Avalonia.Models;

public sealed class NodeInstance
{
    public Guid Id { get; }
    public NodeDefinition Definition { get; }
    public double X { get; set; }
    public double Y { get; set; }

    public NodeInstance(Guid id, NodeDefinition definition, double x, double y)
    {
        Id = id;
        Definition = definition;
        X = x;
        Y = y;
    }
}
