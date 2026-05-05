namespace Lash.Compiler.Frontend.Semantics;

using Lash.Compiler.Ast;
using Lash.Compiler.Ast.Expressions;
using Lash.Compiler.Ast.Statements;
using Lash.Compiler.Ast.Types;
using Lash.Compiler.Diagnostics;

public sealed class NameResolver {
  private static readonly HashSet<char> ValidSetShortFlags = [
    'a', 'b', 'e', 'f', 'h', 'k', 'm', 'n', 'p', 't', 'u', 'v', 'x', 'B', 'C',
    'E', 'H', 'P', 'T'
  ];

  private static readonly HashSet<string> ValidSetLongOptions =
      new(StringComparer.Ordinal) { "allexport",
                                    "braceexpand",
                                    "emacs",
                                    "errexit",
                                    "errtrace",
                                    "functrace",
                                    "hashall",
                                    "histexpand",
                                    "history",
                                    "ignoreeof",
                                    "interactive-comments",
                                    "keyword",
                                    "monitor",
                                    "noclobber",
                                    "noexec",
                                    "noglob",
                                    "nolog",
                                    "notify",
                                    "nounset",
                                    "onecmd",
                                    "physical",
                                    "pipefail",
                                    "posix",
                                    "privileged",
                                    "verbose",
                                    "vi",
                                    "xtrace" };

  private static readonly HashSet<char> ValidShoptShortFlags =
      ['o', 'p', 'q', 's', 'u'];

  private static readonly HashSet<char> ValidExportShortFlags = ['f', 'n', 'p'];

  private static readonly HashSet<char> ValidAliasShortFlags = ['p'];
  private static readonly HashSet<char> ValidUnsetShortFlags = ['f', 'n', 'v'];
  private static readonly HashSet<char> ValidDeclareShortFlags = ['a', 'A', 'f', 'F', 'g', 'i', 'l', 'n', 'p', 'r', 't', 'u', 'x'];
  private static readonly HashSet<char> ValidLocalShortFlags = ['a', 'A', 'i', 'l', 'n', 'p', 'r', 't', 'u', 'x'];

  private static readonly HashSet<string> ValidTrapSignals =
      new(StringComparer.Ordinal) {
        "EXIT", "ERR",    "DEBUG", "RETURN", "HUP",  "INT",    "QUIT",
        "ILL",  "TRAP",   "ABRT",  "BUS",    "FPE",  "KILL",   "USR1",
        "SEGV", "USR2",   "PIPE",  "ALRM",   "TERM", "STKFLT", "CHLD",
        "CONT", "STOP",   "TSTP",  "TTIN",   "TTOU", "URG",    "XCPU",
        "XFSZ", "VTALRM", "PROF",  "WINCH",  "IO",   "PWR",    "SYS"
      };

  private readonly DiagnosticBag diagnostics;
  private readonly Dictionary<string, SymbolInfo> globalScope;
  private readonly HashSet<string> globalDeclared = new(StringComparer.Ordinal);
  private readonly Dictionary<string, HashSet<string>> enums =
      new(StringComparer.Ordinal);
  private readonly Dictionary<string, FunctionInfo> functions =
      new(StringComparer.Ordinal);
  private readonly Stack<Dictionary<string, SymbolInfo>> scopes = new();
  private readonly Stack<HashSet<string>> declaredInScope = new();
  private int loopDepth;
  private int functionDepth;

  public NameResolver(DiagnosticBag diagnostics) {
    this.diagnostics = diagnostics;
    globalScope = new Dictionary<string, SymbolInfo>(StringComparer.Ordinal);
    scopes.Push(globalScope);
    declaredInScope.Push(globalDeclared);
  }

  public void Analyze(ProgramNode program) {
    CollectDeclarations(program.Statements);

    foreach (var statement in program.Statements)
      CheckStatement(statement);
  }

  private void CollectDeclarations(IEnumerable<Statement> statements) {
    foreach (var statement in statements) {
      switch (statement) {
      case FunctionDeclaration function:
        if (functions.ContainsKey(function.Name)) {
          Report(function, $"Duplicate function declaration '{function.Name}'.",
                 DiagnosticCodes.DuplicateDeclaration);
        } else {
          var required = function.Parameters.Count(p => p.DefaultValue == null);
          functions[function.Name] =
              new FunctionInfo(function.Parameters.Count, required);
        }

        CollectDeclarations(function.Body);
        break;

      case EnumDeclaration enumDeclaration:
        if (enums.ContainsKey(enumDeclaration.Name)) {
          Report(enumDeclaration,
                 $"Duplicate enum declaration '{enumDeclaration.Name}'.",
                 DiagnosticCodes.DuplicateDeclaration);
        } else {
          if (enumDeclaration.Members.Count == 0) {
            Report(enumDeclaration,
                   $"Enum '{enumDeclaration.Name}' must declare at least one member.",
                   DiagnosticCodes.EmptyEnumDeclaration);
          }

          var members = new HashSet<string>(StringComparer.Ordinal);
          foreach (var member in enumDeclaration.Members) {
            if (!members.Add(member)) {
              Report(enumDeclaration,
                     $"Enum '{enumDeclaration.Name}' has duplicate member '{member}'.",
                     DiagnosticCodes.DuplicateEnumMember);
            }
          }

          enums[enumDeclaration.Name] = members;
        }
        break;

      case IfStatement ifStatement:
        CollectDeclarations(ifStatement.ThenBlock);
        foreach (var elifClause in ifStatement.ElifClauses)
          CollectDeclarations(elifClause.Body);
        CollectDeclarations(ifStatement.ElseBlock);
        break;

      case SwitchStatement switchStatement:
        foreach (var clause in switchStatement.Cases)
          CollectDeclarations(clause.Body);
        break;

      case ForLoop forLoop:
        CollectDeclarations(forLoop.Body);
        break;
      case SelectLoop selectLoop:
        CollectDeclarations(selectLoop.Body);
        break;

      case WhileLoop whileLoop:
        CollectDeclarations(whileLoop.Body);
        break;

      case UntilLoop untilLoop:
        CollectDeclarations(untilLoop.Body);
        break;

      case SubshellStatement subshellStatement:
        CollectDeclarations(subshellStatement.Body);
        break;
      }
    }
  }

  private void CheckStatement(Statement statement) {
    switch (statement) {
    case VariableDeclaration variable:
      CheckExpression(variable.Value);
      if (IsBuiltinIdentifier(variable.Name)) {
        Report(variable, $"Cannot declare built-in variable '{variable.Name}'.",
               DiagnosticCodes.InvalidAssignmentTarget);
        break;
      }

      if (variable.Kind == VariableDeclaration.VarKind.Readonly && loopDepth > 0) {
        Report(variable,
               $"Readonly declaration '{variable.Name}' is not allowed inside repeated contexts.",
               DiagnosticCodes.InvalidReadonlyContext);
      }

      Declare(variable.Name, IsImmutableDeclaration(variable.Kind),
              variable, variable.IsGlobal);
      break;

    case EnumDeclaration:
      break;

    case Assignment assignment:
      CheckExpression(assignment.Value);

      if (assignment.Operator != "=" &&
          assignment.Target is IndexAccessExpression) {
        Report(assignment.Target,
               $"Operator '{assignment.Operator}' only supports variable targets.",
               DiagnosticCodes.InvalidAssignmentTarget);
        break;
      }

      if (assignment.Target is IdentifierExpression identifier)
        ValidateAssignmentTarget(identifier, assignment.IsGlobal);
      else if (assignment.Target is IndexAccessExpression indexAccess)
        ValidateIndexAssignmentTarget(indexAccess);
      break;

    case UpdateStatement updateStatement:
      ValidateAssignmentTarget(updateStatement.Target, updateStatement.IsGlobal);
      break;

    case FunctionDeclaration function:
      CheckFunction(function);
      break;

    case IfStatement ifStatement:
      CheckExpression(ifStatement.Condition);
      PushScope();
      foreach (var nested in ifStatement.ThenBlock)
        CheckStatement(nested);
      PopScope();

      foreach (var elifClause in ifStatement.ElifClauses) {
        CheckExpression(elifClause.Condition);
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
      CheckExpression(switchStatement.Value);
      var wildcardSeen = false;
      var seenExactPatterns = new HashSet<string>(StringComparer.Ordinal);
      foreach (var clause in switchStatement.Cases) {
        if (!clause.IsWildcard) {
          foreach (var pattern in clause.Patterns) {
            CheckExpression(pattern);
            if (TryGetExactSwitchPatternKey(pattern, out var patternKey) &&
                !seenExactPatterns.Add(patternKey)) {
              Report(clause,
                     "Duplicate switch case pattern; an earlier case uses the same pattern.",
                     DiagnosticCodes.DuplicateSwitchCasePattern);
            }
          }
        } else {
          if (wildcardSeen) {
            Report(clause, "Switch can contain at most one wildcard case '_'.",
                   DiagnosticCodes.DuplicateWildcardCase);
          }
          wildcardSeen = true;
        }
        PushScope();
        foreach (var nested in clause.Body)
          CheckStatement(nested);
        PopScope();
      }
      break;

    case ForLoop forLoop:
      if (forLoop.Range != null)
        CheckExpression(forLoop.Range);
      if (forLoop.Step != null)
        CheckExpression(forLoop.Step);

      PushScope();
      loopDepth++;
      Declare(forLoop.Variable, isConst: false, forLoop);
      foreach (var nested in forLoop.Body)
        CheckStatement(nested);
      loopDepth--;
      PopScope();
      break;

    case SelectLoop selectLoop:
      if (selectLoop.Options != null)
        CheckExpression(selectLoop.Options);

      PushScope();
      loopDepth++;
      Declare(selectLoop.Variable, isConst: false, selectLoop);
      foreach (var nested in selectLoop.Body)
        CheckStatement(nested);
      loopDepth--;
      PopScope();
      break;

    case WhileLoop whileLoop:
      CheckExpression(whileLoop.Condition);
      PushScope();
      loopDepth++;
      foreach (var nested in whileLoop.Body)
        CheckStatement(nested);
      loopDepth--;
      PopScope();
      break;

    case UntilLoop untilLoop:
      CheckExpression(untilLoop.Condition);
      PushScope();
      loopDepth++;
      foreach (var nested in untilLoop.Body)
        CheckStatement(nested);
      loopDepth--;
      PopScope();
      break;

    case BreakStatement breakStatement:
      if (loopDepth == 0) {
        Report(statement, "'break' can only be used inside a loop.",
               DiagnosticCodes.InvalidControlFlowContext);
      }
      ValidateLoopControlDepth(breakStatement, "break");
      break;

    case ContinueStatement continueStatement:
      if (loopDepth == 0) {
        Report(statement, "'continue' can only be used inside a loop.",
               DiagnosticCodes.InvalidControlFlowContext);
      }
      ValidateLoopControlDepth(continueStatement, "continue");
      break;

    case ReturnStatement returnStatement:
      if (functionDepth == 0) {
        Report(returnStatement, "'return' can only be used inside a function.",
               DiagnosticCodes.InvalidControlFlowContext);
      }

      if (returnStatement.Value != null)
        CheckExpression(returnStatement.Value);
      break;

    case ShiftStatement shiftStatement when shiftStatement.Amount != null:
      CheckExpression(shiftStatement.Amount);
      break;

    case SubshellStatement subshellStatement:
      PushScope();
      foreach (var nested in subshellStatement.Body)
        CheckStatement(nested);
      PopScope();

      ResolveIntoBinding(subshellStatement.IntoVariable,
                         subshellStatement.IntoMode, subshellStatement,
                         (creates, createConst) => {
                           subshellStatement.IntoCreatesVariable = creates;
                           subshellStatement.IntoCreatesConst = createConst;
                         });
      break;

    case CoprocStatement coprocStatement:
      PushScope();
      foreach (var nested in coprocStatement.Body)
        CheckStatement(nested);
      PopScope();

      ResolveIntoBinding(coprocStatement.IntoVariable, coprocStatement.IntoMode,
                         coprocStatement, (creates, createConst) => {
                           coprocStatement.IntoCreatesVariable = creates;
                           coprocStatement.IntoCreatesConst = createConst;
                         });
      break;

    case WaitStatement waitStatement:
      if (waitStatement.TargetKind == WaitTargetKind.Target &&
          waitStatement.Target != null)
        CheckExpression(waitStatement.Target);

      ResolveIntoBinding(waitStatement.IntoVariable, waitStatement.IntoMode,
                         waitStatement, (creates, createConst) => {
                           waitStatement.IntoCreatesVariable = creates;
                           waitStatement.IntoCreatesConst = createConst;
                         });
      break;

    case ShellCommandStatement shellCommand:
      ValidateShellCommand(shellCommand);
      break;

    case CommandStatement:
      break;

    case ShellStatement shellStatement:
      CheckExpression(shellStatement.Command);
      break;
    case TestStatement testStatement:
      CheckExpression(testStatement.Condition);
      break;
    case TrapStatement trapStatement:
      ValidateTrapSignal(trapStatement.Signal, trapStatement);
      if (trapStatement.Handler != null) {
        if (trapStatement.Handler.Arguments.Count != 0) {
          Report(trapStatement.Handler,
                 "Trap handler function calls cannot include arguments.",
                 DiagnosticCodes.InvalidTrapHandler);
        }

        ValidateFunctionCall(trapStatement.Handler, implicitArgs: 0);
      } else if (trapStatement.Command != null) {
        CheckExpression(trapStatement.Command);
      }
      break;
    case UntrapStatement untrapStatement:
      ValidateTrapSignal(untrapStatement.Signal, untrapStatement);
      break;

    case ExpressionStatement expressionStatement:
      CheckExpression(expressionStatement.Expression);
      break;
    }
  }

  private void CheckFunction(FunctionDeclaration function) {
    PushScope();
    functionDepth++;
    bool sawDefault = false;

    foreach (var parameter in function.Parameters) {
      if (IsBuiltinIdentifier(parameter.Name)) {
        Report(parameter,
               $"Cannot declare built-in variable '{parameter.Name}'.",
               DiagnosticCodes.InvalidAssignmentTarget);
      }

      if (parameter.DefaultValue == null) {
        if (sawDefault) {
          Report(
              parameter,
              $"Required parameter '{parameter.Name}' cannot appear after defaulted parameters.",
              DiagnosticCodes.InvalidParameterDeclaration);
        }
      } else {
        sawDefault = true;
        CheckExpression(parameter.DefaultValue);
      }

      Declare(parameter.Name, isConst: false, parameter);
    }

    foreach (var statement in function.Body)
      CheckStatement(statement);

    functionDepth--;
    PopScope();
  }

  private void CheckExpression(Expression expression) {
    switch (expression) {
    case IdentifierExpression identifier:
      ValidateIdentifierUse(identifier);
      break;

    case EnumAccessExpression enumAccess:
      ValidateEnumAccess(enumAccess);
      break;

    case FunctionCallExpression functionCall:
      ValidateFunctionCall(functionCall, implicitArgs: 0);
      break;

    case ShellCaptureExpression shellCapture:
      CheckExpression(shellCapture.Command);
      break;
    case TestCaptureExpression testCapture:
      CheckExpression(testCapture.Condition);
      break;
    case ProcessSubstitutionExpression processSubstitution:
      CheckExpression(processSubstitution.Payload);
      break;

    case PipeExpression pipe:
      CheckExpression(pipe.Left);
      if (pipe.Right is IdentifierExpression target) {
        ValidateAssignmentTarget(target, isGlobal: false);
      } else if (pipe.Right is FunctionCallExpression call) {
        ValidateFunctionCall(call, implicitArgs: 1);
      } else {
        CheckExpression(pipe.Right);
      }
      break;

    case RedirectExpression redirect:
      CheckExpression(redirect.Left);
      CheckExpression(redirect.Right);
      break;

    case UnaryExpression unary:
      CheckExpression(unary.Operand);
      break;

    case BinaryExpression binary:
      CheckExpression(binary.Left);
      CheckExpression(binary.Right);
      break;

    case IndexAccessExpression indexAccess:
      CheckExpression(indexAccess.Array);
      CheckExpression(indexAccess.Index);
      break;

    case ArrayLiteral arrayLiteral:
      foreach (var element in arrayLiteral.Elements)
        CheckExpression(element);
      break;

    case MapLiteral mapLiteral:
      foreach (var entry in mapLiteral.Entries) {
        CheckExpression(entry.Key);
        CheckExpression(entry.Value);
      }
      break;
    }
  }

  private void ValidateLoopControlDepth(Statement statement, string keyword) {
    var depth = statement switch {
      BreakStatement breakStatement => breakStatement.Depth,
      ContinueStatement continueStatement => continueStatement.Depth,
      _ => null
    };

    if (depth is null)
      return;

    if (depth.Value >= 1)
      return;

    Report(statement, $"'{keyword}' depth must be a positive integer literal.",
           DiagnosticCodes.InvalidControlFlowContext);
  }

  private void ValidateFunctionCall(FunctionCallExpression functionCall,
                                    int implicitArgs) {
    foreach (var argument in functionCall.Arguments)
      CheckExpression(argument);

    if (!functions.TryGetValue(functionCall.FunctionName,
                               out var functionInfo)) {
      Report(functionCall, $"Unknown function '{functionCall.FunctionName}'.",
             DiagnosticCodes.UnknownFunction);
      return;
    }

    var actual = functionCall.Arguments.Count + implicitArgs;
    if (actual < functionInfo.RequiredParameterCount ||
        actual > functionInfo.ParameterCount) {
      Report(
          functionCall,
          $"Function '{functionCall.FunctionName}' expects {FormatArity(functionInfo.RequiredParameterCount, functionInfo.ParameterCount)}, got {actual}.",
          DiagnosticCodes.FunctionArityMismatch);
    }
  }

  private void ValidateIdentifierUse(IdentifierExpression identifier) {
    if (IsBuiltinIdentifier(identifier.Name))
      return;

    if (TryResolveSymbol(identifier.Name, out _))
      return;

    Report(identifier, $"Use of undeclared variable '{identifier.Name}'.",
           DiagnosticCodes.UndeclaredVariable);
  }

  private void ValidateAssignmentTarget(IdentifierExpression identifier,
                                        bool isGlobal) {
    if (IsBuiltinIdentifier(identifier.Name)) {
      Report(identifier,
             $"Cannot assign to built-in variable '{identifier.Name}'.",
             DiagnosticCodes.InvalidAssignmentTarget);
      return;
    }

    if (isGlobal) {
      if (!globalScope.TryGetValue(identifier.Name, out var symbol)) {
        Report(identifier, $"Use of undeclared variable '{identifier.Name}'.",
               DiagnosticCodes.UndeclaredVariable);
        return;
      }

      if (symbol.IsConst) {
        Report(identifier,
               $"Cannot assign to immutable variable '{identifier.Name}'.",
               DiagnosticCodes.InvalidAssignmentTarget);
      }

      return;
    }

    if (!TryResolveSymbol(identifier.Name, out var resolved)) {
      Report(identifier, $"Use of undeclared variable '{identifier.Name}'.",
             DiagnosticCodes.UndeclaredVariable);
      return;
    }

    if (resolved.IsConst) {
      Report(identifier,
             $"Cannot assign to immutable variable '{identifier.Name}'.",
             DiagnosticCodes.InvalidAssignmentTarget);
    }
  }

  private void
  ValidateIndexAssignmentTarget(IndexAccessExpression indexAccess) {
    CheckExpression(indexAccess.Array);
    CheckExpression(indexAccess.Index);

    if (indexAccess.Array is IdentifierExpression identifier &&
        IsBuiltinIdentifier(identifier.Name)) {
      Report(indexAccess,
             $"Cannot assign to built-in variable '{identifier.Name}'.",
             DiagnosticCodes.InvalidAssignmentTarget);
    }
  }

  private void ValidateEnumAccess(EnumAccessExpression enumAccess) {
    if (!enums.TryGetValue(enumAccess.EnumName, out var members)) {
      Report(enumAccess, $"Unknown enum '{enumAccess.EnumName}'.",
             DiagnosticCodes.UndeclaredVariable);
      return;
    }

    if (!members.Contains(enumAccess.MemberName)) {
      Report(
          enumAccess,
          $"Unknown enum member '{enumAccess.EnumName}::{enumAccess.MemberName}'.",
          DiagnosticCodes.UndeclaredVariable);
    }
  }

  private void ValidateTrapSignal(string signal, AstNode node) {
    if (TryNormalizeTrapSignal(signal, out var normalized) &&
        ValidTrapSignals.Contains(normalized))
      return;

    var suggestion = TryNormalizeTrapSignal(signal, out var typoSignal) &&
                     TrySuggestClosest(typoSignal, ValidTrapSignals, out var suggestedSignal)
                         ? signal.TrimStart().StartsWith("SIG", StringComparison.OrdinalIgnoreCase)
                               ? $"SIG{suggestedSignal}"
                               : suggestedSignal
                         : null;
    ReportWithSuggestion(node, $"'{signal}' is not a valid trap signal.",
                         DiagnosticCodes.InvalidTrapSignal, suggestion);
  }

  private static bool TryNormalizeTrapSignal(string signal,
                                             out string normalized) {
    normalized = string.Empty;
    if (string.IsNullOrWhiteSpace(signal))
      return false;

    var trimmed = signal.Trim();
    if (trimmed.StartsWith("SIG", StringComparison.OrdinalIgnoreCase))
      trimmed = trimmed[3..];

    if (trimmed.Length == 0)
      return false;

    normalized = trimmed.ToUpperInvariant();
    return normalized.All(static c => c is >= 'A' and <= 'Z');
  }

  private void ValidateShellCommand(ShellCommandStatement command) {
    switch (command.Kind) {
    case ShellCommandKind.Set:
      ValidateSetCommand(command);
      break;
    case ShellCommandKind.Export:
      ValidateExportCommand(command);
      break;
    case ShellCommandKind.Shopt:
      ValidateShoptCommand(command);
      break;
    case ShellCommandKind.Alias:
      ValidateAliasCommand(command);
      break;
    case ShellCommandKind.Source:
      ValidateSourceCommand(command);
      break;
    case ShellCommandKind.Unset:
      ValidateUnsetCommand(command);
      break;
    case ShellCommandKind.Declare:
      ValidateDeclareLikeCommand(command, "declare", ValidDeclareShortFlags,
                                 allowGlobalFlag: true);
      break;
    case ShellCommandKind.Local:
      ValidateDeclareLikeCommand(command, "local", ValidLocalShortFlags,
                                 allowGlobalFlag: false);
      break;
    }
  }

  private void ValidateSetCommand(ShellCommandStatement command) {
    var args = command.Arguments;
    for (var i = 0; i < args.Count; i++) {
      var arg = args[i];
      if (arg == "--")
        break;
      if (!TryParseOptionToken(arg, out var sign, out var optionBody))
        break;
      if (optionBody.Length == 0 || optionBody == "-" || optionBody == "+")
        continue;

      for (var flagIndex = 0; flagIndex < optionBody.Length; flagIndex++) {
        var flag = optionBody[flagIndex];
        if (flag == 'o') {
          if (flagIndex != optionBody.Length - 1) {
            Report(
                command,
                $"Invalid set option cluster '{arg}': 'o' must be the last short flag in the token.",
                DiagnosticCodes.InvalidCommandUsage);
            return;
          }

          if (i + 1 >= args.Count ||
              !ValidSetLongOptions.Contains(args[i + 1])) {
            if (i + 1 < args.Count) {
              var invalidLongOption = args[i + 1];
              var suggestion = TrySuggestClosest(invalidLongOption, ValidSetLongOptions,
                                                 out var suggestedLongOption)
                                   ? suggestedLongOption
                                   : null;
              ReportWithSuggestion(
                  command,
                  $"Invalid set option name '{invalidLongOption}' after '{arg}'.",
                  DiagnosticCodes.InvalidCommandUsage, suggestion);
            } else {
              Report(
                  command,
                  $"Invalid set option '{arg}': expected one of [{string.Join(", ", ValidSetLongOptions.OrderBy(v => v))}] after '{arg}'.",
                  DiagnosticCodes.InvalidCommandUsage);
            }
            return;
          }

          i++;
          continue;
        }

        if (!ValidSetShortFlags.Contains(flag)) {
          var suggestion = TrySuggestCaseVariantFlag(flag, ValidSetShortFlags,
                                                     out var suggestedFlag)
                               ? $"{sign}{suggestedFlag}"
                               : null;
          ReportWithSuggestion(command, $"Invalid set flag '{sign}{flag}'.",
                               DiagnosticCodes.InvalidCommandUsage, suggestion);
          return;
        }
      }
    }
  }

  private void ValidateExportCommand(ShellCommandStatement command) {
    ValidateSimpleOptionCommand(command, ValidExportShortFlags, "export",
                                ValidateExportArgument);
  }

  private void ValidateShoptCommand(ShellCommandStatement command) {
    ValidateSimpleOptionCommand(command, ValidShoptShortFlags, "shopt",
                                ValidateShoptArgument);
  }

  private void ValidateAliasCommand(ShellCommandStatement command) {
    ValidateSimpleOptionCommand(command, ValidAliasShortFlags, "alias",
                                ValidateAliasArgument);
  }

  private void ValidateSourceCommand(ShellCommandStatement command) {
    if (command.Arguments.Count == 0) {
      Report(command, "Command 'source' requires a path argument.",
             DiagnosticCodes.InvalidCommandUsage);
    }
  }

  private void ValidateUnsetCommand(ShellCommandStatement command) {
    var sawTarget = false;
    var stopOptions = false;
    foreach (var arg in command.Arguments) {
      if (!stopOptions && arg == "--") {
        stopOptions = true;
        continue;
      }

      if (!stopOptions && TryParseOptionToken(arg, out var sign, out var optionBody)) {
        if (sign != '-' || optionBody.Length == 0 || optionBody == "-") {
          Report(command, $"Invalid unset option '{arg}'.",
                 DiagnosticCodes.InvalidCommandUsage);
          return;
        }

        foreach (var flag in optionBody) {
          if (!ValidUnsetShortFlags.Contains(flag)) {
            var suggestion = TrySuggestCaseVariantFlag(
                flag, ValidUnsetShortFlags, out var suggestedFlag)
                                 ? $"-{suggestedFlag}"
                                 : null;
            ReportWithSuggestion(command, $"Invalid unset flag '-{flag}'.",
                                 DiagnosticCodes.InvalidCommandUsage,
                                 suggestion);
            return;
          }
        }

        continue;
      }

      sawTarget = true;
      if (!IsIdentifier(arg)) {
        Report(command, $"Invalid unset target '{arg}'.",
               DiagnosticCodes.InvalidCommandUsage);
        return;
      }
    }

    if (!sawTarget) {
      Report(command, "Command 'unset' requires at least one target.",
             DiagnosticCodes.InvalidCommandUsage);
    }
  }

  private void ValidateDeclareLikeCommand(
      ShellCommandStatement command, string commandName,
      HashSet<char> validFlags, bool allowGlobalFlag) {
    if (string.Equals(commandName, "local", StringComparison.Ordinal) &&
        functionDepth == 0) {
      Report(command, "Command 'local' is only valid inside functions.",
             DiagnosticCodes.InvalidCommandUsage);
      return;
    }

    foreach (var arg in command.Arguments) {
      if (arg == "--")
        continue;

      if (TryParseOptionToken(arg, out var sign, out var optionBody)) {
        if (optionBody.Length == 0 || optionBody == "-") {
          Report(command, $"Invalid {commandName} option '{arg}'.",
                 DiagnosticCodes.InvalidCommandUsage);
          return;
        }

        foreach (var flag in optionBody) {
          if (!validFlags.Contains(flag) ||
              (!allowGlobalFlag && flag == 'g')) {
            var suggestion = TrySuggestCaseVariantFlag(flag, validFlags,
                                                       out var suggestedFlag) &&
                                     (allowGlobalFlag || suggestedFlag != 'g')
                                 ? $"{sign}{suggestedFlag}"
                                 : null;
            ReportWithSuggestion(
                command, $"Invalid {commandName} flag '{sign}{flag}'.",
                DiagnosticCodes.InvalidCommandUsage, suggestion);
            return;
          }
        }

        continue;
      }

      if (TrySplitAssignment(arg, out var name)) {
        if (!IsIdentifier(name)) {
          Report(command, $"Invalid {commandName} assignment target '{name}'.",
                 DiagnosticCodes.InvalidCommandUsage);
        }

        continue;
      }

      if (!IsIdentifier(arg)) {
        Report(command, $"Invalid {commandName} target '{arg}'.",
               DiagnosticCodes.InvalidCommandUsage);
      }
    }
  }

  private void ValidateSimpleOptionCommand(
      ShellCommandStatement command, HashSet<char> allowedFlags,
      string commandName,
      Action<ShellCommandStatement, string> validateNonOptionArgument) {
    var stopOptions = false;
    foreach (var arg in command.Arguments) {
      if (!stopOptions && arg == "--") {
        stopOptions = true;
        continue;
      }

      if (!stopOptions &&
          TryParseOptionToken(arg, out var sign, out var optionBody)) {
        if (sign != '-' || optionBody.Length == 0) {
          Report(command, $"Invalid {commandName} option '{arg}'.",
                 DiagnosticCodes.InvalidCommandUsage);
          return;
        }

        foreach (var flag in optionBody) {
          if (!allowedFlags.Contains(flag)) {
            var suggestion = TrySuggestCaseVariantFlag(flag, allowedFlags,
                                                       out var suggestedFlag)
                                 ? $"-{suggestedFlag}"
                                 : null;
            ReportWithSuggestion(
                command, $"Invalid {commandName} flag '-{flag}'.",
                DiagnosticCodes.InvalidCommandUsage, suggestion);
            return;
          }
        }

        continue;
      }

      validateNonOptionArgument(command, arg);
    }
  }

  private void ValidateExportArgument(ShellCommandStatement command,
                                      string arg) {
    if (TrySplitAssignment(arg, out var name)) {
      if (!IsIdentifier(name)) {
        Report(command, $"Invalid export assignment target '{name}'.",
               DiagnosticCodes.InvalidCommandUsage);
      }

      return;
    }

    if (!IsIdentifier(arg)) {
      Report(command, $"Invalid export target '{arg}'.",
             DiagnosticCodes.InvalidCommandUsage);
    }
  }

  private void ValidateShoptArgument(ShellCommandStatement command,
                                     string arg) {
    if (arg.Length == 0) {
      Report(command, "Invalid shopt argument.",
             DiagnosticCodes.InvalidCommandUsage);
      return;
    }

    if (!arg.All(c => char.IsLetterOrDigit(c) || c is '-' or '_')) {
      Report(command, $"Invalid shopt option name '{arg}'.",
             DiagnosticCodes.InvalidCommandUsage);
    }
  }

  private void ValidateAliasArgument(ShellCommandStatement command,
                                     string arg) {
    if (TrySplitAssignment(arg, out var aliasName)) {
      if (!IsIdentifier(aliasName)) {
        Report(command, $"Invalid alias name '{aliasName}'.",
               DiagnosticCodes.InvalidCommandUsage);
      }

      return;
    }

    if (!IsIdentifier(arg)) {
      Report(command, $"Invalid alias name '{arg}'.",
             DiagnosticCodes.InvalidCommandUsage);
    }
  }

  private static bool TrySplitAssignment(string text, out string name) {
    name = string.Empty;
    var equals = text.IndexOf('=');
    if (equals <= 0)
      return false;

    name = text[..equals];
    return true;
  }

  private static bool TryParseOptionToken(string token, out char sign,
                                          out string optionBody) {
    sign = '\0';
    optionBody = string.Empty;

    if (token.Length < 2)
      return false;

    var first = token[0];
    if (first is not('-' or '+'))
      return false;

    if (token == "--") {
      sign = '-';
      optionBody = "-";
      return true;
    }

    sign = first;
    optionBody = token[1..];
    return true;
  }

  private static bool IsIdentifier(string value) {
    if (string.IsNullOrWhiteSpace(value))
      return false;
    if (!(char.IsLetter(value[0]) || value[0] == '_'))
      return false;
    for (var i = 1; i < value.Length; i++) {
      var ch = value[i];
      if (!(char.IsLetterOrDigit(ch) || ch == '_'))
        return false;
    }

    return true;
  }

  private static bool TryGetExactSwitchPatternKey(Expression pattern,
                                                  out string key) {
    key = string.Empty;

    if (pattern is LiteralExpression literal &&
        literal.LiteralType is PrimitiveType primitive) {
      switch (primitive.PrimitiveKind) {
      case PrimitiveType.Kind.Int when literal.Value is int intValue:
        key = $"int:{intValue}";
        return true;
      case PrimitiveType.Kind.Bool when literal.Value is bool boolValue:
        key = $"bool:{(boolValue ? 1 : 0)}";
        return true;
      case PrimitiveType.Kind.String:
        key = $"str:{literal.Value?.ToString() ?? string.Empty}";
        return true;
      }
    }

    if (pattern is EnumAccessExpression enumAccess) {
      key = $"enum:{enumAccess.EnumName}::{enumAccess.MemberName}";
      return true;
    }

    return false;
  }

  private void ResolveIntoBinding(string? targetName, IntoBindingMode mode,
                                  AstNode node,
                                  Action<bool, bool> setResolution) {
    setResolution(false, false);
    if (string.IsNullOrEmpty(targetName))
      return;

    if (IsBuiltinIdentifier(targetName)) {
      Report(node, $"Cannot assign to built-in variable '{targetName}'.",
             DiagnosticCodes.InvalidAssignmentTarget);
      return;
    }

    if (TryResolveSymbol(targetName, out var resolved)) {
      if (resolved.IsConst) {
        Report(node, $"Cannot assign to immutable variable '{targetName}'.",
               DiagnosticCodes.InvalidAssignmentTarget);
      }

      return;
    }

    Declare(targetName, isConst: true, node, isGlobal: false);
    setResolution(true, true);
  }

  private void PushScope() {
    scopes.Push(new Dictionary<string, SymbolInfo>(scopes.Peek(),
                                                   StringComparer.Ordinal));
    declaredInScope.Push(new HashSet<string>(StringComparer.Ordinal));
  }

  private void PopScope() {
    scopes.Pop();
    declaredInScope.Pop();
  }

  private void Declare(string name, bool isConst, AstNode node,
                       bool isGlobal = false) {
    if (IsDiscardBinding(name))
      return;

    if (isGlobal) {
      if (!globalDeclared.Add(name)) {
        Report(node, $"Duplicate declaration of '{name}' in the same scope.",
               DiagnosticCodes.DuplicateDeclaration);
        return;
      }

      globalScope[name] = new SymbolInfo(isConst);
      return;
    }

    if (!declaredInScope.Peek().Add(name)) {
      Report(node, $"Duplicate declaration of '{name}' in the same scope.",
             DiagnosticCodes.DuplicateDeclaration);
      return;
    }

    scopes.Peek()[name] = new SymbolInfo(isConst);
  }

  private bool TryResolveSymbol(string name, out SymbolInfo symbol) {
    foreach (var scope in scopes) {
      if (scope.TryGetValue(name, out symbol))
        return true;
    }

    symbol = default;
    return false;
  }

  private void Report(AstNode node, string message, string code) {
    diagnostics.AddError(WithTip(message, code), node.Line, node.Column, code);
  }

  private void ReportWithSuggestion(AstNode node, string message, string code,
                                    string? suggestion) {
    if (!string.IsNullOrWhiteSpace(suggestion))
      message = $"{message} Did you mean '{suggestion}'?";
    Report(node, message, code);
  }

  private static bool TrySuggestCaseVariantFlag(char flag,
                                                HashSet<char> allowedFlags,
                                                out char suggestion) {
    suggestion = '\0';
    if (!char.IsLetter(flag))
      return false;

    var swapped = char.IsLower(flag) ? char.ToUpperInvariant(flag)
                                     : char.ToLowerInvariant(flag);
    if (!allowedFlags.Contains(swapped))
      return false;

    suggestion = swapped;
    return true;
  }

  private static bool TrySuggestClosest(string input,
                                        IEnumerable<string> candidates,
                                        out string suggestion) {
    suggestion = string.Empty;
    if (string.IsNullOrWhiteSpace(input))
      return false;

    var probe = input.Trim();
    var maxDistance = probe.Length switch {
      <= 4 => 2,
      <= 8 => 2,
      _ => 3
    };

    var best = string.Empty;
    var bestDistance = int.MaxValue;
    var bestLengthDelta = int.MaxValue;
    var tie = false;

    foreach (var candidate in candidates) {
      if (string.IsNullOrWhiteSpace(candidate))
        continue;

      var distance = ComputeEditDistanceBounded(
          probe, candidate, maxDistance, StringComparison.OrdinalIgnoreCase);
      if (distance < 0)
        continue;

      var lengthDelta = Math.Abs(candidate.Length - probe.Length);
      if (distance < bestDistance ||
          (distance == bestDistance && lengthDelta < bestLengthDelta) ||
          (distance == bestDistance && lengthDelta == bestLengthDelta &&
           string.CompareOrdinal(candidate, best) < 0)) {
        tie = distance == bestDistance && lengthDelta == bestLengthDelta &&
              !string.Equals(best, candidate, StringComparison.Ordinal);
        best = candidate;
        bestDistance = distance;
        bestLengthDelta = lengthDelta;
      } else if (distance == bestDistance && lengthDelta == bestLengthDelta &&
                 !string.Equals(best, candidate, StringComparison.Ordinal)) {
        tie = true;
      }
    }

    if (string.IsNullOrEmpty(best) || tie)
      return false;

    suggestion = best;
    return true;
  }

  private static int ComputeEditDistanceBounded(
      string left, string right, int maxDistance, StringComparison comparison) {
    if (Math.Abs(left.Length - right.Length) > maxDistance)
      return -1;

    var rows = left.Length + 1;
    var cols = right.Length + 1;
    var previous = new int[cols];
    var current = new int[cols];

    for (var j = 0; j < cols; j++)
      previous[j] = j;

    for (var i = 1; i < rows; i++) {
      current[0] = i;
      var rowMin = current[0];
      for (var j = 1; j < cols; j++) {
        var equal = string.Compare(left, i - 1, right, j - 1, 1, comparison) == 0;
        var substitutionCost = equal ? 0 : 1;
        current[j] = Math.Min(
            Math.Min(previous[j] + 1, current[j - 1] + 1),
            previous[j - 1] + substitutionCost);
        rowMin = Math.Min(rowMin, current[j]);
      }

      if (rowMin > maxDistance)
        return -1;

      (previous, current) = (current, previous);
    }

    var result = previous[cols - 1];
    return result <= maxDistance ? result : -1;
  }

  private static string WithTip(string message, string code) {
    var tip = code switch {
      DiagnosticCodes.InvalidAssignmentTarget =>
          "Use 'var' for mutable variables, or remove this assignment.",
      DiagnosticCodes.UndeclaredVariable => null,
      DiagnosticCodes.UnknownFunction => null,
      DiagnosticCodes.FunctionArityMismatch => null,
      DiagnosticCodes.InvalidControlFlowContext => null,
      DiagnosticCodes.InvalidParameterDeclaration => null,
      DiagnosticCodes.DuplicateDeclaration => null,
      DiagnosticCodes.InvalidCommandUsage =>
          "Use a valid form for this command, or use sh to emit it directly.",
      DiagnosticCodes.InvalidReadonlyContext =>
          "Use 'let' for compile-time immutability in loops, or hoist 'readonly' outside the repeated block.",
      DiagnosticCodes.DuplicateEnumMember => null,
      DiagnosticCodes.EmptyEnumDeclaration => null,
      DiagnosticCodes.DuplicateSwitchCasePattern => null,
      DiagnosticCodes.DuplicateWildcardCase => null,
      _ => null
    };

    return DiagnosticMessage.WithTip(message, tip);
  }

  private static string FormatArity(int required, int total) {
    if (required == total)
      return total.ToString(System.Globalization.CultureInfo.InvariantCulture);
    return $"{required}..{total}";
  }

  private static bool IsBuiltinIdentifier(string name) =>
      string.Equals(name, "argv", StringComparison.Ordinal);

  private static bool IsDiscardBinding(string name) =>
      string.Equals(name, "_", StringComparison.Ordinal);

  private static bool IsImmutableDeclaration(VariableDeclaration.VarKind kind) =>
      kind is VariableDeclaration.VarKind.Let or VariableDeclaration.VarKind.Readonly;

  private readonly record struct SymbolInfo(bool IsConst);
  private readonly record struct FunctionInfo(int ParameterCount,
                                              int RequiredParameterCount);
}
