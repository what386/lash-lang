namespace Lash.Compiler.Preprocessing.Directives;

using Lash.Compiler.Diagnostics;

internal sealed class ImportDirective : IPreprocessorDirective
{
    public string Name => "import";

    public void Apply(Directive directive, PreprocessorState state)
    {
        if (!state.IsCurrentActive)
            return;

        if (state.RuntimeBlockDepth > 0)
        {
            state.AddError(
                DiagnosticMessage.WithTip(
                    "@import is only allowed at file/preprocessor scope, not inside runtime blocks.",
                    "Move @import outside runtime blocks, or close/open blocks so the directive is at top level."),
                DiagnosticCodes.PreprocessorImportUsage);
            return;
        }

        if (!DirectiveProcessor.TryParseImportArguments(directive.Arguments, out var pathExpression, out var intoVariable, out var intoMode, out var error))
        {
            state.AddError(
                DiagnosticMessage.WithTip($"Invalid @import directive: {error}", "Use '@import \"path\" [into name]'."),
                DiagnosticCodes.PreprocessorDirectiveSyntax);
            return;
        }

        state.EnqueueImport(new ImportRequest(pathExpression, intoVariable, intoMode));
    }
}

internal sealed class RawDirective : IPreprocessorDirective
{
    public string Name => "raw";

    public void Apply(Directive directive, PreprocessorState state)
    {
        if (!string.IsNullOrWhiteSpace(directive.Arguments))
            state.AddError(
                DiagnosticMessage.WithTip("@raw does not accept arguments.", "Use '@raw' on its own line and close with '@end'."),
                DiagnosticCodes.PreprocessorRawUsage);

        state.EnterRaw(state.IsCurrentActive);
    }
}
