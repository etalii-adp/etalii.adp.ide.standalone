using EtAlii.Adp.Specification.Cel;

namespace EtAlii.Adp.Specification.Fbl.Yaml;

/// <summary>
/// Reads a YAML flow collection (<c>[a, b]</c>, <c>{k: v}</c>) into the values CEL sees (FBL §4.3):
/// its members are readable, while its only writable span is the whole collection. One pass gives
/// three readings of the same shape: each member's typed value, its written text (FBL §4.1.5) and
/// the name of the style it is written in (FBL §2.4).
/// </summary>
internal sealed class FlowReader(string text)
{
    private int _position;

    public FlowValue Read() => Value();

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

    private FlowValue Value()
    {
        Skip();
        if (_position >= text.Length) return new FlowValue(null, "", "plain");
        switch (text[_position])
        {
            case '[':
                _position++;
                var typed = new List<object?>();
                var texts = new List<object?>();
                var styles = new List<object?>();
                while (true)
                {
                    Skip();
                    if (_position >= text.Length || text[_position] == ']')
                    {
                        if (_position < text.Length) _position++;
                        return new FlowValue(typed, texts, styles);
                    }
                    var item = Value();
                    typed.Add(item.Typed);
                    texts.Add(item.Text);
                    styles.Add(item.Style);
                    Skip();
                    if (_position < text.Length && text[_position] == ',') _position++;
                }
            case '{':
                _position++;
                var typedMap = new CelMap();
                var textMap = new CelMap();
                var styleMap = new CelMap();
                while (true)
                {
                    Skip();
                    if (_position >= text.Length || text[_position] == '}')
                    {
                        if (_position < text.Length) _position++;
                        return new FlowValue(typedMap, textMap, styleMap);
                    }
                    var key = Scalar(true);
                    Skip();
                    var value = new FlowValue(null, "", "plain");
                    if (_position < text.Length && text[_position] == ':')
                    {
                        _position++;
                        value = Value();
                    }
                    var name = Convert.ToString(key.Typed, System.Globalization.CultureInfo.InvariantCulture) ?? "";
                    typedMap[name] = value.Typed;
                    textMap[(string)key.Text!] = value.Text;
                    styleMap[name] = value.Style;
                    Skip();
                    if (_position < text.Length && text[_position] == ',') _position++;
                }
            default:
                return Scalar(false);
        }
    }

    private FlowValue Scalar(bool key)
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
            var decoded = c == '"' ? YamlScalars.DecodeDouble(inner) : YamlScalars.DecodeSingle(inner);
            return new FlowValue(decoded, decoded, c == '"' ? "double" : "single");
        }
        var begin = _position;
        while (_position < text.Length)
        {
            var d = text[_position];
            if (d is ',' or ']' or '}') break;
            if (d == ':' && (key || _position + 1 >= text.Length || text[_position + 1] is ' ' or ',' or ']' or '}')) break;
            _position++;
        }
        var plain = YamlScalars.Fold(text[begin.._position].Trim());
        return new FlowValue(YamlScalars.Typed(plain), plain, "plain");
    }
}

/// <summary>A member of a flow collection, or the collection itself, read three ways: typed, as written, and by style.</summary>
internal sealed record FlowValue(object? Typed, object? Text, object? Style);
