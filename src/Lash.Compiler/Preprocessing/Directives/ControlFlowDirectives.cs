namespace Lash.Compiler.Preprocessing.Directives;

using Lash.Compiler.Diagnostics;

internal sealed class IfDirective : IPreprocessorDirective
{
    public string Name => "if";

    public void Apply(Directive directive, PreprocessorState state)
    {
        if (string.IsNullOrWhiteSpace(directive.Arguments))
        {
            state.AddError(
                DiagnosticMessage.WithTip("@if requires a condition expression.", "Use a condition like '@if NAME && NAME == true'."),
                DiagnosticCodes.PreprocessorDirectiveSyntax);
            state.PushConditional(new ConditionalFrame(state.IsCurrentActive, false, false, false, state.CurrentLine, state.CurrentColumn));
            return;
        }

        if (!state.TryEvaluateCondition(directive.Arguments, out var condition, out var error))
        {
            state.AddError(
                DiagnosticMessage.WithTip($"Invalid @if expression: {error}", "Use symbol names, literals, &&, ||, !, ==, !=, and parentheses."),
                DiagnosticCodes.PreprocessorDirectiveSyntax);
            state.PushConditional(new ConditionalFrame(state.IsCurrentActive, false, false, false, state.CurrentLine, state.CurrentColumn));
            return;
        }

        var parentActive = state.IsCurrentActive;
        var ifActive = parentActive && condition;
        state.PushConditional(new ConditionalFrame(parentActive, ifActive, ifActive, false, state.CurrentLine, state.CurrentColumn));
    }
}

internal sealed class ElifDirective : IPreprocessorDirective
{
    public string Name => "elif";

    public void Apply(Directive directive, PreprocessorState state)
    {
        if (!state.Conditionals.TryPop(out var frame))
        {
            state.AddError(
                "@elif without matching @if.",
                DiagnosticCodes.PreprocessorConditionalStructure);
            return;
        }

        if (string.IsNullOrWhiteSpace(directive.Arguments))
        {
            state.AddError(
                DiagnosticMessage.WithTip("@elif requires a condition expression.", "Provide a boolean expression after @elif."),
                DiagnosticCodes.PreprocessorDirectiveSyntax);
            state.Conditionals.Push(frame);
            return;
        }

        if (frame.ElseSeen)
        {
            state.AddError(
                "@elif cannot appear after @else in the same conditional block.",
                DiagnosticCodes.PreprocessorConditionalStructure);
            state.Conditionals.Push(frame with { IsActive = false });
            return;
        }

        if (!state.TryEvaluateCondition(directive.Arguments, out var condition, out var error))
        {
            state.AddError(
                DiagnosticMessage.WithTip($"Invalid @elif expression: {error}", "Use a valid directive expression after @elif."),
                DiagnosticCodes.PreprocessorDirectiveSyntax);
            state.Conditionals.Push(frame with { IsActive = false });
            return;
        }

        var elifActive = frame.ParentActive && !frame.AnyBranchMatched && condition;
        state.Conditionals.Push(frame with
        {
            AnyBranchMatched = frame.AnyBranchMatched || elifActive,
            IsActive = elifActive
        });
    }
}

internal sealed class ElseDirective : IPreprocessorDirective
{
    public string Name => "else";

    public void Apply(Directive directive, PreprocessorState state)
    {
        if (!state.Conditionals.TryPop(out var frame))
        {
            state.AddError(
                "@else without matching @if.",
                DiagnosticCodes.PreprocessorConditionalStructure);
            return;
        }

        if (frame.ElseSeen)
        {
            state.AddError(
                "Only one @else is allowed per @if block.",
                DiagnosticCodes.PreprocessorConditionalStructure);
            state.Conditionals.Push(frame);
            return;
        }

        var elseActive = frame.ParentActive && !frame.AnyBranchMatched;
        state.Conditionals.Push(frame with
        {
            AnyBranchMatched = frame.AnyBranchMatched || elseActive,
            IsActive = elseActive,
            ElseSeen = true
        });
    }
}

internal sealed class EndDirective : IPreprocessorDirective
{
    public string Name => "end";

    public void Apply(Directive directive, PreprocessorState state)
    {
        if (!string.IsNullOrWhiteSpace(directive.Arguments))
            state.AddError(
                "@end does not accept arguments.",
                DiagnosticCodes.PreprocessorDirectiveSyntax);

        if (!state.TryCloseTopBlock(out var error))
            state.AddError(error, DiagnosticCodes.PreprocessorConditionalStructure);
    }
}
