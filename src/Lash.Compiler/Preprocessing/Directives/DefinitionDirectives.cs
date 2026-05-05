namespace Lash.Compiler.Preprocessing.Directives;

using Lash.Compiler.Diagnostics;

internal sealed class DefineDirective : IPreprocessorDirective
{
    public string Name => "define";

    public void Apply(Directive directive, PreprocessorState state)
    {
        if (!state.IsCurrentActive)
            return;

        if (!DirectiveProcessor.TryParseDefinition(directive.Arguments, out var name, out var value, out var error))
        {
            state.AddError(
                DiagnosticMessage.WithTip($"Invalid @define directive: {error}", "Use '@define NAME', '@define NAME=value', or '@define NAME value'."),
                DiagnosticCodes.PreprocessorDirectiveSyntax);
            return;
        }

        state.Symbols[name] = value;
    }
}

internal sealed class UndefDirective : IPreprocessorDirective
{
    public string Name => "undef";

    public void Apply(Directive directive, PreprocessorState state)
    {
        if (!state.IsCurrentActive)
            return;

        if (!DirectiveProcessor.TryParseSymbolName(directive.Arguments, out var name, out var error))
        {
            state.AddError(
                DiagnosticMessage.WithTip($"Invalid @undef directive: {error}", "Use '@undef NAME'."),
                DiagnosticCodes.PreprocessorDirectiveSyntax);
            return;
        }

        state.Symbols.Remove(name);
    }
}
