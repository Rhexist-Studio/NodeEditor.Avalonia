namespace NodeEditor.Avalonia.Models;

public sealed class NodePinView
{
    public Guid NodeId { get; }
    public int Index { get; }
    public string Name { get; }
    public bool IsOutput { get; }
    public bool ShowName { get; }

    public NodePinView(Guid nodeId, int index, string name, bool isOutput, bool showName = true)
    {
        NodeId = nodeId;
        Index = index;
        Name = name;
        IsOutput = isOutput;
        ShowName = showName;
    }
}
