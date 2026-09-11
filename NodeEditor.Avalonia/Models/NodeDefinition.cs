using Avalonia.Media;

namespace NodeEditor.Avalonia.Models;

public sealed class NodeDefinition
{
    public string Name { get; }
    public string Namespace { get; }
    public string Title { get; }
    public Color TitleColor { get; }
    public IReadOnlyList<NodePinDefinition> Inputs { get; }
    public IReadOnlyList<NodePinDefinition> Outputs { get; }

    public NodeDefinition(
        string name,
        string ns,
        string title,
        Color titleColor,
        IReadOnlyList<NodePinDefinition> inputs,
        IReadOnlyList<NodePinDefinition> outputs)
    {
        Name = name;
        Namespace = ns;
        Title = title;
        TitleColor = titleColor;
        Inputs = inputs;
        Outputs = outputs;
    }
}
