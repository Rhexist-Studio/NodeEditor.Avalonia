namespace NodeEditor.Avalonia.Models;

public sealed class NodeInstance
{
    public Guid Id { get; }
    public NodeDefinition Definition { get; }
    public double X { get; set; }
    public double Y { get; set; }
    public object? Value { get; set; }

    public NodeInstance(Guid id, NodeDefinition definition, double x, double y, object? value = null)
    {
        Id = id;
        Definition = definition;
        X = x;
        Y = y;
        Value = value ?? DefaultValue(definition.ValueKind);
    }

    /// <summary>
    /// 按数据源类型给出默认值，非数据源节点返回 null。
    /// </summary>
    public static object? DefaultValue(NodeValueKind? kind) => kind switch
    {
        NodeValueKind.Int => 0,
        NodeValueKind.Long => 0L,
        NodeValueKind.Float => 0f,
        NodeValueKind.Double => 0d,
        NodeValueKind.String => "",
        NodeValueKind.Bool => false,
        _ => null
    };
}
