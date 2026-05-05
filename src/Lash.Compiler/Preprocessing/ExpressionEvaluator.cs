namespace Lash.Compiler.Preprocessing;

internal sealed class DirectiveExpressionEvaluator
{
    public bool TryEvaluate(string expression, IReadOnlyDictionary<string, string?> symbols, out bool value, out string error)
    {
        value = false;
        error = string.Empty;

        if (!DirectiveTokenizer.TryTokenize(expression, out var tokens, out error))
            return false;

        var parser = new Parser(tokens, symbols);
        if (!parser.TryParse(out value, out error))
            return false;

        return true;
    }

    private sealed class Parser
    {
        private readonly IReadOnlyList<Token> tokens;
        private readonly IReadOnlyDictionary<string, string?> symbols;
        private int index;

        public Parser(IReadOnlyList<Token> tokens, IReadOnlyDictionary<string, string?> symbols)
        {
            this.tokens = tokens;
            this.symbols = symbols;
        }

        public bool TryParse(out bool value, out string error)
        {
            if (!TryParseOr(out value, out error))
                return false;

            if (Current.Kind != TokenKind.End)
            {
                error = $"Unexpected token '{Current.Text}'.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private bool TryParseOr(out bool value, out string error)
        {
            if (!TryParseAnd(out value, out error))
                return false;

            while (Match(TokenKind.OrOr))
            {
                if (!TryParseAnd(out var right, out error))
                    return false;
                value = value || right;
            }

            error = string.Empty;
            return true;
        }

        private bool TryParseAnd(out bool value, out string error)
        {
            if (!TryParseUnary(out value, out error))
                return false;

            while (Match(TokenKind.AndAnd))
            {
                if (!TryParseUnary(out var right, out error))
                    return false;
                value = value && right;
            }

            error = string.Empty;
            return true;
        }

        private bool TryParseUnary(out bool value, out string error)
        {
            if (Match(TokenKind.Bang))
            {
                if (!TryParseUnary(out var inner, out error))
                {
                    value = false;
                    return false;
                }
                value = !inner;
                return true;
            }

            return TryParseCondition(out value, out error);
        }

        private bool TryParseCondition(out bool value, out string error)
        {
            if (Match(TokenKind.LeftParen))
            {
                if (!TryParseOr(out value, out error))
                    return false;

                if (!Match(TokenKind.RightParen))
                {
                    error = "Expected ')' to close expression.";
                    return false;
                }

                return true;
            }

            if (Current.Kind == TokenKind.Identifier
                && !string.Equals(Current.Text, "true", StringComparison.Ordinal)
                && !string.Equals(Current.Text, "false", StringComparison.Ordinal))
            {
                var identifier = Advance().Text;

                if (Match(TokenKind.EqualEqual))
                {
                    var leftValue = ResolveIdentifierValue(identifier);
                    if (!TryParseValue(out var right, out error))
                    {
                        value = false;
                        return false;
                    }

                    value = leftValue.ValueEquals(right);
                    return true;
                }

                if (Match(TokenKind.BangEqual))
                {
                    var leftValue = ResolveIdentifierValue(identifier);
                    if (!TryParseValue(out var right, out error))
                    {
                        value = false;
                        return false;
                    }

                    value = !leftValue.ValueEquals(right);
                    return true;
                }

                value = symbols.ContainsKey(identifier);
                error = string.Empty;
                return true;
            }

            if (!TryParseValue(out var left, out error))
            {
                value = false;
                return false;
            }

            if (Match(TokenKind.EqualEqual))
            {
                if (!TryParseValue(out var right, out error))
                {
                    value = false;
                    return false;
                }

                value = left.ValueEquals(right);
                return true;
            }

            if (Match(TokenKind.BangEqual))
            {
                if (!TryParseValue(out var right, out error))
                {
                    value = false;
                    return false;
                }

                value = !left.ValueEquals(right);
                return true;
            }

            value = left.ToBool();
            error = string.Empty;
            return true;
        }

        private bool TryParseValue(out DirectiveValue value, out string error)
        {
            if (Current.Kind == TokenKind.Identifier)
            {
                var identifier = Advance().Text;

                if (string.Equals(identifier, "true", StringComparison.Ordinal))
                {
                    value = DirectiveValue.FromBool(true);
                    error = string.Empty;
                    return true;
                }

                if (string.Equals(identifier, "false", StringComparison.Ordinal))
                {
                    value = DirectiveValue.FromBool(false);
                    error = string.Empty;
                    return true;
                }

                if (!symbols.TryGetValue(identifier, out var rawSymbolValue))
                {
                    value = DirectiveValue.Undefined;
                    error = string.Empty;
                    return true;
                }

                value = DirectiveValue.FromRawSymbol(rawSymbolValue);
                error = string.Empty;
                return true;
            }

            if (Current.Kind == TokenKind.Number)
            {
                var token = Advance();
                if (!long.TryParse(token.Text, out var parsed))
                {
                    value = default;
                    error = $"Invalid numeric literal '{token.Text}'.";
                    return false;
                }

                value = DirectiveValue.FromNumber(parsed);
                error = string.Empty;
                return true;
            }

            if (Current.Kind == TokenKind.String)
            {
                value = DirectiveValue.FromString(Advance().Text);
                error = string.Empty;
                return true;
            }

            value = default;
            error = $"Unexpected token '{Current.Text}'.";
            return false;
        }

        private DirectiveValue ResolveIdentifierValue(string identifier)
        {
            if (!symbols.TryGetValue(identifier, out var rawSymbolValue))
                return DirectiveValue.Undefined;

            return DirectiveValue.FromRawSymbol(rawSymbolValue);
        }

        private Token Current => index < tokens.Count ? tokens[index] : tokens[^1];

        private Token Advance()
        {
            var token = Current;
            if (index < tokens.Count)
                index++;
            return token;
        }

        private bool Match(TokenKind kind)
        {
            if (Current.Kind != kind)
                return false;

            Advance();
            return true;
        }
    }

    private readonly record struct DirectiveValue(DirectiveValueKind Kind, bool BoolValue, long NumberValue, string StringValue)
    {
        public static DirectiveValue Undefined => new(DirectiveValueKind.Undefined, false, 0L, string.Empty);

        public static DirectiveValue FromBool(bool value) => new(DirectiveValueKind.Bool, value, 0L, string.Empty);

        public static DirectiveValue FromNumber(long value) => new(DirectiveValueKind.Number, false, value, string.Empty);

        public static DirectiveValue FromString(string value) => new(DirectiveValueKind.String, false, 0L, value);

        public static DirectiveValue FromRawSymbol(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return FromBool(true);

            if (bool.TryParse(value, out var parsedBool))
                return FromBool(parsedBool);

            if (long.TryParse(value, out var parsedLong))
                return FromNumber(parsedLong);

            return FromString(value);
        }

        public bool ToBool()
        {
            return Kind switch
            {
                DirectiveValueKind.Bool => BoolValue,
                DirectiveValueKind.Number => NumberValue != 0,
                DirectiveValueKind.String => !string.IsNullOrEmpty(StringValue)
                                             && !string.Equals(StringValue, "0", StringComparison.Ordinal)
                                             && !string.Equals(StringValue, "false", StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        public bool ValueEquals(DirectiveValue other)
        {
            if (Kind == DirectiveValueKind.Undefined || other.Kind == DirectiveValueKind.Undefined)
                return Kind == other.Kind;

            if (Kind == DirectiveValueKind.Number && other.Kind == DirectiveValueKind.Number)
                return NumberValue == other.NumberValue;

            if (Kind == DirectiveValueKind.Bool && other.Kind == DirectiveValueKind.Bool)
                return BoolValue == other.BoolValue;

            return string.Equals(ToComparableString(), other.ToComparableString(), StringComparison.Ordinal);
        }

        private string ToComparableString()
        {
            return Kind switch
            {
                DirectiveValueKind.Bool => BoolValue ? "true" : "false",
                DirectiveValueKind.Number => NumberValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                DirectiveValueKind.String => StringValue,
                _ => string.Empty
            };
        }
    }

    private enum DirectiveValueKind
    {
        Undefined,
        Bool,
        Number,
        String
    }

    private static class DirectiveTokenizer
    {
        public static bool TryTokenize(string source, out List<Token> tokens, out string error)
        {
            tokens = new List<Token>();
            error = string.Empty;

            for (int i = 0; i < source.Length;)
            {
                var c = source[i];

                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                if (c == '&' && i + 1 < source.Length && source[i + 1] == '&')
                {
                    tokens.Add(new Token(TokenKind.AndAnd, "&&"));
                    i += 2;
                    continue;
                }

                if (c == '|' && i + 1 < source.Length && source[i + 1] == '|')
                {
                    tokens.Add(new Token(TokenKind.OrOr, "||"));
                    i += 2;
                    continue;
                }

                if (c == '=' && i + 1 < source.Length && source[i + 1] == '=')
                {
                    tokens.Add(new Token(TokenKind.EqualEqual, "=="));
                    i += 2;
                    continue;
                }

                if (c == '!' && i + 1 < source.Length && source[i + 1] == '=')
                {
                    tokens.Add(new Token(TokenKind.BangEqual, "!="));
                    i += 2;
                    continue;
                }

                if (c == '!')
                {
                    tokens.Add(new Token(TokenKind.Bang, "!"));
                    i++;
                    continue;
                }

                if (c == '(')
                {
                    tokens.Add(new Token(TokenKind.LeftParen, "("));
                    i++;
                    continue;
                }

                if (c == ')')
                {
                    tokens.Add(new Token(TokenKind.RightParen, ")"));
                    i++;
                    continue;
                }

                if (char.IsDigit(c))
                {
                    var start = i;
                    i++;
                    while (i < source.Length && char.IsDigit(source[i]))
                        i++;
                    tokens.Add(new Token(TokenKind.Number, source[start..i]));
                    continue;
                }

                if (LashIdentifier.IsStart(c))
                {
                    var start = i;
                    i++;
                    while (i < source.Length && LashIdentifier.IsPart(source[i]))
                        i++;
                    tokens.Add(new Token(TokenKind.Identifier, source[start..i]));
                    continue;
                }

                if (c is '"' or '\'')
                {
                    var quote = c;
                    var start = ++i;
                    var escaped = false;

                    while (i < source.Length)
                    {
                        var ch = source[i];
                        if (!escaped && ch == quote)
                            break;

                        if (!escaped && ch == '\\')
                            escaped = true;
                        else
                            escaped = false;

                        i++;
                    }

                    if (i >= source.Length)
                    {
                        error = "Unterminated string literal.";
                        return false;
                    }

                    tokens.Add(new Token(TokenKind.String, source[start..i]));
                    i++; // consume closing quote
                    continue;
                }

                error = $"Unexpected character '{c}'.";
                return false;
            }

            tokens.Add(new Token(TokenKind.End, string.Empty));
            return true;
        }

    }

    private readonly record struct Token(TokenKind Kind, string Text);

    private enum TokenKind
    {
        Identifier,
        Number,
        String,
        Bang,
        AndAnd,
        OrOr,
        EqualEqual,
        BangEqual,
        LeftParen,
        RightParen,
        End
    }
}
