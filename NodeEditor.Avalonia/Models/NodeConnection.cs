namespace NodeEditor.Avalonia.Models;

public sealed class NodeConnection : IEquatable<NodeConnection>
{
    public Guid FromNodeId { get; }
    public int FromOutputIndex { get; }
    public Guid ToNodeId { get; }
    public int ToInputIndex { get; }

    public NodeConnection(Guid fromNodeId, int fromOutputIndex, Guid toNodeId, int toInputIndex)
    {
        FromNodeId = fromNodeId;
        FromOutputIndex = fromOutputIndex;
        ToNodeId = toNodeId;
        ToInputIndex = toInputIndex;
    }

    public bool Equals(NodeConnection? other)
    {
        return other != null &&
               FromNodeId == other.FromNodeId &&
               FromOutputIndex == other.FromOutputIndex &&
               ToNodeId == other.ToNodeId &&
               ToInputIndex == other.ToInputIndex;
    }

    public override bool Equals(object? obj) => Equals(obj as NodeConnection);

    public override int GetHashCode() => HashCode.Combine(FromNodeId, FromOutputIndex, ToNodeId, ToInputIndex);
}
