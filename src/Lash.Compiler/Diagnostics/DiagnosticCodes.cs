namespace Lash.Compiler.Diagnostics;

public static class DiagnosticCodes
{
    // Parse/Lex
    public const string LexInvalidToken = "E000";
    public const string ParseSyntaxError = "E001";
    public const string ParseUnclosedBlockInfo = "I001";
    public const string ParseIndentationMismatchInfo = "I002";

    // Preprocessor
    public const string PreprocessorUnknownDirective = "E010";
    public const string PreprocessorDirectiveSyntax = "E011";
    public const string PreprocessorConditionalStructure = "E012";
    public const string PreprocessorImportIo = "E013";
    public const string PreprocessorImportUsage = "E014";
    public const string PreprocessorRawUsage = "E015";
    public const string PreprocessorWarning = "W010";

    // Name/Declaration/Scope
    public const string InvalidAssignmentTarget = "E110";
    public const string UndeclaredVariable = "E111";
    public const string DuplicateDeclaration = "E112";
    public const string UnknownFunction = "E113";
    public const string FunctionArityMismatch = "E114";
    public const string InvalidControlFlowContext = "E115";
    public const string InvalidParameterDeclaration = "E116";
    public const string InvalidTrapSignal = "E117";
    public const string InvalidTrapHandler = "E118";
    public const string InvalidCommandUsage = "E119";
    public const string InvalidReadonlyContext = "E120";
    public const string DuplicateEnumMember = "E121";
    public const string EmptyEnumDeclaration = "E122";
    public const string DuplicateSwitchCasePattern = "E123";
    public const string DuplicateWildcardCase = "E124";
    public const string InvalidIntoConstContext = "E125";

    // Type/Semantics
    public const string TypeMismatch = "E200";
    public const string InvalidShellPayload = "E201";
    public const string InvalidIndexOrContainerUsage = "E202";
    public const string AmbiguousCompoundAssignment = "E203";

    // Flow/Constant Safety
    public const string MaybeUninitializedVariable = "E300";
    public const string DivisionOrModuloByZero = "E301";
    public const string InvalidShiftAmount = "E302";
    public const string InvalidForStep = "E303";

    // Codegen Feasibility
    public const string UnsupportedExpressionForCodegen = "E400";
    public const string UnsupportedStatementForCodegen = "E401";

    // Warnings
    public const string UnreachableStatement = "W500";
    public const string ShadowedVariable = "W501";
    public const string WaitJobsWithoutTrackedJobs = "W502";
    public const string UnusedVariable = "W503";
    public const string UnusedParameter = "W504";
    public const string UnusedFunction = "W505";
    public const string EquivalentIfBranches = "W506";
    public const string DuplicateSwitchCaseBody = "W507";
    public const string EquivalentBranchAssignment = "W508";
    public const string LetNeverReassigned = "W509";
    public const string NoEffectLiteralStatement = "W510";
    public const string SuspiciousSplitMultilineArgument = "W511";
    public const string PossibleMissingInterpolation = "W512";
    public const string ConstantCondition = "W513";
    public const string MalformedShellExpansion = "W514";
    public const string SuspiciousHeredocPayload = "W515";
    public const string UnusedCaptureResult = "W516";
    public const string SwitchWithoutMatchingCase = "W517";
    public const string ForStepDirectionMismatch = "W518";
    public const string NonPositiveWaitTarget = "W519";
    public const string IgnoredCaptureSideEffects = "W520";
    public const string MissingShebang = "W521";
    public const string MalformedShebang = "W522";
}
