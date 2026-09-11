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
    /// 注册数据源节点。kind 决定可编辑值和导出类型，只有一个输出端点 Value，无输入。
    /// </summary>
    public NodeDefinition RegisterDataSource(string name, string title, Color titleColor, NodeValueKind kind)
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

        var definition = new NodeDefinition(
            trimmed,
            ns,
            title,
            titleColor,
            [],
            [new NodePinDefinition("Value", 0)],
            kind);
        _byName[trimmed] = definition;
        _byNamespace[ns] = definition;
        return definition;
    }

    /// <summary>
    /// 注册数据源节点，titleColor 为可解析颜色字符串。其余参数与 RegisterDataSource(name, title, Color, kind) 相同。
    /// </summary>
    public NodeDefinition RegisterDataSource(string name, string title, string titleColor, NodeValueKind kind)
    {
        return RegisterDataSource(name, title, Color.Parse(titleColor), kind);
    }

    /// <summary>
    /// 一次性注册常用数据源：Int / Long / Float / Double / String / Bool，导出类型为 node:int 这类命名空间。
    /// </summary>
    public void RegisterCommonDataSources()
    {
        RegisterDataSource("Int", "Int", "#2F6FED", NodeValueKind.Int);
        RegisterDataSource("Long", "Long", "#1F5FBF", NodeValueKind.Long);
        RegisterDataSource("Float", "Float", "#7A4AE0", NodeValueKind.Float);
        RegisterDataSource("Double", "Double", "#5B32B8", NodeValueKind.Double);
        RegisterDataSource("String", "String", "#C46B1A", NodeValueKind.String);
        RegisterDataSource("Bool", "Bool", "#2E8B57", NodeValueKind.Bool);
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
        return Create(typeName, x, y, Guid.NewGuid(), null);
    }

    /// <summary>
    /// 用指定 uuid 和数据源值创建节点。id 冲突会抛错；value 按节点类型转换。
    /// </summary>
    public NodeInstance Create(string typeName, double x, double y, Guid id, object? value)
    {
        var definition = Resolve(typeName) ?? throw new InvalidOperationException($"node '{typeName}' is not registered");
        if (_instances.ContainsKey(id))
            throw new InvalidOperationException($"node '{id}' already exists");
        var instance = new NodeInstance(id, definition, x, y, ConvertValue(definition.ValueKind, value));
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
            var inputs = Enumerable.Repeat<string?>(null, instance.Definition.Inputs.Count).ToList();
            var outputs = Enumerable.Range(0, instance.Definition.Outputs.Count)
                .Select(_ => new List<string>())
                .ToList();
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
                Value = instance.Value,
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
        return JsonSerializer.Serialize(Export(), JsonOptions);
    }

    /// <summary>
    /// 清空画布上全部节点和连线。
    /// </summary>
    public void Clear()
    {
        foreach (var id in _instances.Keys.ToList())
            Remove(id);
    }

    /// <summary>
    /// 用 Export() 同结构的 JSON 还原节点、位置、数据源值和连线。已注册类型缺失会跳过该节点。
    /// </summary>
    public void ImportJson(string json)
    {
        var nodes = JsonSerializer.Deserialize<List<ExportedNode>>(json, JsonOptions) ?? [];
        Import(nodes);
    }

    /// <summary>
    /// 用导出节点列表还原视图，先清空再按 uuid 重建并接线。
    /// </summary>
    public void Import(IReadOnlyList<ExportedNode> nodes)
    {
        Clear();
        foreach (var node in nodes)
        {
            if (Resolve(node.Type) == null)
                continue;
            var id = Guid.TryParse(node.Id, out var parsed) ? parsed : Guid.NewGuid();
            Create(node.Type, node.X, node.Y, id, node.Value);
        }

        foreach (var node in nodes)
        {
            if (!Guid.TryParse(node.Id, out var fromId) || !_instances.ContainsKey(fromId))
                continue;
            RestoreConnections(fromId, node);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private void RestoreConnections(Guid fromId, ExportedNode node)
    {
        if (node.Outputs is { Count: > 0 })
        {
            for (var i = 0; i < node.Outputs.Count; i++)
            {
                foreach (var target in node.Outputs[i])
                {
                    if (TryParseRef(target, out var toId, out var toIndex))
                        TryConnect(fromId, i, toId, toIndex);
                }
            }

            return;
        }

        if (node.Inputs == null)
            return;
        for (var i = 0; i < node.Inputs.Count; i++)
        {
            if (!TryParseRef(node.Inputs[i], out var fromNodeId, out var fromOutput))
                continue;
            TryConnect(fromNodeId, fromOutput, fromId, i);
        }
    }

    private void TryConnect(Guid fromNodeId, int fromOutputIndex, Guid toNodeId, int toInputIndex)
    {
        try
        {
            Connect(fromNodeId, fromOutputIndex, toNodeId, toInputIndex);
        }
        catch (Exception)
        {
        }
    }

    private static bool TryParseRef(string? value, out Guid id, out int index)
    {
        id = default;
        index = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var sep = value.LastIndexOf(':');
        if (sep <= 0 || sep == value.Length - 1)
            return false;
        return Guid.TryParse(value[..sep], out id) && int.TryParse(value[(sep + 1)..], out index);
    }

    private static object? ConvertValue(NodeValueKind? kind, object? value)
    {
        if (kind == null)
            return null;
        if (value is JsonElement element)
            return ConvertJson(kind.Value, element);
        if (value == null)
            return NodeInstance.DefaultValue(kind);
        return kind switch
        {
            NodeValueKind.Int => Convert.ToInt32(value),
            NodeValueKind.Long => Convert.ToInt64(value),
            NodeValueKind.Float => Convert.ToSingle(value),
            NodeValueKind.Double => Convert.ToDouble(value),
            NodeValueKind.Bool => Convert.ToBoolean(value),
            NodeValueKind.String => Convert.ToString(value) ?? "",
            _ => value
        };
    }

    private static object ConvertJson(NodeValueKind kind, JsonElement element)
    {
        return kind switch
        {
            NodeValueKind.Int => element.ValueKind == JsonValueKind.Number ? element.GetInt32() : 0,
            NodeValueKind.Long => element.ValueKind == JsonValueKind.Number ? element.GetInt64() : 0L,
            NodeValueKind.Float => element.ValueKind == JsonValueKind.Number ? element.GetSingle() : 0f,
            NodeValueKind.Double => element.ValueKind == JsonValueKind.Number ? element.GetDouble() : 0d,
            NodeValueKind.Bool => element.ValueKind == JsonValueKind.True ||
                                  (element.ValueKind == JsonValueKind.False ? false : element.GetBoolean()),
            _ => element.ValueKind == JsonValueKind.String ? element.GetString() ?? "" : element.ToString()
        };
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
