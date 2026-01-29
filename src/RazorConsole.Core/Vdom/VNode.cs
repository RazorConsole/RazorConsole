// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Core.Vdom;

public enum VNodeKind
{
    Component,
    Region,
    Element,
    Text,
}

public sealed class VNode : IEquatable<VNode>
{
    private readonly List<VNode> _children;
    private readonly Dictionary<string, object?> _attributes;
    private readonly Dictionary<string, VNodeEvent> _events;

    private VNode(VNodeKind kind, string? tagName = null, string? text = null, string? key = null)
    {
        Kind = kind;
        TagName = tagName;
        Text = text ?? string.Empty;
        Key = key;
        _children = new List<VNode>();
        _attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
        _events = new Dictionary<string, VNodeEvent>(StringComparer.OrdinalIgnoreCase);
        ID = Guid.NewGuid().ToString("N");
    }

    public VNodeKind Kind { get; }

    public string? TagName { get; private set; }

    public string Text { get; private set; }

    public string? Key { get; private set; }

    public string ID { get; private set; }

    public IReadOnlyList<VNode> Children => _children;

    public IReadOnlyDictionary<string, object?> Attributes => _attributes;

    public IReadOnlyCollection<VNodeEvent> Events => _events.Values;

    public static VNode CreateComponent() => new(VNodeKind.Component);

    public static VNode CreateRegion() => new(VNodeKind.Region);

    public static VNode CreateElement(string tagName, string? key = null)
    {
        if (string.IsNullOrWhiteSpace(tagName))
        {
            throw new ArgumentException("Tag name is required.", nameof(tagName));
        }

        return new VNode(VNodeKind.Element, tagName, key: key);
    }

    public static VNode CreateText(string? text)
        => new(VNodeKind.Text, text: text);

    public void AddChild(VNode child)
    {
        if (child is null)
        {
            throw new ArgumentNullException(nameof(child));
        }

        _children.Add(child);
    }

    public void InsertChild(int index, VNode child)
    {
        if (child is null)
        {
            throw new ArgumentNullException(nameof(child));
        }

        if ((uint)index > (uint)_children.Count)
        {
            index = _children.Count;
        }

        _children.Insert(index, child);
    }

    public void RemoveChildAt(int index)
    {
        if ((uint)index >= (uint)_children.Count)
        {
            return;
        }

        _children.RemoveAt(index);
    }

    public void SetAttribute<TValue>(string name, TValue? value)
    {
        if ((Kind != VNodeKind.Element && Kind != VNodeKind.Component) || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        _attributes[name] = value;
    }

    public void RemoveAttribute(string name)
    {
        if ((Kind != VNodeKind.Element && Kind != VNodeKind.Component) || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        _attributes.Remove(name);
    }

    public void SetEvent(string name, ulong handlerId, VNodeEventOptions? options = null)
    {
        if (Kind != VNodeKind.Element || string.IsNullOrWhiteSpace(name) || handlerId == 0)
        {
            return;
        }

        var resolvedOptions = options ?? new VNodeEventOptions(false, false);
        _events[name] = new VNodeEvent(name, handlerId, resolvedOptions);
    }

    public void RemoveEvent(string name)
    {
        if (Kind != VNodeKind.Element || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        _events.Remove(name);
    }

    public void SetText(string? text)
    {
        if (Kind == VNodeKind.Text)
        {
            Text = text ?? string.Empty;
        }
    }

    public void SetKey(string? key)
    {
        if (Kind == VNodeKind.Element || Kind == VNodeKind.Component)
        {
            Key = string.IsNullOrWhiteSpace(key) ? null : key;
        }
    }

    public void SetTagName(string tagName)
    {
        if (Kind == VNodeKind.Element && !string.IsNullOrWhiteSpace(tagName))
        {
            TagName = tagName;
        }
    }

    public bool Equals(VNode? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null)
        {
            return false;
        }

        if (Kind != other.Kind ||
            !string.Equals(TagName, other.TagName, StringComparison.Ordinal) ||
            !string.Equals(Text, other.Text, StringComparison.Ordinal) ||
            !string.Equals(Key, other.Key, StringComparison.Ordinal) ||
            _children.Count != other._children.Count ||
            _attributes.Count != other._attributes.Count ||
            _events.Count != other._events.Count)
        {
            return false;
        }

        for (var i = 0; i < _children.Count; i++)
        {
            if (!_children[i].Equals(other._children[i]))
            {
                return false;
            }
        }

        foreach (var pair in _attributes)
        {
            if (!other._attributes.TryGetValue(pair.Key, out var otherValue))
            {
                return false;
            }

            if (!EqualityComparer<object?>.Default.Equals(pair.Value, otherValue))
            {
                return false;
            }
        }

        foreach (var pair in _events)
        {
            if (!other._events.TryGetValue(pair.Key, out var otherEvent))
            {
                return false;
            }

            if (!pair.Value.Equals(otherEvent))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj)
        => Equals(obj as VNode);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(TagName, StringComparer.Ordinal);
        hash.Add(Text, StringComparer.Ordinal);
        hash.Add(Key, StringComparer.Ordinal);

        if (_attributes.Count > 0)
        {
            var attributeKeys = new List<string>(_attributes.Keys);
            attributeKeys.Sort(StringComparer.Ordinal);

            foreach (var key in attributeKeys)
            {
                hash.Add(key, StringComparer.Ordinal);
                _attributes.TryGetValue(key, out var value);
                hash.Add(value, EqualityComparer<object?>.Default);
            }
        }

        if (_events.Count > 0)
        {
            var eventKeys = new List<string>(_events.Keys);
            eventKeys.Sort(StringComparer.OrdinalIgnoreCase);

            foreach (var key in eventKeys)
            {
                hash.Add(key, StringComparer.OrdinalIgnoreCase);
                if (_events.TryGetValue(key, out var evt))
                {
                    hash.Add(evt);
                }
            }
        }

        foreach (var child in _children)
        {
            hash.Add(child);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(VNode? left, VNode? right)
        => left is null ? right is null : left.Equals(right);

    public static bool operator !=(VNode? left, VNode? right)
        => !(left == right);

    public override string ToString()
        => Kind switch
        {
            VNodeKind.Component => "[Component]",
            VNodeKind.Region => "[Region]",
            VNodeKind.Element => $"[Element: {TagName}, Key={Key}, Attrs={_attributes.Count}, Events={_events.Count}, Children={_children.Count}]",
            VNodeKind.Text => $"[Text: \"{Text}\"]",
            _ => "[Unknown]",
        };
}

public readonly record struct VNodeEvent(string Name, ulong HandlerId, VNodeEventOptions Options);

public readonly record struct VNodeEventOptions(bool PreventDefault, bool StopPropagation);
