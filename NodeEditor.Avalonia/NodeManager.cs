using System.Text.Json;
using Avalonia.Media;
using NodeEditor.Avalonia.Models;

namespace NodeEditor.Avalonia;

public sealed class NodeManager
{
    private readonly Dictionary<string, NodeDefinition> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, NodeDefinition> _byNamespace = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, NodeInstance> _instances = [];
    private readonly List<NodeConnection> _connections = [];

    public IReadOnlyCollection<NodeDefinition> Definitions => _byName.Values;
    public IReadOnlyCollection<NodeInstance> Instances => _instances.Values;
    public IReadOnlyList<NodeConnection> Connections => _connections;

    public event Action<NodeInstance>? NodeCreated;
    public event Action<NodeInstance>? NodeRemoved;
    public event Action? Changed;

    /// <summary>
    /// 注册节点类型。name 如 SendMessage，导出类型为 node:sendMessage；title 为标题栏文字；titleColor 为标题栏颜色；inputs/outputs 为引脚名，顺序对应端点 :0,:1,:2...
    /// </summary>
    public NodeDefinition Register(
        string name,
        string title,
        Color titleColor,
        IReadOnlyList<string>? inputs = null,
        IReadOnlyList<string>? outputs = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("name is required", nameof(name));
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("title is required", nameof(title));

        var trimmed = name.Trim();
        if (_byName.ContainsKey(trimmed))
            throw new InvalidOperationException($"node '{trimmed}' already registered");

        var ns = ToNamespace(trimmed);
        if (_byNamespace.ContainsKey(ns))
            throw new InvalidOperationException($"namespace '{ns}' already registered");

        var inputPins = ToPins(inputs);
        var outputPins = ToPins(outputs);
        var definition = new NodeDefinition(trimmed, ns, title, titleColor, inputPins, outputPins);
        _byName[trimmed] = definition;
        _byNamespace[ns] = definition;
        return definition;
    }

    /// <summary>
    /// 注册节点类型，titleColor 为可解析颜色字符串，如 #8B1E1E 或 DarkRed。其余参数与 Register(name, title, Color, ...) 相同。
    /// </summary>
    public NodeDefinition Register(
        string name,
        string title,
        string titleColor,
        IReadOnlyList<string>? inputs = null,
        IReadOnlyList<string>? outputs = null)
    {
        return Register(name, title, Color.Parse(titleColor), inputs, outputs);
    }

    /// <summary>
    /// 按注册名或命名空间（如 SendMessage / node:sendMessage）创建画布实例，返回带 uuid 的节点。
    /// </summary>
    public NodeInstance Create(string typeName, double x, double y)
    {
        var definition = Resolve(typeName) ?? throw new InvalidOperationException($"node '{typeName}' is not registered");
        var instance = new NodeInstance(Guid.NewGuid(), definition, x, y);
        _instances[instance.Id] = instance;
        NodeCreated?.Invoke(instance);
        Changed?.Invoke();
        return instance;
    }

    /// <summary>
    /// 连接 fromNodeId 的第 fromOutputIndex 个输出，到 toNodeId 的第 toInputIndex 个输入。同一输出可接多个输入，同一输入只保留一条来源。
    /// </summary>
    public NodeConnection Connect(Guid fromNodeId, int fromOutputIndex, Guid toNodeId, int toInputIndex)
    {
        if (fromNodeId == toNodeId)
            throw new InvalidOperationException("cannot connect a node to itself");
        if (!_instances.TryGetValue(fromNodeId, out var from))
            throw new InvalidOperationException($"node '{fromNodeId}' not found");
        if (!_instances.TryGetValue(toNodeId, out var to))
            throw new InvalidOperationException($"node '{toNodeId}' not found");
        if (fromOutputIndex < 0 || fromOutputIndex >= from.Definition.Outputs.Count)
            throw new ArgumentOutOfRangeException(nameof(fromOutputIndex));
        if (toInputIndex < 0 || toInputIndex >= to.Definition.Inputs.Count)
            throw new ArgumentOutOfRangeException(nameof(toInputIndex));

        _connections.RemoveAll(c => c.ToNodeId == toNodeId && c.ToInputIndex == toInputIndex);
        var exists = _connections.Exists(c =>
            c.FromNodeId == fromNodeId &&
            c.FromOutputIndex == fromOutputIndex &&
            c.ToNodeId == toNodeId &&
            c.ToInputIndex == toInputIndex);
        if (exists)
            return _connections.First(c =>
                c.FromNodeId == fromNodeId &&
                c.FromOutputIndex == fromOutputIndex &&
                c.ToNodeId == toNodeId &&
                c.ToInputIndex == toInputIndex);

        var connection = new NodeConnection(fromNodeId, fromOutputIndex, toNodeId, toInputIndex);
        _connections.Add(connection);
        Changed?.Invoke();
        return connection;
    }

    /// <summary>
    /// 断开端点连线。isOutput 为 true 时 pinIndex 是输出序号（清掉该输出的全部扇出），否则是输入序号。
    /// </summary>
    public void Disconnect(Guid nodeId, int pinIndex, bool isOutput)
    {
        var removed = isOutput
            ? _connections.RemoveAll(c => c.FromNodeId == nodeId && c.FromOutputIndex == pinIndex)
            : _connections.RemoveAll(c => c.ToNodeId == nodeId && c.ToInputIndex == pinIndex);
        if (removed > 0)
            Changed?.Invoke();
    }

    /// <summary>
    /// 断开指定的那一条连线，不影响同一输出上的其它扇出。
    /// </summary>
    public bool Disconnect(NodeConnection connection)
    {
        var removed = _connections.RemoveAll(c =>
            c.FromNodeId == connection.FromNodeId &&
            c.FromOutputIndex == connection.FromOutputIndex &&
            c.ToNodeId == connection.ToNodeId &&
            c.ToInputIndex == connection.ToInputIndex);
        if (removed > 0)
            Changed?.Invoke();
        return removed > 0;
    }

    /// <summary>
    /// 按 uuid 删除节点实例，并移除与它相关的全部连线。
    /// </summary>
    public bool Remove(Guid id)
    {
        if (!_instances.Remove(id, out var instance))
            return false;
        _connections.RemoveAll(c => c.FromNodeId == id || c.ToNodeId == id);
        NodeRemoved?.Invoke(instance);
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// 按 uuid 取画布上的节点实例，没有则返回 null。
    /// </summary>
    public NodeInstance? GetInstance(Guid id)
    {
        return _instances.TryGetValue(id, out var instance) ? instance : null;
    }

    /// <summary>
    /// 按注册名或命名空间取节点类型定义，没有则返回 null。
    /// </summary>
    public NodeDefinition? GetDefinition(string typeName)
    {
        return Resolve(typeName);
    }

    /// <summary>
    /// 导出节点列表。type 为 node:sendMessage；inputs 每项为来源 uuid:输出序号或 null；outputs 每项为扇出列表，元素为 目标uuid:输入序号。
    /// </summary>
    public IReadOnlyList<ExportedNode> Export()
    {
        var result = new List<ExportedNode>(_instances.Count);
        foreach (var instance in _instances.Values)
        {
            var inputs = new string?[instance.Definition.Inputs.Count];
            var outputs = Enumerable.Range(0, instance.Definition.Outputs.Count)
                .Select(_ => new List<string>())
                .ToArray();
            foreach (var connection in _connections)
            {
                if (connection.FromNodeId == instance.Id)
                    outputs[connection.FromOutputIndex].Add($"{connection.ToNodeId}:{connection.ToInputIndex}");
                if (connection.ToNodeId == instance.Id)
                    inputs[connection.ToInputIndex] = $"{connection.FromNodeId}:{connection.FromOutputIndex}";
            }

            result.Add(new ExportedNode
            {
                Id = instance.Id.ToString(),
                Type = instance.Definition.Namespace,
                Title = instance.Definition.Title,
                X = instance.X,
                Y = instance.Y,
                Inputs = inputs,
                Outputs = outputs
            });
        }

        return result;
    }

    /// <summary>
    /// 把当前图导出成 JSON 字符串，字段含义与 Export() 相同。
    /// </summary>
    public string ExportJson()
    {
        return JsonSerializer.Serialize(Export(), new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    private NodeDefinition? Resolve(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return null;
        var key = typeName.Trim();
        if (_byName.TryGetValue(key, out var byName))
            return byName;
        if (_byNamespace.TryGetValue(key, out var byNs))
            return byNs;
        return null;
    }

    private static string ToNamespace(string name)
    {
        var camel = char.ToLowerInvariant(name[0]) + name[1..];
        return "node:" + camel;
    }

    private static IReadOnlyList<NodePinDefinition> ToPins(IReadOnlyList<string>? names)
    {
        if (names == null || names.Count == 0)
            return [];
        var pins = new NodePinDefinition[names.Count];
        for (var i = 0; i < names.Count; i++)
            pins[i] = new NodePinDefinition(names[i], i);
        return pins;
    }
}
