namespace Lash.Compiler.Frontend.Semantics;

using Lash.Compiler.Ast;
using Lash.Compiler.Ast.Expressions;
using Lash.Compiler.Ast.Statements;
using Lash.Compiler.Ast.Types;
using Lash.Compiler.Diagnostics;

public sealed class TypeChecker
{
    private readonly DiagnosticBag diagnostics;
    private readonly Dictionary<string, ExpressionType> globalScope;
    private readonly Dictionary<string, ContainerKeyKind> globalContainerModes;
    private readonly Stack<Dictionary<string, ExpressionType>> scopes = new();
    private readonly Stack<Dictionary<string, ContainerKeyKind>> containerModes = new();

    public TypeChecker(DiagnosticBag diagnostics)
    {
        this.diagnostics = diagnostics;
        globalScope = new Dictionary<string, ExpressionType>(StringComparer.Ordinal)
        {
            ["argv"] = ExpressionTypes.Array
        };
        globalContainerModes = new Dictionary<string, ContainerKeyKind>(StringComparer.Ordinal)
        {
            ["argv"] = ContainerKeyKind.Numeric
        };
        scopes.Push(globalScope);
        containerModes.Push(globalContainerModes);
    }

    public void Analyze(ProgramNode program)
    {
        foreach (var statement in program.Statements)
            CheckStatement(statement);
    }

    private void CheckStatement(Statement statement)
    {
        switch (statement)
        {
            case VariableDeclaration variable:
                {
                    var valueType = InferType(variable.Value);
                    Declare(variable.Name, valueType, variable.IsGlobal);

                    if (IsArray(valueType))
                    {
                        var mode = InferContainerKindFromExpression(variable.Value);
                        if (mode != ContainerKeyKind.Unknown)
                            DeclareContainerMode(variable.Name, mode, variable.IsGlobal);
                    }

                    break;
                }
            case Assignment assignment:
                {
                    CheckAssignment(assignment);
                    break;
                }
            case UpdateStatement updateStatement:
                {
                    var targetType = Resolve(updateStatement.Target.Name);
                    _ = ValidateNumberOperand(updateStatement.Operator, updateStatement.Target, targetType);
                    Assign(updateStatement.Target, ExpressionTypes.Number, updateStatement.IsGlobal);
                    break;
                }
            case FunctionDeclaration function:
                CheckFunction(function);
                break;
            case IfStatement ifStatement:
                InferType(ifStatement.Condition);
                PushScope();
                foreach (var nested in ifStatement.ThenBlock)
                    CheckStatement(nested);
                PopScope();

                foreach (var elifClause in ifStatement.ElifClauses)
                {
                    InferType(elifClause.Condition);
                    PushScope();
                    foreach (var nested in elifClause.Body)
                        CheckStatement(nested);
                    PopScope();
                }

                PushScope();
                foreach (var nested in ifStatement.ElseBlock)
                    CheckStatement(nested);
                PopScope();
                break;
            case SwitchStatement switchStatement:
                InferType(switchStatement.Value);
                foreach (var clause in switchStatement.Cases)
                {
                    if (!clause.IsWildcard)
                    {
                        foreach (var pattern in clause.Patterns)
                            InferType(pattern);
                    }
                    PushScope();
                    foreach (var nested in clause.Body)
                        CheckStatement(nested);
                    PopScope();
                }
                break;
            case ForLoop forLoop:
                PushScope();
                Declare(forLoop.Variable, InferForLoopVariableType(forLoop));
                if (forLoop.Range != null)
                    InferType(forLoop.Range);
                if (forLoop.Step != null)
                    InferType(forLoop.Step);
                foreach (var nested in forLoop.Body)
                    CheckStatement(nested);
                PopScope();
                break;
            case SelectLoop selectLoop:
                PushScope();
                Declare(selectLoop.Variable, ExpressionTypes.String);
                if (selectLoop.Options != null)
                    InferType(selectLoop.Options);
                foreach (var nested in selectLoop.Body)
                    CheckStatement(nested);
                PopScope();
                break;
            case WhileLoop whileLoop:
                InferType(whileLoop.Condition);
                PushScope();
                foreach (var nested in whileLoop.Body)
                    CheckStatement(nested);
                PopScope();
                break;
            case UntilLoop untilLoop:
                InferType(untilLoop.Condition);
                PushScope();
                foreach (var nested in untilLoop.Body)
                    CheckStatement(nested);
                PopScope();
                break;
            case ReturnStatement returnStatement when returnStatement.Value != null:
                InferType(returnStatement.Value);
                break;
            case ShiftStatement shiftStatement when shiftStatement.Amount != null:
                {
                    var amountType = InferType(shiftStatement.Amount);
                    _ = ValidateNumberOperand("shift", shiftStatement.Amount, amountType);
                    break;
                }
            case SubshellStatement subshellStatement:
                {
                    PushScope();
                    foreach (var nested in subshellStatement.Body)
                        CheckStatement(nested);
                    PopScope();

                    if (!string.IsNullOrEmpty(subshellStatement.IntoVariable))
                    {
                        Assign(
                            new IdentifierExpression
                            {
                                Line = subshellStatement.Line,
                                Column = subshellStatement.Column,
                                Name = subshellStatement.IntoVariable!,
                                Type = ExpressionTypes.Unknown
                            },
                            ExpressionTypes.Number);
                    }
                    break;
                }
            case CoprocStatement coprocStatement:
                {
                    PushScope();
                    foreach (var nested in coprocStatement.Body)
                        CheckStatement(nested);
                    PopScope();

                    if (!string.IsNullOrEmpty(coprocStatement.IntoVariable))
                    {
                        Assign(
                            new IdentifierExpression
                            {
                                Line = coprocStatement.Line,
                                Column = coprocStatement.Column,
                                Name = coprocStatement.IntoVariable!,
                                Type = ExpressionTypes.Unknown
                            },
                            ExpressionTypes.Number);
                    }
                    break;
                }
            case WaitStatement waitStatement:
                {
                    if (waitStatement.TargetKind == WaitTargetKind.Target && waitStatement.Target != null)
                        _ = InferType(waitStatement.Target);

                    if (!string.IsNullOrEmpty(waitStatement.IntoVariable))
                    {
                        Assign(
                            new IdentifierExpression
                            {
                                Line = waitStatement.Line,
                                Column = waitStatement.Column,
                                Name = waitStatement.IntoVariable!,
                                Type = ExpressionTypes.Unknown
                            },
                            ExpressionTypes.Number);
                    }

                    break;
                }
            case ShellStatement shellStatement:
                ValidateShellPayload(shellStatement.Command, shellStatement, "Statement 'sh'");
                break;
            case TestStatement testStatement:
                ValidateShellPayload(testStatement.Condition, testStatement, "Statement 'test'");
                break;
            case TrapStatement trapStatement:
                if (trapStatement.Handler != null)
                {
                    foreach (var argument in trapStatement.Handler.Arguments)
                        _ = InferType(argument);
                }
                else if (trapStatement.Command != null)
                {
                    ValidateShellPayload(trapStatement.Command, trapStatement, "Statement 'trap'");
                }
                break;
            case UntrapStatement:
                break;
            case ExpressionStatement expressionStatement:
                if (expressionStatement.Expression is BinaryExpression binary &&
                    IsComparisonRedirect(binary))
                {
                    _ = InferType(binary.Left);
                    _ = InferType(binary.Right);
                    break;
                }

                InferType(expressionStatement.Expression);
                break;
        }
    }

    private ExpressionType InferForLoopVariableType(ForLoop forLoop)
    {
        if (forLoop.GlobPattern != null)
            return ExpressionTypes.String;

        if (forLoop.Range is RangeExpression)
            return ExpressionTypes.Number;

        return ExpressionTypes.String;
    }

    private static bool IsComparisonRedirect(BinaryExpression binaryExpression)
    {
        return binaryExpression.Operator is ">" or "<";
    }

    private void CheckAssignment(Assignment assignment)
    {
        if (assignment.Operator != "=" && assignment.Target is not IdentifierExpression)
        {
            Report(assignment, $"Operator '{assignment.Operator}' only supports variable targets.", DiagnosticCodes.TypeMismatch);
            return;
        }

        if (assignment.Operator == "+=")
        {
            CheckPlusEqualsAssignment(assignment);
            return;
        }

        if (assignment.Operator is "-=" or "*=" or "/=" or "%=")
        {
            CheckArithmeticCompoundAssignment(assignment);
            return;
        }

        var valueType = InferType(assignment.Value);
        assignment.Mode = Assignment.AssignmentMode.Simple;
        if (assignment.Target is IdentifierExpression ident)
        {
            Assign(ident, valueType, assignment.IsGlobal);

            if (IsArray(valueType))
            {
                var mode = InferContainerKindFromExpression(assignment.Value);
                if (mode != ContainerKeyKind.Unknown)
                    SetContainerMode(ident.Name, mode, assignment.IsGlobal);
            }
            return;
        }

        if (assignment.Target is IndexAccessExpression indexTarget)
        {
            InferType(indexTarget);
            if (indexTarget.Array is IdentifierExpression arrayIdent)
            {
                Assign(arrayIdent, ExpressionTypes.Array, assignment.IsGlobal);
                var keyType = InferType(indexTarget.Index);
                var keyKind = InferKeyKind(indexTarget.Index, keyType);
                ValidateAndTrackContainerKeyKind(arrayIdent, keyKind, indexTarget, assignment.IsGlobal);
            }
        }
    }

    private void CheckPlusEqualsAssignment(Assignment assignment)
    {
        if (assignment.Target is not IdentifierExpression identifier)
        {
            Report(assignment, "Operator '+=' only supports variable targets.", DiagnosticCodes.TypeMismatch);
            return;
        }

        var leftType = Resolve(identifier.Name);
        var rightType = InferType(assignment.Value);

        var canBeArray = (!IsKnown(leftType) || IsArray(leftType))
                         && (!IsKnown(rightType) || IsArray(rightType));
        var canBeNumeric = (!IsKnown(leftType) || IsNumber(leftType))
                           && (!IsKnown(rightType) || IsNumber(rightType));

        if (canBeArray && !canBeNumeric)
        {
            assignment.Mode = Assignment.AssignmentMode.ArrayAppend;
            Assign(identifier, ExpressionTypes.Array, assignment.IsGlobal);

            var rhsMode = InferContainerKindFromExpression(assignment.Value);
            if (rhsMode != ContainerKeyKind.Unknown)
                ValidateAndTrackContainerKeyKind(identifier, rhsMode, assignment, assignment.IsGlobal);
            return;
        }

        if (canBeNumeric && !canBeArray)
        {
            assignment.Mode = Assignment.AssignmentMode.Arithmetic;
            Assign(identifier, ExpressionTypes.Number, assignment.IsGlobal);
            return;
        }

        if (canBeArray && canBeNumeric)
        {
            assignment.Mode = Assignment.AssignmentMode.Unresolved;
            Report(
                assignment,
                "Operator '+=' is ambiguous for unknown target/value types.",
                DiagnosticCodes.AmbiguousCompoundAssignment);
            return;
        }

        assignment.Mode = Assignment.AssignmentMode.Unresolved;
        Report(
            assignment,
            $"Operator '+=' cannot combine {FormatType(leftType)} and {FormatType(rightType)}.",
            DiagnosticCodes.TypeMismatch);
    }

    private void CheckArithmeticCompoundAssignment(Assignment assignment)
    {
        if (assignment.Target is not IdentifierExpression identifier)
        {
            Report(assignment, $"Operator '{assignment.Operator}' only supports variable targets.", DiagnosticCodes.TypeMismatch);
            return;
        }

        var leftType = Resolve(identifier.Name);
        var rightType = InferType(assignment.Value);
        _ = ValidateNumberOperand(assignment.Operator, identifier, leftType);
        _ = ValidateNumberOperand(assignment.Operator, assignment.Value, rightType);
        assignment.Mode = Assignment.AssignmentMode.Arithmetic;
        Assign(identifier, ExpressionTypes.Number, assignment.IsGlobal);
    }

    private void CheckFunction(FunctionDeclaration function)
    {
        PushScope();

        foreach (var parameter in function.Parameters)
        {
            var parameterType = parameter.DefaultValue != null ? InferType(parameter.DefaultValue) : ExpressionTypes.Unknown;
            Declare(parameter.Name, parameterType);
        }

        foreach (var statement in function.Body)
            CheckStatement(statement);

        PopScope();
    }

    private ExpressionType InferType(Expression expression)
    {
        if (!IsUnknown(expression.Type))
            return expression.Type;

        var type = expression switch
        {
            LiteralExpression literal => literal.Type,
            NullLiteral => ExpressionTypes.Unknown,
            ArrayLiteral => ExpressionTypes.Array,
            MapLiteral => InferMapLiteralType((MapLiteral)expression),
            IdentifierExpression identifier => InferIdentifierType(identifier),
            EnumAccessExpression => ExpressionTypes.String,
            IndexAccessExpression indexAccess => InferIndexAccessType(indexAccess),
            FunctionCallExpression functionCall => InferFunctionCallType(functionCall),
            ShellCaptureExpression shellCapture => InferShellCaptureType(shellCapture),
            TestCaptureExpression testCapture => InferTestCaptureType(testCapture),
            ProcessSubstitutionExpression processSubstitution => InferProcessSubstitutionType(processSubstitution),
            UnaryExpression unary => InferUnaryType(unary),
            BinaryExpression binary => InferBinaryType(binary),
            PipeExpression pipe => InferPipeType(pipe),
            RedirectExpression redirect => InferRedirectType(redirect),
            RangeExpression => ExpressionTypes.Array,
            _ => ExpressionTypes.Unknown
        };

        expression.Type = type;
        return type;
    }

    private ExpressionType InferIdentifierType(IdentifierExpression identifier)
    {
        if (string.Equals(identifier.Name, "argv", StringComparison.Ordinal))
            return ExpressionTypes.Array;

        return Resolve(identifier.Name);
    }

    private ExpressionType InferIndexAccessType(IndexAccessExpression indexAccess)
    {
        var arrayType = InferType(indexAccess.Array);
        var indexType = InferType(indexAccess.Index);
        var keyKind = InferKeyKind(indexAccess.Index, indexType);

        if (indexAccess.Array is IdentifierExpression identifier)
        {
            ValidateAndTrackContainerKeyKind(identifier, keyKind, indexAccess);
        }
        else if (IsKnown(arrayType) && !IsArray(arrayType))
        {
            Report(indexAccess.Array, $"Index access expects an array, got {FormatType(arrayType)}.", DiagnosticCodes.InvalidIndexOrContainerUsage);
        }
        else if (indexAccess.Array is IdentifierExpression && keyKind == ContainerKeyKind.Unknown && IsKnown(arrayType) && !IsArray(arrayType))
        {
            // For identifier-backed index access with unknown key kind we still
            // want one clear non-array diagnostic.
            Report(indexAccess.Array, $"Index access expects an array, got {FormatType(arrayType)}.", DiagnosticCodes.InvalidIndexOrContainerUsage);
        }

        return ExpressionTypes.Unknown;
    }

    private ExpressionType InferFunctionCallType(FunctionCallExpression functionCall)
    {
        foreach (var argument in functionCall.Arguments)
            _ = InferType(argument);

        return ExpressionTypes.Unknown;
    }

    private ExpressionType InferShellCaptureType(ShellCaptureExpression shellCapture)
    {
        ValidateShellPayload(shellCapture.Command, shellCapture, "Expression '$(...)'");
        return ExpressionTypes.Unknown;
    }

    private ExpressionType InferTestCaptureType(TestCaptureExpression testCapture)
    {
        ValidateShellPayload(testCapture.Condition, testCapture, "Expression '$(test ...)'");
        return ExpressionTypes.Number;
    }

    private ExpressionType InferProcessSubstitutionType(ProcessSubstitutionExpression processSubstitution)
    {
        ValidateShellPayload(processSubstitution.Payload, processSubstitution, "Expression '<(...)' / '>(...)'");
        return ExpressionTypes.String;
    }

    private ExpressionType InferUnaryType(UnaryExpression unary)
    {
        var operandType = InferType(unary.Operand);
        return unary.Operator switch
        {
            "!" => ExpressionTypes.Bool,
            "-" or "+" => ValidateNumberOperand(unary.Operator, unary.Operand, operandType),
            "#" => ValidateLengthOperand(unary.Operand, operandType),
            _ => ExpressionTypes.Unknown
        };
    }

    private ExpressionType InferBinaryType(BinaryExpression binary)
    {
        var leftType = InferType(binary.Left);
        var rightType = InferType(binary.Right);

        switch (binary.Operator)
        {
            case "+":
                if (IsString(leftType) && IsString(rightType))
                    return ExpressionTypes.String;
                if (IsNumber(leftType) && IsNumber(rightType))
                    return ExpressionTypes.Number;
                if (IsKnown(leftType) && IsKnown(rightType))
                    Report(binary, $"Cannot add {FormatType(leftType)} and {FormatType(rightType)}.", DiagnosticCodes.TypeMismatch);
                return ExpressionTypes.Unknown;

            case "-":
            case "*":
            case "/":
            case "%":
                ValidateBothNumbers(binary, leftType, rightType);
                return ExpressionTypes.Number;

            case "<":
            case ">":
            case "<=":
            case ">=":
                ValidateBothNumbers(binary, leftType, rightType);
                return ExpressionTypes.Bool;

            case "==":
            case "!=":
                return ExpressionTypes.Bool;

            case "=~":
                ValidateRegexOperand(binary.Left);
                ValidateRegexOperand(binary.Right);
                return ExpressionTypes.Bool;

            case "&&":
            case "||":
                ValidateLogical(binary, leftType, rightType);
                return ExpressionTypes.Bool;

            case "..":
                ValidateBothNumbers(binary, leftType, rightType);
                return ExpressionTypes.Array;

            default:
                return ExpressionTypes.Unknown;
        }
    }

    private ExpressionType InferPipeType(PipeExpression pipe)
    {
        var leftType = InferType(pipe.Left);
        if (pipe.Right is IdentifierExpression target)
        {
            Assign(target, leftType);
            return leftType;
        }

        _ = InferType(pipe.Right);
        return ExpressionTypes.Unknown;
    }

    private ExpressionType InferRedirectType(RedirectExpression redirect)
    {
        _ = InferType(redirect.Left);
        _ = InferType(redirect.Right);
        return ExpressionTypes.Unknown;
    }

    private ExpressionType ValidateNumberOperand(string op, Expression operand, ExpressionType type)
    {
        if (IsUnknown(type))
            return ExpressionTypes.Number;
        if (IsNumber(type))
            return ExpressionTypes.Number;

        Report(operand, $"Operator '{op}' expects a number, got {FormatType(type)}.", DiagnosticCodes.TypeMismatch);
        return ExpressionTypes.Unknown;
    }

    private ExpressionType ValidateLengthOperand(Expression operand, ExpressionType type)
    {
        if (IsUnknown(type))
            return ExpressionTypes.Number;
        if (IsArray(type))
            return ExpressionTypes.Number;

        Report(operand, $"Operator '#' expects an array, got {FormatType(type)}.", DiagnosticCodes.TypeMismatch);
        return ExpressionTypes.Unknown;
    }

    private ExpressionType InferMapLiteralType(MapLiteral mapLiteral)
    {
        foreach (var entry in mapLiteral.Entries)
        {
            var keyType = InferType(entry.Key);
            ValidateMapKeyType(entry.Key, keyType);
            _ = InferType(entry.Value);
        }

        return ExpressionTypes.Array;
    }

    private void ValidateMapKeyType(Expression key, ExpressionType type)
    {
        if (IsUnknown(type) || IsString(type))
            return;

        Report(key, $"Map literal keys must be strings, got {FormatType(type)}.", DiagnosticCodes.InvalidIndexOrContainerUsage);
    }

    private void ValidateRegexOperand(Expression operand)
    {
        var type = InferType(operand);
        if (IsUnknown(type) || IsString(type) || IsNumber(type) || type is BooleanType)
            return;

        Report(operand, $"Operator '=~' expects a string-like operand, got {FormatType(type)}.", DiagnosticCodes.TypeMismatch);
    }

    private void ValidateShellPayload(Expression command, AstNode node, string context)
    {
        if (command is LiteralExpression literal &&
            literal.LiteralType is PrimitiveType { PrimitiveKind: PrimitiveType.Kind.String })
        {
            return;
        }

        Report(node, $"{context} expects a string literal command.", DiagnosticCodes.InvalidShellPayload);
    }

    private void ValidateAndTrackContainerKeyKind(
        IdentifierExpression identifier,
        ContainerKeyKind keyKind,
        AstNode location,
        bool isGlobalHint = false)
    {
        if (keyKind == ContainerKeyKind.Unknown)
            return;

        var containerType = Resolve(identifier.Name);
        if (IsKnown(containerType) && !IsArray(containerType))
        {
            Report(location, $"Index access expects an array, got {FormatType(containerType)}.", DiagnosticCodes.InvalidIndexOrContainerUsage);
            return;
        }

        var existing = ResolveContainerMode(identifier.Name);
        if (existing != ContainerKeyKind.Unknown && existing != keyKind)
        {
            Report(location, $"Cannot mix numeric and string keys for '{identifier.Name}'.", DiagnosticCodes.InvalidIndexOrContainerUsage);
            return;
        }

        SetContainerMode(identifier.Name, keyKind, isGlobalHint);
    }

    private static ContainerKeyKind InferKeyKind(Expression keyExpression, ExpressionType keyType)
    {
        if (keyExpression is LiteralExpression literal && literal.LiteralType is PrimitiveType primitive)
        {
            return primitive.PrimitiveKind switch
            {
                PrimitiveType.Kind.Int => ContainerKeyKind.Numeric,
                PrimitiveType.Kind.String => ContainerKeyKind.String,
                _ => ContainerKeyKind.Unknown
            };
        }

        if (IsNumber(keyType))
            return ContainerKeyKind.Numeric;
        if (IsString(keyType))
            return ContainerKeyKind.String;

        return ContainerKeyKind.Unknown;
    }

    private ContainerKeyKind InferContainerKindFromExpression(Expression expression)
    {
        return expression switch
        {
            IdentifierExpression identifier => ResolveContainerMode(identifier.Name),
            ArrayLiteral arrayLiteral when arrayLiteral.Elements.Count == 0 => ContainerKeyKind.Unknown,
            ArrayLiteral => ContainerKeyKind.Numeric,
            MapLiteral => ContainerKeyKind.String,
            RangeExpression => ContainerKeyKind.Numeric,
            _ => ContainerKeyKind.Unknown
        };
    }

    private void ValidateBothNumbers(Expression location, ExpressionType leftType, ExpressionType rightType)
    {
        if (IsKnown(leftType) && !IsNumber(leftType))
            Report(location, $"Expected number, got {FormatType(leftType)}.", DiagnosticCodes.TypeMismatch);
        if (IsKnown(rightType) && !IsNumber(rightType))
            Report(location, $"Expected number, got {FormatType(rightType)}.", DiagnosticCodes.TypeMismatch);
    }

    private void ValidateLogical(Expression location, ExpressionType leftType, ExpressionType rightType)
    {
        if (IsKnown(leftType) && (IsString(leftType) || IsArray(leftType)))
            Report(location, $"Logical operator expects bool/number, got {FormatType(leftType)}.", DiagnosticCodes.TypeMismatch);
        if (IsKnown(rightType) && (IsString(rightType) || IsArray(rightType)))
            Report(location, $"Logical operator expects bool/number, got {FormatType(rightType)}.", DiagnosticCodes.TypeMismatch);
    }

    private void PushScope()
    {
        scopes.Push(new Dictionary<string, ExpressionType>(scopes.Peek(), StringComparer.Ordinal));
        containerModes.Push(new Dictionary<string, ContainerKeyKind>(containerModes.Peek(), StringComparer.Ordinal));
    }

    private void PopScope()
    {
        scopes.Pop();
        containerModes.Pop();
    }

    private void Declare(string name, ExpressionType type, bool isGlobal = false)
    {
        if (isGlobal)
        {
            globalScope[name] = type;
            return;
        }

        scopes.Peek()[name] = type;
    }

    private void DeclareContainerMode(string name, ContainerKeyKind mode, bool isGlobal = false)
    {
        if (isGlobal)
        {
            globalContainerModes[name] = mode;
            return;
        }

        containerModes.Peek()[name] = mode;
    }

    private void Assign(IdentifierExpression target, ExpressionType type, bool isGlobal = false)
    {
        var name = target.Name;

        if (isGlobal)
        {
            globalScope[name] = type;
            return;
        }

        foreach (var scope in scopes)
        {
            if (!scope.ContainsKey(name))
                continue;

            scope[name] = type;
            return;
        }

        scopes.Peek()[name] = type;
    }

    private void SetContainerMode(string name, ContainerKeyKind mode, bool isGlobal = false)
    {
        if (isGlobal)
        {
            globalContainerModes[name] = mode;
            return;
        }

        foreach (var modeScope in containerModes)
        {
            if (!modeScope.ContainsKey(name))
                continue;

            modeScope[name] = mode;
            return;
        }

        containerModes.Peek()[name] = mode;
    }

    private ExpressionType Resolve(string name)
    {
        foreach (var scope in scopes)
        {
            if (scope.TryGetValue(name, out var type))
                return type;
        }

        return ExpressionTypes.Unknown;
    }

    private ContainerKeyKind ResolveContainerMode(string name)
    {
        foreach (var scope in containerModes)
        {
            if (scope.TryGetValue(name, out var mode))
                return mode;
        }

        return ContainerKeyKind.Unknown;
    }

    private void Report(AstNode node, string message, string code = DiagnosticCodes.TypeMismatch)
    {
        var tip = code switch
        {
            DiagnosticCodes.TypeMismatch => null,
            DiagnosticCodes.AmbiguousCompoundAssignment => "Disambiguate '+=' by using clear numeric operands or array append operands.",
            DiagnosticCodes.InvalidShellPayload => null,
            DiagnosticCodes.InvalidIndexOrContainerUsage => null,
            _ => null
        };

        diagnostics.AddError(
            DiagnosticMessage.WithTip($"Type error: {message}", tip),
            node.Line,
            node.Column,
            code);
    }

    private static bool IsKnown(ExpressionType type) => !IsUnknown(type);
    private static bool IsUnknown(ExpressionType type) => type is UnknownType;
    private static bool IsNumber(ExpressionType type) => type is NumberType;
    private static bool IsString(ExpressionType type) => type is StringType;
    private static bool IsArray(ExpressionType type) => type is ArrayType;
    private static string FormatType(ExpressionType type) => type.ToString();

    private enum ContainerKeyKind
    {
        Unknown,
        Numeric,
        String
    }
}
