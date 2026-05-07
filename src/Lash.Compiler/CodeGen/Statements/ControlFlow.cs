namespace Lash.Compiler.CodeGen;

using Lash.Compiler.Ast;
using Lash.Compiler.Ast.Expressions;
using Lash.Compiler.Ast.Statements;
using Lash.Compiler.Ast.Types;

internal sealed partial class StatementGenerator
{
    private void GenerateIfStatement(IfStatement ifStmt)
    {
        var condition = GenerateCondition(ifStmt.Condition);
        owner.Emit($"if {condition}; then");
        owner.IndentLevel++;

        foreach (var stmt in ifStmt.ThenBlock)
        {
            owner.EmitLine();
            GenerateStatement(stmt);
        }

        owner.IndentLevel--;

        foreach (var elifClause in ifStmt.ElifClauses)
        {
            owner.EmitLine();
            var elifCondition = GenerateCondition(elifClause.Condition);
            owner.Emit($"elif {elifCondition}; then");
            owner.IndentLevel++;

            foreach (var stmt in elifClause.Body)
            {
                owner.EmitLine();
                GenerateStatement(stmt);
            }

            owner.IndentLevel--;
        }

        if (ifStmt.ElseBlock.Count > 0)
        {
            owner.EmitLine();
            owner.Emit("else");
            owner.IndentLevel++;

            foreach (var stmt in ifStmt.ElseBlock)
            {
                owner.EmitLine();
                GenerateStatement(stmt);
            }

            owner.IndentLevel--;
        }

        owner.EmitLine();
        owner.Emit("fi");
    }

    private void GenerateForLoop(ForLoop forLoop)
    {
        string rangeExpr;
        if (!string.IsNullOrEmpty(forLoop.GlobPattern))
        {
            rangeExpr = forLoop.GlobPattern!;
        }
        else if (forLoop.Range is RangeExpression range)
        {
            var start = owner.GenerateExpression(range.Start);
            var end = owner.GenerateExpression(range.End);

            if (forLoop.Step != null)
            {
                var step = owner.GenerateExpression(forLoop.Step);
                rangeExpr = $"$(seq {start} {step} {end})";
            }
            else
            {
                rangeExpr = $"$(seq {start} {end})";
            }
        }
        else
        {
            if (forLoop.Range is null)
                throw new InvalidOperationException("Non-glob for-loop is missing its iterable expression.");

            rangeExpr = GenerateIterableWordsExpression(forLoop.Range);
        }

        owner.Emit($"for {forLoop.Variable} in {rangeExpr}; do");
        owner.IndentLevel++;

        foreach (var stmt in forLoop.Body)
        {
            owner.EmitLine();
            GenerateStatement(stmt);
        }

        owner.IndentLevel--;
        owner.EmitLine();
        owner.Emit("done");
    }

    private string GenerateIterableWordsExpression(Expression iterable)
    {
        return iterable switch
        {
            IdentifierExpression ident => string.Equals(ident.Name, "argv", StringComparison.Ordinal)
                ? "\"$@\""
                : $"\"${{{ident.Name}[@]}}\"",
            ArrayLiteral array => string.Join(" ", array.Elements.Select(owner.GenerateExpression)),
            _ => owner.GenerateExpression(iterable)
        };
    }

    private void GenerateSelectLoop(SelectLoop selectLoop)
    {
        string optionsExpr;
        if (!string.IsNullOrEmpty(selectLoop.GlobPattern))
        {
            optionsExpr = selectLoop.GlobPattern!;
        }
        else
        {
            if (selectLoop.Options is null)
                throw new InvalidOperationException("Select-loop is missing its options expression.");

            optionsExpr = selectLoop.Options switch
            {
                RangeExpression range =>
                    $"$(seq {owner.GenerateExpression(range.Start)} {owner.GenerateExpression(range.End)})",
                _ => GenerateIterableWordsExpression(selectLoop.Options)
            };
        }

        owner.Emit($"select {selectLoop.Variable} in {optionsExpr}; do");
        owner.IndentLevel++;

        foreach (var stmt in selectLoop.Body)
        {
            owner.EmitLine();
            GenerateStatement(stmt);
        }

        owner.IndentLevel--;
        owner.EmitLine();
        owner.Emit("done");
    }

    private void GenerateSwitchStatement(SwitchStatement switchStatement)
    {
        var switchValue = owner.GenerateExpression(switchStatement.Value);
        owner.Emit($"case {switchValue} in");
        owner.IndentLevel++;

        foreach (var caseClause in switchStatement.Cases)
        {
            owner.EmitLine();
            owner.Emit($"{(caseClause.IsWildcard ? "*" : GenerateSwitchPatterns(caseClause.Patterns))})");
            owner.IndentLevel++;

            foreach (var statement in caseClause.Body)
            {
                owner.EmitLine();
                GenerateStatement(statement);
            }

            owner.EmitLine();
            owner.Emit(";;");
            owner.IndentLevel--;
        }

        owner.IndentLevel--;
        owner.EmitLine();
        owner.Emit("esac");
    }

    private string GenerateSwitchPatterns(IReadOnlyList<Expression> patterns)
    {
        return string.Join("|", patterns.Select(GenerateSwitchPattern));
    }

    private string GenerateSwitchPattern(Expression pattern)
    {
        if (pattern is LiteralExpression literal
            && literal.LiteralType is PrimitiveType { PrimitiveKind: PrimitiveType.Kind.String })
        {
            return EscapeCasePattern(literal.Value?.ToString() ?? string.Empty);
        }

        return owner.GenerateExpression(pattern);
    }

    private void GenerateWhileLoop(WhileLoop whileLoop)
    {
        var condition = GenerateCondition(whileLoop.Condition);
        owner.Emit($"while {condition}; do");
        owner.IndentLevel++;

        foreach (var stmt in whileLoop.Body)
        {
            owner.EmitLine();
            GenerateStatement(stmt);
        }

        owner.IndentLevel--;
        owner.EmitLine();
        owner.Emit("done");
    }

    private void GenerateUntilLoop(UntilLoop untilLoop)
    {
        var condition = GenerateCondition(untilLoop.Condition);
        owner.Emit($"until {condition}; do");
        owner.IndentLevel++;

        foreach (var stmt in untilLoop.Body)
        {
            owner.EmitLine();
            GenerateStatement(stmt);
        }

        owner.IndentLevel--;
        owner.EmitLine();
        owner.Emit("done");
    }

    private void GenerateReturnStatement(ReturnStatement returnStmt)
    {
        if (returnStmt.Value != null)
        {
            var value = owner.GenerateExpression(returnStmt.Value);
            owner.Emit($"echo {value}");
            owner.EmitLine();
            owner.Emit("return 0");
        }
        else
        {
            owner.Emit("return 0");
        }
    }

    private string GenerateCondition(Expression condition)
    {
        return condition switch
        {
            BinaryExpression bin when IsComparisonOperator(bin.Operator) => GenerateComparisonCondition(bin),
            BinaryExpression bin when bin.Operator == "&&" =>
                $"{GenerateCondition(bin.Left)} && {GenerateCondition(bin.Right)}",
            BinaryExpression bin when bin.Operator == "||" =>
                $"{GenerateCondition(bin.Left)} || {GenerateCondition(bin.Right)}",
            UnaryExpression { Operator: "!" } unary => $"! {GenerateCondition(unary.Operand)}",
            _ => GenerateNumericTruthinessCondition(condition)
        };
    }

    private static bool IsComparisonOperator(string op)
    {
        return op is "==" or "!=" or "=~" or "<" or ">" or "<=" or ">=";
    }

    private string GenerateComparisonCondition(BinaryExpression comparison)
    {
        if (comparison.Operator is "==" or "!=")
        {
            var leftString = owner.GenerateExpression(comparison.Left);
            var rightString = owner.GenerateExpression(comparison.Right);
            return $"[[ {leftString} {comparison.Operator} {rightString} ]]";
        }

        if (comparison.Operator == "=~")
        {
            var leftString = owner.GenerateExpression(comparison.Left);
            var rightRegex = GenerateRegexOperand(comparison.Right);
            return $"[[ {leftString} =~ {rightRegex} ]]";
        }

        var left = owner.GenerateArithmeticExpression(comparison.Left);
        var right = owner.GenerateArithmeticExpression(comparison.Right);

        return $"(( {left} {comparison.Operator} {right} ))";
    }

    private string GenerateRegexOperand(Expression expression)
    {
        if (expression is LiteralExpression literal
            && literal.LiteralType is PrimitiveType { PrimitiveKind: PrimitiveType.Kind.String }
            && literal.Value is string text)
        {
            return EscapeRegexLiteral(text);
        }

        return owner.GenerateExpression(expression);
    }

    private static string EscapeRegexLiteral(string pattern)
    {
        if (pattern.Length == 0)
            return "\"\"";

        var builder = new System.Text.StringBuilder(pattern.Length);
        foreach (var ch in pattern)
        {
            switch (ch)
            {
                case ' ':
                    builder.Append("\\ ");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '$':
                case '\\':
                case '"':
                case '\'':
                    builder.Append('\\').Append(ch);
                    break;
                default:
                    builder.Append(ch);
                    break;
            }
        }

        return builder.ToString();
    }

    private string GenerateNumericTruthinessCondition(Expression condition)
    {
        if (condition is IdentifierExpression identifier)
        {
            if (string.Equals(identifier.Name, "argv", StringComparison.Ordinal))
                return "(( $# != 0 ))";

            return $"(( {identifier.Name} != 0 ))";
        }

        var expr = owner.GenerateExpression(condition);
        if (expr.StartsWith("$((", StringComparison.Ordinal) &&
            expr.EndsWith("))", StringComparison.Ordinal))
        {
            var inner = expr[3..^2].Trim();
            return $"(( {inner} ))";
        }

        if (int.TryParse(expr, out _))
            return $"(( {expr} != 0 ))";

        return $"[ {expr} -ne 0 ]";
    }
}
