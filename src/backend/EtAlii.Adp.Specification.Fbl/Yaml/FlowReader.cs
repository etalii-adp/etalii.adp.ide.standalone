using EtAlii.Adp.Specification.Fbl.Expressions;

namespace EtAlii.Adp.Specification.Fbl.Yaml;

/// <summary>
/// Reads a YAML flow collection (<c>[a, b]</c>, <c>{k: v}</c>) into the values CEL sees (FBL §4.3):
/// its members are readable, while its only writable span is the whole collection.
/// </summary>
internal sealed class FlowReader(string text)
{
    private int _position;

    public object? Read()
    {
        var value = Value();
        return value;
    }

    private void Skip()
    {
        while (_position < text.Length)
        {
            var c = text[_position];
            if (c is ' ' or '\t' or '\r' or '\n') _position++;
            else if (c == '#' && (_position == 0 || text[_position - 1] is ' ' or '\t' or '\n'))
            {
                while (_position < text.Length && text[_position] != '\n') _position++;
            }
            else break;
        }
    }

    private object? Value()
    {
        Skip();
        if (_position >= text.Length) return null;
        switch (text[_position])
        {
            case '[':
                _position++;
                var list = new List<object?>();
                while (true)
                {
                    Skip();
                    if (_position >= text.Length) return list;
                    if (text[_position] == ']') { _position++; return list; }
                    list.Add(Value());
                    Skip();
                    if (_position < text.Length && text[_position] == ',') _position++;
                }
            case '{':
                _position++;
                var map = new CelMap();
                while (true)
                {
                    Skip();
                    if (_position >= text.Length) return map;
                    if (text[_position] == '}') { _position++; return map; }
                    var key = Scalar(true);
                    Skip();
                    object? value = null;
                    if (_position < text.Length && text[_position] == ':')
                    {
                        _position++;
                        value = Value();
                    }
                    map[Convert.ToString(key, System.Globalization.CultureInfo.InvariantCulture) ?? ""] = value;
                    Skip();
                    if (_position < text.Length && text[_position] == ',') _position++;
                }
            default:
                return Scalar(false);
        }
    }

    private object? Scalar(bool key)
    {
        var c = text[_position];
        if (c is '"' or '\'')
        {
            var start = ++_position;
            while (_position < text.Length)
            {
                if (c == '"' && text[_position] == '\\') { _position += 2; continue; }
                if (text[_position] == c)
                {
                    if (c == '\'' && _position + 1 < text.Length && text[_position + 1] == '\'') { _position += 2; continue; }
                    break;
                }
                _position++;
            }
            var inner = text[start..Math.Min(_position, text.Length)];
            _position++;
            return c == '"' ? YamlScalars.DecodeDouble(inner) : YamlScalars.DecodeSingle(inner);
        }
        var begin = _position;
        while (_position < text.Length)
        {
            var d = text[_position];
            if (d is ',' or ']' or '}') break;
            if (d == ':' && (key || _position + 1 >= text.Length || text[_position + 1] is ' ' or ',' or ']' or '}')) break;
            _position++;
        }
        return YamlScalars.Typed(YamlScalars.Fold(text[begin.._position].Trim()));
    }
}
