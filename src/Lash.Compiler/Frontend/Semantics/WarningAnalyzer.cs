namespace Lash.Compiler.Frontend.Semantics;

using System.Text.RegularExpressions;
using Lash.Compiler.Ast;
using Lash.Compiler.Ast.Expressions;
using Lash.Compiler.Ast.Statements;
using Lash.Compiler.Ast.Types;
using Lash.Compiler.Diagnostics;

public sealed class WarningAnalyzer {
  private const int MaxConstRangeElements = 256;
  private static readonly Regex BracedCommandVariableRegex =
      new(@"(?<!\\)\$\{([A-Za-z_][A-Za-z0-9_]*)[^}]*\}", RegexOptions.Compiled);
  private static readonly Regex PlainCommandVariableRegex =
      new(@"(?<!\\)\$([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);
  private static readonly Regex InterpolationPlaceholderRegex =
      new(@"\{([^{}]+)\}", RegexOptions.Compiled);
  private static readonly Regex SuspiciousPlainStringPlaceholderRegex =
      new(@"(?<!\\)\{[A-Za-z_][A-Za-z0-9_.]*\}", RegexOptions.Compiled);

  private readonly DiagnosticBag diagnostics;
  private readonly Stack<ScopeFrame> scopes = new();
  private readonly Stack<int> trackedJobs = new();
  private readonly Stack<Dictionary<string, ConstValue>> constValues = new();

  public WarningAnalyzer(DiagnosticBag diagnostics) {
    this.diagnostics = diagnostics;
  }

  public void Analyze(ProgramNode program) {
    ExecuteScoped(0, () => { AnalyzeBlock(program.Statements, inLoop: false); });
  }

  private bool AnalyzeBlock(IEnumerable<Statement> statements, bool inLoop) {
    bool terminated = false;
    Statement? previousStatement = null;

    foreach (var statement in statements) {
      if (terminated) {
        AddWarning("Unreachable statement.", statement.Line, statement.Column,
                   DiagnosticCodes.UnreachableStatement);
        continue;
      }

      WarnSuspiciousNoEffectLiteral(statement, previousStatement);
      terminated = AnalyzeStatement(statement, inLoop);
      previousStatement = statement;
    }

    return terminated;
  }

  private bool AnalyzeStatement(Statement statement, bool inLoop) {
    switch (statement) {
    case VariableDeclaration variable:
      AnalyzeExpression(variable.Value);
      if (!IsDiscardBinding(variable.Name) &&
          ShouldIgnoreUnusedSymbol(variable.Name) &&
          IsCaptureExpression(variable.Value)) {
        AddWarning(
            $"Capture assigned to ignored variable '{variable.Name}' still executes for side effects.",
            variable.Line, variable.Column,
            DiagnosticCodes.IgnoredCaptureSideEffects);
      }

      DeclareVariableSymbol(variable.Name, variable.Line, variable.Column,
                            ignoreUnused: variable.IsPublic,
                            suggestConst: variable.Kind ==
                                VariableDeclaration.VarKind.Var,
                            isCaptureResult: IsCaptureExpression(variable.Value));

      if (IsDiscardBinding(variable.Name)) {
        constValues.Peek().Remove(variable.Name);
        return false;
      }

      if (variable.Kind == VariableDeclaration.VarKind.Let &&
          TryEvaluateConstValue(variable.Value, out var constValue)) {
        constValues.Peek()[variable.Name] = constValue;
      } else {
        constValues.Peek().Remove(variable.Name);
      }
      return false;

    case Assignment assignment:
      AnalyzeExpression(assignment.Value);

      if (assignment.Target is IdentifierExpression identifier) {
        ApplyVariableWrite(identifier.Name, markReassigned: true);
      } else if (assignment.Target is IndexAccessExpression indexTarget) {
        AnalyzeExpression(indexTarget.Array);
        AnalyzeExpression(indexTarget.Index);
        if (indexTarget.Array is IdentifierExpression arrayIdentifier) {
          ApplyVariableWrite(arrayIdentifier.Name, markReassigned: true);
        }
      }

      return false;
    case UpdateStatement updateStatement:
      AnalyzeExpression(updateStatement.Target);
      ApplyVariableWrite(updateStatement.Target.Name, markReassigned: true);
      return false;

    case FunctionDeclaration function:
      DeclareFunctionSymbol(function.Name, function.Line, function.Column,
                            ignoreUnused: function.IsPublic);

      ExecuteScoped(0, () => {
        foreach (var parameter in function.Parameters) {
          if (parameter.DefaultValue != null)
            AnalyzeExpression(parameter.DefaultValue);

          DeclareParameterSymbol(parameter.Name, parameter.Line,
                                 parameter.Column);
          InvalidateConst(parameter.Name);
        }

        AnalyzeBlock(function.Body, inLoop: false);
      });
      return false;

    case IfStatement ifStatement:
      AnalyzeExpression(ifStatement.Condition);
      return AnalyzeIfStatement(ifStatement, inLoop);

    case SwitchStatement switchStatement:
      AnalyzeExpression(switchStatement.Value);
      return AnalyzeSwitchStatement(switchStatement, inLoop);

    case ForLoop forLoop:
      if (forLoop.Range != null)
        AnalyzeExpression(forLoop.Range);
      if (forLoop.Step != null)
        AnalyzeExpression(forLoop.Step);
      WarnForStepDirectionMismatch(forLoop);

      ExecuteScoped(CurrentTrackedJobs(), () => {
        DeclareVariableSymbol(forLoop.Variable, forLoop.Line, forLoop.Column);
        InvalidateConst(forLoop.Variable);
        AnalyzeBlock(forLoop.Body, inLoop: true);
      });
      return false;

    case SelectLoop selectLoop:
      if (selectLoop.Options != null)
        AnalyzeExpression(selectLoop.Options);

      ExecuteScoped(CurrentTrackedJobs(), () => {
        DeclareVariableSymbol(selectLoop.Variable, selectLoop.Line,
                              selectLoop.Column);
        InvalidateConst(selectLoop.Variable);
        AnalyzeBlock(selectLoop.Body, inLoop: true);
      });
      return false;

    case WhileLoop whileLoop:
      AnalyzeExpression(whileLoop.Condition);
      if (TryEvaluateBool(whileLoop.Condition, out var whileConst))
        AddWarning(
            $"Condition is constant ({whileConst.ToString().ToLowerInvariant()}).",
            whileLoop.Line, whileLoop.Column,
            DiagnosticCodes.ConstantCondition);
      if (TryEvaluateBool(whileLoop.Condition, out var whileCondition) &&
          !whileCondition) {
        WarnBlockUnreachable(
            whileLoop.Body,
            "Unreachable loop body: condition is always false.");
        return false;
      }

      AnalyzeBranch(whileLoop.Body, inLoop: true, CurrentTrackedJobs());
      return false;

    case UntilLoop untilLoop:
      AnalyzeExpression(untilLoop.Condition);
      if (TryEvaluateBool(untilLoop.Condition, out var untilConst))
        AddWarning(
            $"Condition is constant ({untilConst.ToString().ToLowerInvariant()}).",
            untilLoop.Line, untilLoop.Column,
            DiagnosticCodes.ConstantCondition);
      if (TryEvaluateBool(untilLoop.Condition, out var untilCondition) &&
          untilCondition) {
        WarnBlockUnreachable(
            untilLoop.Body, "Unreachable loop body: condition is always true.");
        return false;
      }

      AnalyzeBranch(untilLoop.Body, inLoop: true, CurrentTrackedJobs());
      return false;

    case ReturnStatement returnStatement:
      if (returnStatement.Value != null)
        AnalyzeExpression(returnStatement.Value);
      return true;

    case BreakStatement:
      return inLoop;

    case ContinueStatement:
      return inLoop;

    case SubshellStatement subshellStatement:
      AnalyzeBranch(subshellStatement.Body, inLoop: false, baseTrackedJobs: 0);
      ApplyIntoWrite(subshellStatement.IntoVariable,
                     subshellStatement.IntoCreatesVariable,
                     subshellStatement.Line, subshellStatement.Column);

      if (subshellStatement.RunInBackground)
        SetCurrentTrackedJobs(CurrentTrackedJobs() + 1);
      return false;

    case CoprocStatement coprocStatement:
      AnalyzeBranch(coprocStatement.Body, inLoop: false, baseTrackedJobs: 0);
      ApplyIntoWrite(coprocStatement.IntoVariable,
                     coprocStatement.IntoCreatesVariable,
                     coprocStatement.Line, coprocStatement.Column);

      SetCurrentTrackedJobs(CurrentTrackedJobs() + 1);
      return false;

    case WaitStatement waitStatement when waitStatement.TargetKind ==
        WaitTargetKind.Jobs:
      if (CurrentTrackedJobs() == 0) {
        AddWarning("'wait jobs' has no tracked background jobs to wait for.",
                   waitStatement.Line, waitStatement.Column,
                   DiagnosticCodes.WaitJobsWithoutTrackedJobs);
      }
      ApplyIntoWrite(waitStatement.IntoVariable, waitStatement.IntoCreatesVariable,
                     waitStatement.Line, waitStatement.Column);

      SetCurrentTrackedJobs(0);
      return false;

    case WaitStatement waitStatement:
      if (waitStatement.TargetKind == WaitTargetKind.Target &&
          waitStatement.Target != null) {
        AnalyzeExpression(waitStatement.Target);
        if (TryEvaluateInt(waitStatement.Target, out var waitTarget) &&
            waitTarget <= 0) {
          AddWarning(
              $"'wait' target is constant non-positive value ({waitTarget}).",
              waitStatement.Target.Line, waitStatement.Target.Column,
              DiagnosticCodes.NonPositiveWaitTarget);
        }
      }
      ApplyIntoWrite(waitStatement.IntoVariable, waitStatement.IntoCreatesVariable,
                     waitStatement.Line, waitStatement.Column);

      return false;

    case ShiftStatement shiftStatement when shiftStatement.Amount != null:
      AnalyzeExpression(shiftStatement.Amount);
      return false;

    case CommandStatement commandStatement:
      AnalyzeCommandScript(commandStatement.Script, commandStatement.Line,
                           commandStatement.Column, "command");
      return false;

    case ShellStatement shellStatement:
      AnalyzeShellPayload(shellStatement.Command, "sh statement");
      return false;
    case TestStatement testStatement:
      AnalyzeShellPayload(testStatement.Condition, "test statement");
      return false;
    case TrapStatement trapStatement:
      if (trapStatement.Handler != null) {
        MarkFunctionUsed(trapStatement.Handler.FunctionName);
        foreach (var argument in trapStatement.Handler.Arguments)
          AnalyzeExpression(argument);
      } else if (trapStatement.Command != null) {
        AnalyzeExpression(trapStatement.Command);
      }
      return false;
    case UntrapStatement:
      return false;

    case ExpressionStatement expressionStatement:
      AnalyzeExpression(expressionStatement.Expression);
      return false;

    default:
      return false;
    }
  }

  private bool AnalyzeIfStatement(IfStatement ifStatement, bool inLoop) {
    WarnEquivalentIfBranches(ifStatement);

    if (TryEvaluateBool(ifStatement.Condition, out var conditionValue)) {
      AddWarning(
          $"Condition is constant ({conditionValue.ToString().ToLowerInvariant()}).",
          ifStatement.Line, ifStatement.Column, DiagnosticCodes.ConstantCondition);
      if (conditionValue) {
        var thenResult =
            AnalyzeBranch(ifStatement.ThenBlock, inLoop, CurrentTrackedJobs());
        foreach (var clause in ifStatement.ElifClauses)
          WarnBlockUnreachable(
              clause.Body,
              "Unreachable branch: previous condition is always true.",
              clause.Line, clause.Column);
        WarnBlockUnreachable(
            ifStatement.ElseBlock,
            "Unreachable branch: previous condition is always true.");
        SetCurrentTrackedJobs(thenResult.TrackedJobs);
        return thenResult.Terminated;
      }

      WarnBlockUnreachable(ifStatement.ThenBlock,
                           "Unreachable branch: condition is always false.",
                           ifStatement.Line, ifStatement.Column);
      return AnalyzeElifChain(ifStatement.ElifClauses, ifStatement.ElseBlock,
                              inLoop, CurrentTrackedJobs());
    }

    foreach (var clause in ifStatement.ElifClauses)
      AnalyzeExpression(clause.Condition);

    return AnalyzeIfPaths(ifStatement.ThenBlock,
                          ifStatement.ElifClauses.Select(c => c.Body).ToList(),
                          ifStatement.ElseBlock, inLoop, CurrentTrackedJobs());
  }

  private bool AnalyzeElifChain(IReadOnlyList<ElifClause> elifClauses,
                                List<Statement> elseBlock, bool inLoop,
                                int baseTrackedJobs) {
    for (int i = 0; i < elifClauses.Count; i++) {
      var clause = elifClauses[i];
      AnalyzeExpression(clause.Condition);

      if (!TryEvaluateBool(clause.Condition, out var clauseValue)) {
        foreach (var laterClause in elifClauses.Skip(i + 1))
          AnalyzeExpression(laterClause.Condition);

        var remainingElifs = elifClauses.Skip(i).Select(c => c.Body).ToList();
        return AnalyzeIfPaths(remainingElifs[0],
                              remainingElifs.Skip(1).ToList(), elseBlock,
                              inLoop, baseTrackedJobs);
      }

      if (!clauseValue) {
        WarnBlockUnreachable(clause.Body,
                             "Unreachable branch: condition is always false.",
                             clause.Line, clause.Column);
        continue;
      }

      var branchResult = AnalyzeBranch(clause.Body, inLoop, baseTrackedJobs);
      foreach (var laterClause in elifClauses.Skip(i + 1))
        WarnBlockUnreachable(
            laterClause.Body,
            "Unreachable branch: previous condition is always true.",
            laterClause.Line, laterClause.Column);
      WarnBlockUnreachable(
          elseBlock, "Unreachable branch: previous condition is always true.");
      SetCurrentTrackedJobs(branchResult.TrackedJobs);
      return branchResult.Terminated;
    }

    if (elseBlock.Count == 0) {
      SetCurrentTrackedJobs(baseTrackedJobs);
      return false;
    }

    var elseResult = AnalyzeBranch(elseBlock, inLoop, baseTrackedJobs);
    SetCurrentTrackedJobs(elseResult.TrackedJobs);
    return elseResult.Terminated;
  }

  private bool AnalyzeIfPaths(List<Statement> thenBlock,
                              IReadOnlyList<List<Statement>> elifBlocks,
                              List<Statement> elseBlock, bool inLoop,
                              int baseTrackedJobs) {
    var branchTracked = new List<int>();

    var thenResult = AnalyzeBranch(thenBlock, inLoop, baseTrackedJobs);
    branchTracked.Add(thenResult.TrackedJobs);

    var elifTerminates = new List<bool>();
    foreach (var block in elifBlocks) {
      var result = AnalyzeBranch(block, inLoop, baseTrackedJobs);
      elifTerminates.Add(result.Terminated);
      branchTracked.Add(result.TrackedJobs);
    }

    bool elseTerminates = false;
    if (elseBlock.Count > 0) {
      var elseResult = AnalyzeBranch(elseBlock, inLoop, baseTrackedJobs);
      elseTerminates = elseResult.Terminated;
      branchTracked.Add(elseResult.TrackedJobs);
    } else {
      branchTracked.Add(baseTrackedJobs);
    }

    SetCurrentTrackedJobs(branchTracked.Max());
    return thenResult.Terminated && elifTerminates.All(t => t) &&
           elseBlock.Count > 0 && elseTerminates;
  }

  private bool AnalyzeSwitchStatement(SwitchStatement switchStatement,
                                      bool inLoop) {
    WarnDuplicateSwitchCaseBodies(switchStatement);
    WarnWildcardSwitchOrdering(switchStatement);

    if (!TryEvaluateConstValue(switchStatement.Value, out var switchValue)) {
      foreach (var clause in switchStatement.Cases)
        if (!clause.IsWildcard)
          foreach (var pattern in clause.Patterns)
            AnalyzeExpression(pattern);
      return AnalyzeSwitchPaths(switchStatement.Cases, inLoop,
                                CurrentTrackedJobs());
    }

    for (int i = 0; i < switchStatement.Cases.Count; i++) {
      var clause = switchStatement.Cases[i];
      if (!clause.IsWildcard)
        foreach (var pattern in clause.Patterns)
          AnalyzeExpression(pattern);

      if (clause.IsWildcard) {
        var matchedWildcard =
            AnalyzeBranch(clause.Body, inLoop, CurrentTrackedJobs());
        foreach (var laterClause in switchStatement.Cases.Skip(i + 1)) {
          WarnBlockUnreachable(laterClause.Body,
                               "Unreachable case: an earlier wildcard case always matches.",
                               laterClause.Line, laterClause.Column);
        }

        SetCurrentTrackedJobs(matchedWildcard.TrackedJobs);
        return matchedWildcard.Terminated;
      }

      if (!TryEvaluateExactPatterns(clause.Patterns, out var patternValues)) {
        var remaining = switchStatement.Cases.Skip(i).ToList();
        return AnalyzeSwitchPaths(remaining, inLoop, CurrentTrackedJobs());
      }

      if (!patternValues.Any(patternValue => ConstValuesEqual(switchValue, patternValue))) {
        WarnBlockUnreachable(clause.Body,
                             "Unreachable case: pattern can never match this " +
                                 "constant switch value.",
                             clause.Line, clause.Column);
        continue;
      }

      var matched = AnalyzeBranch(clause.Body, inLoop, CurrentTrackedJobs());
      foreach (var laterClause in switchStatement.Cases.Skip(i + 1)) {
        WarnBlockUnreachable(laterClause.Body,
                             "Unreachable case: an earlier case always " +
                                 "matches this constant switch value.",
                             laterClause.Line, laterClause.Column);
      }

      SetCurrentTrackedJobs(matched.TrackedJobs);
      return matched.Terminated;
    }

    if (!switchStatement.Cases.Any(static c => c.IsWildcard)) {
      AddWarning(
          "Constant switch value has no matching case and no wildcard fallback.",
          switchStatement.Line, switchStatement.Column,
          DiagnosticCodes.SwitchWithoutMatchingCase);
    }

    return false;
  }

  private void WarnWildcardSwitchOrdering(SwitchStatement switchStatement) {
    for (var i = 0; i < switchStatement.Cases.Count - 1; i++) {
      if (!switchStatement.Cases[i].IsWildcard)
        continue;

      AddWarning("Wildcard case '_' should be the final case in a switch.",
                 switchStatement.Cases[i].Line,
                 switchStatement.Cases[i].Column,
                 DiagnosticCodes.UnreachableStatement);

      foreach (var later in switchStatement.Cases.Skip(i + 1)) {
        WarnBlockUnreachable(
            later.Body,
            "Unreachable case: an earlier wildcard case always matches.",
            later.Line, later.Column);
      }
    }
  }

  private bool AnalyzeSwitchPaths(IReadOnlyList<SwitchCaseClause> cases,
                                  bool inLoop, int baseTrackedJobs) {
    var branchTracked = new List<int>();

    foreach (var clause in cases) {
      var result = AnalyzeBranch(clause.Body, inLoop, baseTrackedJobs);
      branchTracked.Add(result.TrackedJobs);
    }

    if (branchTracked.Count > 0)
      SetCurrentTrackedJobs(branchTracked.Max());
    return false;
  }

  private BranchResult AnalyzeBranch(List<Statement> body, bool inLoop,
                                     int baseTrackedJobs) {
    var terminated = false;
    var jobs = baseTrackedJobs;
    ExecuteScoped(baseTrackedJobs, () => {
      terminated = AnalyzeBlock(body, inLoop);
      jobs = CurrentTrackedJobs();
    });

    return new BranchResult(terminated, jobs);
  }

  private void ExecuteScoped(int baseTrackedJobs, Action action) {
    PushScope();
    PushTrackedJobs(baseTrackedJobs);
    PushConstScope();
    try {
      action();
    } finally {
      PopConstScope();
      PopTrackedJobs();
      PopScope();
    }
  }

  private void WarnEquivalentIfBranches(IfStatement ifStatement) {
    if (ifStatement.ElifClauses.Count != 0 || ifStatement.ElseBlock.Count == 0)
      return;

    var blocksEquivalent =
        AreEquivalentBlocks(ifStatement.ThenBlock, ifStatement.ElseBlock);
    if (blocksEquivalent) {
      AddWarning("Redundant if: 'then' and 'else' branches are equivalent.",
                 ifStatement.Line, ifStatement.Column,
                 DiagnosticCodes.EquivalentIfBranches);
    }

    if (TryGetSingleAssignment(ifStatement.ThenBlock, out var thenAssignment) &&
        TryGetSingleAssignment(ifStatement.ElseBlock, out var elseAssignment) &&
        AreEquivalentAssignments(thenAssignment, elseAssignment)) {
      AddWarning("Redundant if assignment: both branches assign the same " +
                     "target and value.",
                 ifStatement.Line, ifStatement.Column,
                 DiagnosticCodes.EquivalentBranchAssignment);
    }
  }

  private void WarnDuplicateSwitchCaseBodies(SwitchStatement switchStatement) {
    for (var i = 0; i < switchStatement.Cases.Count; i++) {
      for (var j = i + 1; j < switchStatement.Cases.Count; j++) {
        var first = switchStatement.Cases[i];
        var second = switchStatement.Cases[j];
        if (!AreEquivalentBlocks(first.Body, second.Body))
          continue;

        AddWarning("Duplicate switch case body: this case has the same body " +
                       "as an earlier case.",
                   second.Line, second.Column,
                   DiagnosticCodes.DuplicateSwitchCaseBody);
      }
    }
  }

  private static bool TryGetSingleAssignment(List<Statement> block,
                                             out Assignment assignment) {
    if (block.Count == 1 && block[0] is Assignment single) {
      assignment = single;
      return true;
    }

    assignment = null!;
    return false;
  }

  private static bool AreEquivalentAssignments(Assignment left,
                                               Assignment right) {
    return left.IsGlobal == right.IsGlobal &&
           left.Mode == right.Mode &&
           string.Equals(left.Operator, right.Operator,
                         StringComparison.Ordinal) &&
           AreEquivalentExpressions(left.Target, right.Target) &&
           AreEquivalentExpressions(left.Value, right.Value);
  }

  private static bool AreEquivalentBlocks(IReadOnlyList<Statement> left,
                                          IReadOnlyList<Statement> right) {
    if (left.Count != right.Count)
      return false;

    for (var i = 0; i < left.Count; i++) {
      if (!AreEquivalentStatements(left[i], right[i]))
        return false;
    }

    return true;
  }

  private static bool AreEquivalentStatements(Statement left, Statement right) {
    if (left.GetType() != right.GetType())
      return false;

    return (left, right) switch {
      (CommandStatement l, CommandStatement r) =>
          string.Equals(l.Script, r.Script, StringComparison.Ordinal),
      (VariableDeclaration l, VariableDeclaration r) =>
          l.IsGlobal == r.IsGlobal && l.Kind == r.Kind &&
          string.Equals(l.Name, r.Name, StringComparison.Ordinal) &&
          AreEquivalentExpressions(l.Value, r.Value),
      (Assignment l, Assignment r) => AreEquivalentAssignments(l, r),
      (UpdateStatement l, UpdateStatement r) =>
          l.IsGlobal == r.IsGlobal &&
          string.Equals(l.Operator, r.Operator, StringComparison.Ordinal) &&
          AreEquivalentExpressions(l.Target, r.Target),
      (ExpressionStatement l, ExpressionStatement r) =>
          AreEquivalentExpressions(l.Expression, r.Expression),
      (ShellStatement l, ShellStatement r) =>
          AreEquivalentExpressions(l.Command, r.Command),
      (TestStatement l, TestStatement r) =>
          AreEquivalentExpressions(l.Condition, r.Condition),
      (ReturnStatement l, ReturnStatement r) =>
          (l.Value is null && r.Value is null) ||
          (l.Value is not null && r.Value is not null &&
           AreEquivalentExpressions(l.Value, r.Value)),
      (BreakStatement l, BreakStatement r) => l.Depth == r.Depth,
      (ContinueStatement l, ContinueStatement r) => l.Depth == r.Depth,
      (IfStatement l, IfStatement r) =>
          AreEquivalentExpressions(l.Condition, r.Condition) &&
          AreEquivalentBlocks(l.ThenBlock, r.ThenBlock) &&
          AreEquivalentElifClauses(l.ElifClauses, r.ElifClauses) &&
          AreEquivalentBlocks(l.ElseBlock, r.ElseBlock),
      (SwitchStatement l, SwitchStatement r) =>
          AreEquivalentExpressions(l.Value, r.Value) &&
          AreEquivalentSwitchCases(l.Cases, r.Cases),
      _ => false
    };
  }

  private static bool
  AreEquivalentElifClauses(IReadOnlyList<ElifClause> left,
                           IReadOnlyList<ElifClause> right) {
    if (left.Count != right.Count)
      return false;

    for (var i = 0; i < left.Count; i++) {
      if (!AreEquivalentExpressions(left[i].Condition, right[i].Condition) ||
          !AreEquivalentBlocks(left[i].Body, right[i].Body)) {
        return false;
      }
    }

    return true;
  }

  private static bool
  AreEquivalentSwitchCases(IReadOnlyList<SwitchCaseClause> left,
                           IReadOnlyList<SwitchCaseClause> right) {
    if (left.Count != right.Count)
      return false;

    for (var i = 0; i < left.Count; i++) {
      if (left[i].IsWildcard != right[i].IsWildcard ||
          !AreEquivalentExpressionLists(left[i].Patterns, right[i].Patterns) ||
          !AreEquivalentBlocks(left[i].Body, right[i].Body)) {
        return false;
      }
    }

    return true;
  }

  private static bool AreEquivalentExpressions(Expression left,
                                               Expression right) {
    if (left.GetType() != right.GetType())
      return false;

    return (left, right) switch {
      (LiteralExpression l, LiteralExpression r) =>
          l.IsInterpolated == r.IsInterpolated &&
          l.IsMultiline == r.IsMultiline &&
          l.LiteralType.PrimitiveKind == r.LiteralType.PrimitiveKind &&
          string.Equals(l.Value?.ToString(), r.Value?.ToString(),
                        StringComparison.Ordinal),
      (IdentifierExpression l, IdentifierExpression r) =>
          string.Equals(l.Name, r.Name, StringComparison.Ordinal),
      (EnumAccessExpression l, EnumAccessExpression r) =>
          string.Equals(l.EnumName, r.EnumName, StringComparison.Ordinal) &&
          string.Equals(l.MemberName, r.MemberName, StringComparison.Ordinal),
      (UnaryExpression l, UnaryExpression r) =>
          string.Equals(l.Operator, r.Operator, StringComparison.Ordinal) &&
          AreEquivalentExpressions(l.Operand, r.Operand),
      (BinaryExpression l, BinaryExpression r) =>
          string.Equals(l.Operator, r.Operator, StringComparison.Ordinal) &&
          AreEquivalentExpressions(l.Left, r.Left) &&
          AreEquivalentExpressions(l.Right, r.Right),
      (RangeExpression l, RangeExpression r) =>
          AreEquivalentExpressions(l.Start, r.Start) &&
          AreEquivalentExpressions(l.End, r.End),
      (PipeExpression l, PipeExpression r) =>
          AreEquivalentExpressions(l.Left, r.Left) &&
          AreEquivalentExpressions(l.Right, r.Right),
      (RedirectExpression l, RedirectExpression r) =>
          string.Equals(l.Operator, r.Operator, StringComparison.Ordinal) &&
          AreEquivalentExpressions(l.Left, r.Left) &&
          AreEquivalentExpressions(l.Right, r.Right),
      (IndexAccessExpression l, IndexAccessExpression r) =>
          AreEquivalentExpressions(l.Array, r.Array) &&
          AreEquivalentExpressions(l.Index, r.Index),
      (FunctionCallExpression l, FunctionCallExpression r) =>
          string.Equals(l.FunctionName, r.FunctionName,
                        StringComparison.Ordinal) &&
          AreEquivalentExpressionLists(l.Arguments, r.Arguments),
      (ArrayLiteral l, ArrayLiteral r) =>
          AreEquivalentExpressionLists(l.Elements, r.Elements),
      (MapLiteral l, MapLiteral r) =>
          AreEquivalentMapEntries(l.Entries, r.Entries),
      (ShellCaptureExpression l, ShellCaptureExpression r) =>
          AreEquivalentExpressions(l.Command, r.Command),
      (TestCaptureExpression l, TestCaptureExpression r) =>
          AreEquivalentExpressions(l.Condition, r.Condition),
      (ProcessSubstitutionExpression l, ProcessSubstitutionExpression r) =>
          l.Kind == r.Kind &&
          AreEquivalentExpressions(l.Payload, r.Payload),
      (NullLiteral, NullLiteral) => true,
      _ => false
    };
  }

  private static bool
  AreEquivalentExpressionLists(IReadOnlyList<Expression> left,
                               IReadOnlyList<Expression> right) {
    if (left.Count != right.Count)
      return false;

    for (var i = 0; i < left.Count; i++) {
      if (!AreEquivalentExpressions(left[i], right[i]))
        return false;
    }

    return true;
  }

  private static bool
  AreEquivalentMapEntries(IReadOnlyList<MapLiteralEntry> left,
                          IReadOnlyList<MapLiteralEntry> right) {
    if (left.Count != right.Count)
      return false;

    for (var i = 0; i < left.Count; i++) {
      if (!AreEquivalentExpressions(left[i].Key, right[i].Key) ||
          !AreEquivalentExpressions(left[i].Value, right[i].Value)) {
        return false;
      }
    }

    return true;
  }

  private void AnalyzeExpression(Expression expression) {
    switch (expression) {
    case LiteralExpression literal:
      AnalyzeInterpolatedLiteral(literal);
      WarnPossibleMissingInterpolation(literal);
      break;

    case IdentifierExpression identifier:
      MarkVariableRead(identifier.Name);
      break;

    case FunctionCallExpression functionCall:
      MarkFunctionUsed(functionCall.FunctionName);
      foreach (var argument in functionCall.Arguments)
        AnalyzeExpression(argument);
      break;

    case ShellCaptureExpression shellCapture:
      AnalyzeShellPayload(shellCapture.Command, "shell capture");
      break;
    case TestCaptureExpression testCapture:
      AnalyzeShellPayload(testCapture.Condition, "test capture");
      break;
    case ProcessSubstitutionExpression processSubstitution:
      AnalyzeShellPayload(processSubstitution.Payload,
                         "process substitution");
      break;

    case PipeExpression pipe:
      AnalyzeExpression(pipe.Left);
      if (pipe.Right is IdentifierExpression sink) {
        InvalidateConst(sink.Name);
      } else {
        AnalyzeExpression(pipe.Right);
      }
      break;

    case RedirectExpression redirect:
      AnalyzeExpression(redirect.Left);
      AnalyzeExpression(redirect.Right);
      break;

    case UnaryExpression unary:
      AnalyzeExpression(unary.Operand);
      break;

    case BinaryExpression binary:
      AnalyzeExpression(binary.Left);
      if (binary.Operator == "&&" &&
          TryEvaluateBool(binary.Left, out var leftAndValue) && !leftAndValue) {
        break;
      }

      if (binary.Operator == "||" &&
          TryEvaluateBool(binary.Left, out var leftOrValue) && leftOrValue) {
        break;
      }

      AnalyzeExpression(binary.Right);
      break;

    case RangeExpression range:
      AnalyzeExpression(range.Start);
      AnalyzeExpression(range.End);
      break;

    case IndexAccessExpression indexAccess:
      AnalyzeExpression(indexAccess.Array);
      AnalyzeExpression(indexAccess.Index);
      break;

    case ArrayLiteral arrayLiteral:
      foreach (var element in arrayLiteral.Elements)
        AnalyzeExpression(element);
      break;

    case MapLiteral mapLiteral:
      foreach (var entry in mapLiteral.Entries) {
        AnalyzeExpression(entry.Key);
        AnalyzeExpression(entry.Value);
      }
      break;
    }
  }

  private void AnalyzeCommandScript(string script, int line, int column,
                                    string context) {
    AnalyzeInterpolatedCommandSegments(script);

    foreach (Match match in BracedCommandVariableRegex.Matches(script))
      MarkVariableRead(match.Groups[1].Value);

    foreach (Match match in PlainCommandVariableRegex.Matches(script))
      MarkVariableRead(match.Groups[1].Value);

    WarnMalformedShellExpansion(script, line, column, context);
  }

  private void AnalyzeShellPayload(Expression payload, string context) {
    AnalyzeExpression(payload);

    if (payload is LiteralExpression { Value : string script })
      AnalyzeCommandScript(script, payload.Line, payload.Column, context);
  }

  private void AnalyzeInterpolatedLiteral(LiteralExpression literal) {
    if (!literal.IsInterpolated || literal.Value is not string template)
      return;

    AnalyzeInterpolatedTemplate(template);
  }

  private void AnalyzeInterpolatedTemplate(string template) {
    foreach (Match match in InterpolationPlaceholderRegex.Matches(template)) {
      if (TryGetInterpolationSymbolName(match.Groups[1].Value,
                                        out var symbolName))
        MarkVariableRead(symbolName);
    }
  }

  private void AnalyzeInterpolatedCommandSegments(string script) {
    for (var i = 0; i < script.Length - 1; i++) {
      if (script[i] != '$' || script[i + 1] != '"')
        continue;

      var start = i + 2;
      var end = start;
      var escaped = false;
      while (end < script.Length) {
        var ch = script[end];
        if (escaped) {
          escaped = false;
          end++;
          continue;
        }

        if (ch == '\\') {
          escaped = true;
          end++;
          continue;
        }

        if (ch == '"')
          break;

        end++;
      }

      if (end >= script.Length)
        break;

      AnalyzeInterpolatedTemplate(script[start..end]);
      i = end;
    }
  }

  private void WarnSuspiciousNoEffectLiteral(Statement statement,
                                             Statement? previousStatement) {
    if (statement is not ExpressionStatement {
          Expression: LiteralExpression literal
        })
      return;

    if (literal.LiteralType is not PrimitiveType {
          PrimitiveKind: PrimitiveType.Kind.String
        } ||
        !literal.IsMultiline)
      return;

    if (previousStatement is CommandStatement commandStatement) {
      var commandName = ExtractCommandName(commandStatement.Script);
      AddWarning(
          $"Standalone multiline string has no effect; it is not an argument to '{commandName}'.",
          statement.Line, statement.Column,
          DiagnosticCodes.SuspiciousSplitMultilineArgument);
      return;
    }

    AddWarning("Standalone multiline string has no effect.", statement.Line,
               statement.Column, DiagnosticCodes.NoEffectLiteralStatement);
  }

  private void WarnPossibleMissingInterpolation(LiteralExpression literal) {
    if (literal.IsInterpolated ||
        literal.LiteralType is not PrimitiveType {
          PrimitiveKind: PrimitiveType.Kind.String
        } ||
        literal.Value is not string text)
      return;

    if (!SuspiciousPlainStringPlaceholderRegex.IsMatch(text))
      return;

    AddWarning("String contains interpolation-like placeholders but is not interpolated.",
               literal.Line, literal.Column,
               DiagnosticCodes.PossibleMissingInterpolation);
  }

  private void WarnMalformedShellExpansion(string script, int line, int column,
                                           string context) {
    if (!HasUnclosedBracedExpansion(script))
      return;

    AddWarning(
        $"Suspicious shell expansion in {context}: '${{...}}' appears to be missing a closing '}}'.",
        line, column, DiagnosticCodes.MalformedShellExpansion);
  }

  private static bool HasUnclosedBracedExpansion(string script) {
    var cursor = 0;
    while (cursor < script.Length) {
      var open = script.IndexOf("${", cursor, StringComparison.Ordinal);
      if (open < 0)
        return false;

      var close = script.IndexOf('}', open + 2);
      if (close < 0)
        return true;

      cursor = close + 1;
    }

    return false;
  }

  private static bool IsCaptureExpression(Expression expression) {
    return expression is ShellCaptureExpression or TestCaptureExpression;
  }

  private void WarnForStepDirectionMismatch(ForLoop forLoop) {
    if (forLoop.Range is not RangeExpression rangeExpression ||
        forLoop.Step == null ||
        !TryEvaluateInt(rangeExpression.Start, out var start) ||
        !TryEvaluateInt(rangeExpression.End, out var end) ||
        !TryEvaluateInt(forLoop.Step, out var step)) {
      return;
    }

    if ((start < end && step < 0) || (start > end && step > 0)) {
      AddWarning(
          $"For-loop step ({step}) moves away from range direction {start}..{end}.",
          forLoop.Step.Line, forLoop.Step.Column,
          DiagnosticCodes.ForStepDirectionMismatch);
    }
  }

  private static string ExtractCommandName(string script) {
    var trimmed = script.TrimStart();
    if (trimmed.Length == 0)
      return "command";

    var firstSpace = trimmed.IndexOfAny([' ', '\t']);
    return firstSpace < 0 ? trimmed : trimmed[..firstSpace];
  }

  private static bool TryGetInterpolationSymbolName(string placeholder,
                                                    out string symbolName) {
    return LashIdentifier.TryGetBashPath(placeholder, out symbolName);
  }

  private void WarnBlockUnreachable(List<Statement> statements, string reason,
                                    int? line = null, int? column = null) {
    if (statements.Count == 0 && line == null)
      return;

    AddWarning(reason, line ?? statements[0].Line,
               column ?? statements[0].Column,
               DiagnosticCodes.UnreachableStatement);
  }

  private bool TryEvaluateBool(Expression expression, out bool value) {
    if (!TryEvaluateConstValue(expression, out var constValue)) {
      value = false;
      return false;
    }

    value = ToBool(constValue);
    return true;
  }

  private bool TryEvaluateInt(Expression expression, out int value) {
    if (TryEvaluateConstValue(expression, out var constValue) &&
        constValue.Kind == ConstValueKind.Int) {
      value = constValue.IntValue;
      return true;
    }

    value = 0;
    return false;
  }

  private bool TryEvaluateConstValue(Expression expression,
                                     out ConstValue value) {
    switch (expression) {
    case LiteralExpression literal when literal.LiteralType is PrimitiveType
        primitive:
      switch (primitive.PrimitiveKind) {
      case PrimitiveType.Kind.Bool when literal.Value is bool b:
        value = ConstValue.FromBool(b);
        return true;
      case PrimitiveType.Kind.Int when literal.Value is int i:
        value = ConstValue.FromInt(i);
        return true;
      case PrimitiveType.Kind.String:
        value =
            ConstValue.FromString(literal.Value?.ToString() ?? string.Empty);
        return true;
      }
      break;

    case IdentifierExpression identifier:
      if (TryResolveConst(identifier.Name, out value))
        return true;
      break;

    case ArrayLiteral arrayLiteral: {
      var elements = new List<ConstValue>(arrayLiteral.Elements.Count);
      foreach (var element in arrayLiteral.Elements) {
        if (!TryEvaluateConstValue(element, out var elementValue)) {
          value = default;
          return false;
        }

        elements.Add(elementValue);
      }

      value = ConstValue.FromArray(elements);
      return true;
    }

    case RangeExpression rangeExpression:
      if (TryEvaluateInt(rangeExpression.Start, out var rangeStart) &&
          TryEvaluateInt(rangeExpression.End, out var rangeEnd) &&
          TryBuildConstRange(rangeStart, rangeEnd, out value)) {
        return true;
      }
      break;

    case IndexAccessExpression indexAccess:
      if (!TryEvaluateConstValue(indexAccess.Array, out var source) ||
          !TryEvaluateInt(indexAccess.Index, out var index) || index < 0) {
        break;
      }

      if (source.Kind == ConstValueKind.Array) {
        if (index < source.ArrayValues.Count) {
          value = source.ArrayValues[index];
          return true;
        }

        break;
      }

      if (source.Kind == ConstValueKind.String &&
          index < source.StringValue.Length) {
        value = ConstValue.FromString(source.StringValue[index].ToString());
        return true;
      }

      break;

    case UnaryExpression unary:
      if (unary.Operator == "#") {
        if (!TryEvaluateConstValue(unary.Operand, out var lengthValue))
          break;

        switch (lengthValue.Kind) {
        case ConstValueKind.String:
          value = ConstValue.FromInt(lengthValue.StringValue.Length);
          return true;
        case ConstValueKind.Array:
          value = ConstValue.FromInt(lengthValue.ArrayValues.Count);
          return true;
        }

        break;
      }

      if (!TryEvaluateConstValue(unary.Operand, out var operandValue))
        break;

      switch (unary.Operator) {
      case "!":
        value = ConstValue.FromBool(!ToBool(operandValue));
        return true;
      case "-" when operandValue.Kind == ConstValueKind.Int:
        value = ConstValue.FromInt(-operandValue.IntValue);
        return true;
      case "+" when operandValue.Kind == ConstValueKind.Int:
        value = ConstValue.FromInt(operandValue.IntValue);
        return true;
      }
      break;

    case BinaryExpression binary:
      if (binary.Operator == "&&") {
        if (!TryEvaluateConstValue(binary.Left, out var leftAnd))
          break;

        if (!ToBool(leftAnd)) {
          value = ConstValue.FromBool(false);
          return true;
        }

        if (!TryEvaluateConstValue(binary.Right, out var rightAnd))
          break;

        value = ConstValue.FromBool(ToBool(rightAnd));
        return true;
      }

      if (binary.Operator == "||") {
        if (!TryEvaluateConstValue(binary.Left, out var leftOr))
          break;

        if (ToBool(leftOr)) {
          value = ConstValue.FromBool(true);
          return true;
        }

        if (!TryEvaluateConstValue(binary.Right, out var rightOr))
          break;

        value = ConstValue.FromBool(ToBool(rightOr));
        return true;
      }

      if (!TryEvaluateConstValue(binary.Left, out var left) ||
          !TryEvaluateConstValue(binary.Right, out var right))
        break;

      switch (binary.Operator) {
      case "==" when CanCompare(left, right):
        value = ConstValue.FromBool(ConstValuesEqual(left, right));
        return true;
      case "!=" when CanCompare(left, right):
        value = ConstValue.FromBool(!ConstValuesEqual(left, right));
        return true;
      case "<" when left.Kind == ConstValueKind.Int &&
          right.Kind == ConstValueKind.Int:
        value = ConstValue.FromBool(left.IntValue < right.IntValue);
        return true;
      case ">" when left.Kind == ConstValueKind.Int &&
          right.Kind == ConstValueKind.Int:
        value = ConstValue.FromBool(left.IntValue > right.IntValue);
        return true;
      case "<=" when left.Kind == ConstValueKind.Int &&
          right.Kind == ConstValueKind.Int:
        value = ConstValue.FromBool(left.IntValue <= right.IntValue);
        return true;
      case ">=" when left.Kind == ConstValueKind.Int &&
          right.Kind == ConstValueKind.Int:
        value = ConstValue.FromBool(left.IntValue >= right.IntValue);
        return true;
      case "+" when left.Kind == ConstValueKind.Int &&
          right.Kind == ConstValueKind.Int:
        value = ConstValue.FromInt(left.IntValue + right.IntValue);
        return true;
      case "-" when left.Kind == ConstValueKind.Int &&
          right.Kind == ConstValueKind.Int:
        value = ConstValue.FromInt(left.IntValue - right.IntValue);
        return true;
      case "*" when left.Kind == ConstValueKind.Int &&
          right.Kind == ConstValueKind.Int:
        value = ConstValue.FromInt(left.IntValue * right.IntValue);
        return true;
      case "/" when left.Kind == ConstValueKind.Int &&
          right.Kind == ConstValueKind.Int && right.IntValue != 0:
        value = ConstValue.FromInt(left.IntValue / right.IntValue);
        return true;
      case "%" when left.Kind == ConstValueKind.Int &&
          right.Kind == ConstValueKind.Int && right.IntValue != 0:
        value = ConstValue.FromInt(left.IntValue % right.IntValue);
        return true;
      case "+" when left.Kind == ConstValueKind.String &&
          right.Kind == ConstValueKind.String:
        value = ConstValue.FromString(left.StringValue + right.StringValue);
        return true;
      case ".." when left.Kind == ConstValueKind.Int &&
          right.Kind == ConstValueKind.Int:
        return TryBuildConstRange(left.IntValue, right.IntValue, out value);
      }

      break;
    }

    value = default;
    return false;
  }

  private static bool TryBuildConstRange(int start, int end,
                                         out ConstValue value) {
    var length = Math.Abs(end - start) + 1;
    if (length > MaxConstRangeElements) {
      value = default;
      return false;
    }

    var step = start <= end ? 1 : -1;
    var elements = new List<ConstValue>(length);
    for (int current = start;; current += step) {
      elements.Add(ConstValue.FromInt(current));
      if (current == end)
        break;
    }

    value = ConstValue.FromArray(elements);
    return true;
  }

  private bool TryResolveConst(string name, out ConstValue value) {
    foreach (var scope in constValues) {
      if (scope.TryGetValue(name, out value))
        return true;
    }

    value = default;
    return false;
  }

  private static bool IsExactPattern(Expression pattern) {
    if (pattern is not LiteralExpression literal ||
        literal.LiteralType is not PrimitiveType primitive) {
      return false;
    }

    if (primitive.PrimitiveKind != PrimitiveType.Kind.String)
      return true;

    var text = literal.Value?.ToString() ?? string.Empty;
    return text.IndexOfAny(['*', '?', '[', ']']) < 0;
  }

  private bool TryEvaluateExactPatterns(IReadOnlyList<Expression> patterns,
                                        out List<ConstValue> values) {
    values = new List<ConstValue>(patterns.Count);
    foreach (var pattern in patterns) {
      if (!TryEvaluateConstValue(pattern, out var value) ||
          !IsExactPattern(pattern)) {
        values.Clear();
        return false;
      }

      values.Add(value);
    }

    return true;
  }

  private static bool ToBool(ConstValue value) {
    return value.Kind switch { ConstValueKind.Bool => value.BoolValue,
                               ConstValueKind.Int => value.IntValue != 0,
                               ConstValueKind.String =>
                                   !string.IsNullOrEmpty(value.StringValue),
                               ConstValueKind.Array =>
                                   value.ArrayValues.Count > 0,
                               _ => false };
  }

  private static bool CanCompare(ConstValue left, ConstValue right) {
    return left.Kind == right.Kind;
  }

  private static bool ConstValuesEqual(ConstValue left, ConstValue right) {
    if (left.Kind != right.Kind)
      return false;

    return left.Kind switch {
      ConstValueKind.Bool => left.BoolValue == right.BoolValue,
      ConstValueKind.Int => left.IntValue == right.IntValue,
      ConstValueKind.String => string.Equals(
          left.StringValue, right.StringValue, StringComparison.Ordinal),
      ConstValueKind.Array => ArraysEqual(left.ArrayValues, right.ArrayValues),
      _ => false
    };
  }

  private static bool ArraysEqual(IReadOnlyList<ConstValue> left,
                                  IReadOnlyList<ConstValue> right) {
    if (left.Count != right.Count)
      return false;

    for (int i = 0; i < left.Count; i++) {
      if (!ConstValuesEqual(left[i], right[i]))
        return false;
    }

    return true;
  }

  private void WarnIfShadowing(string name, int line, int column) {
    if (scopes.Count <= 1)
      return;

    bool first = true;
    foreach (var scope in scopes) {
      if (first) {
        first = false;
        continue;
      }

      if (!scope.Symbols.ContainsKey(name))
        continue;

      AddWarning($"Declaration '{name}' shadows an outer scope variable.", line,
                 column, DiagnosticCodes.ShadowedVariable);
      return;
    }
  }

  private void DeclareVariableSymbol(string name, int line, int column,
                                     bool ignoreUnused = false,
                                     bool suggestConst = false,
                                     bool isCaptureResult = false) {
    if (IsDiscardBinding(name))
      return;

    WarnIfShadowing(name, line, column);
    DeclareSymbol(name, SymbolKind.Variable, line, column,
                  ignoreUnused: ignoreUnused || ShouldIgnoreUnusedSymbol(name),
                  suggestConst: suggestConst,
                  isCaptureResult: isCaptureResult);
  }

  private void DeclareFunctionSymbol(string name, int line, int column,
                                     bool ignoreUnused = false) {
    WarnIfShadowing(name, line, column);
    DeclareSymbol(name, SymbolKind.Function, line, column,
                  ignoreUnused: ignoreUnused || ShouldIgnoreUnusedSymbol(name));
  }

  private void DeclareParameterSymbol(string name, int line, int column) {
    WarnIfShadowing(name, line, column);
    DeclareSymbol(name, SymbolKind.Parameter, line, column,
                  ignoreUnused: ShouldIgnoreUnusedSymbol(name));
  }

  private void ApplyVariableWrite(string name, bool markReassigned) {
    InvalidateConst(name);
    if (markReassigned)
      MarkVariableReassigned(name);
  }

  private void ApplyIntoWrite(string? variableName, bool createsVariable,
                              int line, int column) {
    if (string.IsNullOrEmpty(variableName))
      return;

    if (createsVariable) {
      DeclareVariableSymbol(variableName, line, column);
      ApplyVariableWrite(variableName, markReassigned: false);
      return;
    }

    ApplyVariableWrite(variableName, markReassigned: true);
  }

  private void PushScope() { scopes.Push(new ScopeFrame()); }

  private void PopScope() {
    var scope = scopes.Pop();
    EmitUnusedSymbolWarnings(scope);
  }

  private void DeclareSymbol(string name, SymbolKind kind, int line, int column,
                             bool ignoreUnused, bool suggestConst = false,
                             bool isCaptureResult = false) {
    scopes.Peek().Symbols[name] =
        new SymbolEntry(name, kind, line, column, ignoreUnused, suggestConst,
                        isCaptureResult);
  }

  private void MarkVariableRead(string name) {
    foreach (var scope in scopes) {
      if (!scope.Symbols.TryGetValue(name, out var symbol))
        continue;

      if (symbol.Kind is SymbolKind.Variable or SymbolKind.Parameter)
        symbol.IsUsed = true;
      return;
    }
  }

  private void MarkFunctionUsed(string name) {
    foreach (var scope in scopes) {
      if (!scope.Symbols.TryGetValue(name, out var symbol))
        continue;

      if (symbol.Kind == SymbolKind.Function)
        symbol.IsUsed = true;
      return;
    }
  }

  private void MarkVariableReassigned(string name) {
    foreach (var scope in scopes) {
      if (!scope.Symbols.TryGetValue(name, out var symbol))
        continue;

      if (symbol.Kind == SymbolKind.Variable)
        symbol.IsReassigned = true;
      return;
    }
  }

  private void EmitUnusedSymbolWarnings(ScopeFrame scope) {
    foreach (var symbol in scope.Symbols.Values.OrderBy(static s => s.Line)
                 .ThenBy(static s => s.Column)) {
      if (symbol.Kind == SymbolKind.Variable && symbol.SuggestConst &&
          symbol.IsUsed && !symbol.IsReassigned) {
        AddWarning(
            $"Variable '{symbol.Name}' is declared mutably but never reassigned.",
            symbol.Line, symbol.Column, DiagnosticCodes.LetNeverReassigned);
      }

      if (symbol.IsUsed || symbol.IgnoreUnused)
        continue;

      switch (symbol.Kind) {
      case SymbolKind.Variable:
        if (symbol.IsCaptureResult) {
          AddWarning($"Captured result '{symbol.Name}' is never used.",
                     symbol.Line, symbol.Column,
                     DiagnosticCodes.UnusedCaptureResult);
        } else {
          AddWarning($"Variable '{symbol.Name}' is declared but never used.",
                     symbol.Line, symbol.Column, DiagnosticCodes.UnusedVariable);
        }
        break;

      case SymbolKind.Parameter:
        AddWarning($"Parameter '{symbol.Name}' is never used.", symbol.Line,
                   symbol.Column, DiagnosticCodes.UnusedParameter);
        break;

      case SymbolKind.Function:
        AddWarning($"Function '{symbol.Name}' is declared but never called.",
                   symbol.Line, symbol.Column, DiagnosticCodes.UnusedFunction);
        break;
      }
    }
  }

  private static bool ShouldIgnoreUnusedSymbol(string name) {
    return string.IsNullOrEmpty(name) || name.StartsWith('_');
  }

  private static bool IsDiscardBinding(string name) =>
      string.Equals(name, "_", StringComparison.Ordinal);

  private void PushTrackedJobs(int count) { trackedJobs.Push(count); }

  private void PopTrackedJobs() { trackedJobs.Pop(); }

  private int CurrentTrackedJobs() { return trackedJobs.Peek(); }

  private void SetCurrentTrackedJobs(int count) {
    trackedJobs.Pop();
    trackedJobs.Push(count);
  }

  private void PushConstScope() {
    var scope = constValues.Count == 0
                    ? new Dictionary<string, ConstValue>(StringComparer.Ordinal)
                    : new Dictionary<string, ConstValue>(
                          constValues.Peek(), StringComparer.Ordinal);
    constValues.Push(scope);
  }

  private void PopConstScope() { constValues.Pop(); }

  private void InvalidateConst(string name) { constValues.Peek().Remove(name); }

  private void AddWarning(string message, int line, int column, string code) {
    var tip = code switch {
      DiagnosticCodes.UnreachableStatement =>
          "Make this path reachable or remove the statement.",
      DiagnosticCodes.ShadowedVariable => "Rename the inner variable.",
      DiagnosticCodes.WaitJobsWithoutTrackedJobs =>
          "Start a background job before 'wait jobs'.",
      DiagnosticCodes.UnusedVariable => "Prefix with '_' to mark as unused.",
      DiagnosticCodes.UnusedParameter => "Prefix with '_' to mark as unused.",
      DiagnosticCodes.UnusedFunction =>
          "Prefix with '_' if intentionally unused.",
      DiagnosticCodes.EquivalentIfBranches =>
          "Keep one branch; both are equivalent.",
      DiagnosticCodes.DuplicateSwitchCaseBody => "Merge duplicate case bodies.",
      DiagnosticCodes.EquivalentBranchAssignment =>
          "Move assignment outside the conditional.",
      DiagnosticCodes.LetNeverReassigned => "Use 'let' instead of 'var'.",
      DiagnosticCodes.NoEffectLiteralStatement =>
          "Assign it or pass it to a command.",
      DiagnosticCodes.SuspiciousSplitMultilineArgument =>
          "Put the multiline literal on the same line as the command.",
      DiagnosticCodes.PossibleMissingInterpolation =>
          "Use $\"...\" or $[[...]] if placeholders should be expanded.",
      DiagnosticCodes.ConstantCondition =>
          "Simplify or remove the constant branch condition.",
      DiagnosticCodes.MalformedShellExpansion =>
          "Close braced expansions like '${name}'.",
      DiagnosticCodes.UnusedCaptureResult =>
          "Remove the capture or prefix the variable with '_' to mark it intentionally unused.",
      DiagnosticCodes.SwitchWithoutMatchingCase =>
          "Add a matching case or a wildcard case '_'.",
      DiagnosticCodes.ForStepDirectionMismatch =>
          "Use a step sign that progresses toward the range endpoint.",
      DiagnosticCodes.NonPositiveWaitTarget =>
          "Wait targets should be positive process IDs.",
      DiagnosticCodes.IgnoredCaptureSideEffects =>
          "Use 'sh ...' for side-effect-only commands, or store the capture in a non-ignored variable.",
      _ => null
    };

    diagnostics.AddWarning(DiagnosticMessage.WithTip(message, tip), line,
                           column, code);
  }

  private readonly record struct BranchResult(bool Terminated, int TrackedJobs);

  private sealed class ScopeFrame {
    public Dictionary<string, SymbolEntry> Symbols { get; } =
        new(StringComparer.Ordinal);
  }

  private sealed class SymbolEntry {
    public SymbolEntry(string name, SymbolKind kind, int line, int column,
                       bool ignoreUnused, bool suggestConst,
                       bool isCaptureResult) {
      Name = name;
      Kind = kind;
      Line = line;
      Column = column;
      IgnoreUnused = ignoreUnused;
      SuggestConst = suggestConst;
      IsCaptureResult = isCaptureResult;
    }

    public string Name { get; }
    public SymbolKind Kind { get; }
    public int Line { get; }
    public int Column { get; }
    public bool IgnoreUnused { get; }
    public bool SuggestConst { get; }
    public bool IsCaptureResult { get; }
    public bool IsUsed { get; set; }
    public bool IsReassigned { get; set; }
  }

  private enum SymbolKind { Variable, Parameter, Function }

  private enum ConstValueKind { Bool, Int, String, Array }

  private readonly
      record struct ConstValue(ConstValueKind Kind, bool BoolValue,
                               int IntValue, string StringValue,
                               IReadOnlyList<ConstValue> ArrayValues) {
    public static ConstValue
    FromBool(bool value) => new(ConstValueKind.Bool, value, 0, string.Empty,
                                Array.Empty<ConstValue>());
    public static ConstValue
    FromInt(int value) => new(ConstValueKind.Int, false, value, string.Empty,
                              Array.Empty<ConstValue>());
    public static ConstValue FromString(string value) =>
        new(ConstValueKind.String, false, 0, value, Array.Empty<ConstValue>());
    public static ConstValue FromArray(IReadOnlyList<ConstValue> value) =>
        new(ConstValueKind.Array, false, 0, string.Empty, value);
  }
}
