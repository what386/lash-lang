using Lash.Compiler.Ast.Statements;
using Lash.Compiler.CodeGen;
using Lash.Compiler.Tests.TestSupport;
using Xunit;

namespace Lash.Compiler.Tests.CodeGen;

public class BashGeneratorTests
{
    [Fact]
    public void BashGenerator_EmitsSimpleStringConcat()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let greeting = "Hello" + ", " + "World"
            """);

        var bash = new BashGenerator().Generate(program);

        Assert.Contains("greeting=\"Hello, World\"", bash);
    }

    [Fact]
    public void BashGenerator_FoldsNumericConstExpressions()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let value = (1 + 2) * 3
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("value=9", bash);
    }

    [Fact]
    public void BashGenerator_EmitsArrayIndexReadAndWriteWithoutHelpers()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            var items = ["zero", "one"]
            let first = items[0]
            items[1] = "updated"
            """);

        var generator = new BashGenerator();
        var bash = generator.Generate(program);

        Assert.Contains("items=(\"zero\" \"one\")", bash);
        Assert.Contains("first=${items[0]}", bash);
        Assert.Contains("items[1]=\"updated\"", bash);
        Assert.Empty(generator.Warnings);
    }

    [Fact]
    public void BashGenerator_DoesNotEmitReadonlyForConstDeclarations()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let name = "lash"
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("name=\"lash\"", bash);
        Assert.DoesNotContain("readonly name=", bash);
    }

    [Fact]
    public void BashGenerator_EmitsLocalDeclarationsInsideFunctionsByDefault()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            fn demo()
                let x = 1
                let y = 2
            end
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("local x=1", bash);
        Assert.Contains("local y=2", bash);
        Assert.DoesNotContain("local -r y=2", bash);
    }

    [Fact]
    public void BashGenerator_EmitsGlobalDeclarationWhenRequested()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            fn demo()
                global var x = 1
                global var y = 2
            end
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.DoesNotContain("local x=1", bash);
        Assert.Contains("x=1", bash);
        Assert.DoesNotContain("local y=2", bash);
        Assert.DoesNotContain("readonly y=2", bash);
        Assert.Contains("y=2", bash);
    }

    [Fact]
    public void BashGenerator_EmitsLocalReadonlyAssociativeDeclarationInsideFunction()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            fn demo()
                readonly settings = []
                let value = settings["name"]
            end
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("local -rA settings=()", bash);
        Assert.DoesNotContain("readonly settings", bash);
    }

    [Fact]
    public void BashGenerator_EmitsReadonlyForReadonlyDeclarations()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            readonly name = "lash"
            fn demo()
                readonly local_name = "inner"
                global readonly shared = "outer"
            end
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("readonly name=\"lash\"", bash);
        Assert.Contains("local -r local_name=\"inner\"", bash);
        Assert.Contains("readonly shared=\"outer\"", bash);
    }

    [Fact]
    public void BashGenerator_EmitsArrayLengthWithHashUnary()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            var items = ["zero", "one"]
            let count = #items
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("count=${#items[@]}", bash);
    }

    [Fact]
    public void BashGenerator_EmitsSwitchAsBashCase()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            switch value
                case "a":
                    echo A
                case "b":
                    echo B
            end
            """);

        var bash = new BashGenerator().Generate(program);

        Assert.Contains("case ${value} in", bash);
        Assert.Contains("a)", bash);
        Assert.Contains("b)", bash);
        Assert.Contains("echo A", bash);
        Assert.Contains("echo B", bash);
        Assert.Contains("esac", bash);
    }

    [Fact]
    public void BashGenerator_EmitsWildcardSwitchCaseAsDefault()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            switch value
                case _:
                    echo "default"
            end
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("*)", bash);
    }

    [Fact]
    public void BashGenerator_EmitsSingleWordCommandsAsRawStatements()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            pwd
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("pwd", bash);
        Assert.DoesNotContain("${pwd}", bash);
    }

    [Fact]
    public void BashGenerator_InterpolatesInRawCommandStatements()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let planet = "Mars"
            echo $"Approaching {planet}"
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("echo \"Approaching ${planet}\"", bash);
    }

    [Fact]
    public void BashGenerator_EmitsGlobForLoopAsDirectBashForLoop()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            for file in ./*.txt
                echo $file
            end
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("for file in ./*.txt; do", bash);
        Assert.Contains("echo $file", bash);
    }

    [Fact]
    public void BashGenerator_PassesArrayArgumentsToArrayLikeFunctionParameters()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let items = ["a", "b"]

            fn join(values)
                for value in values
                    echo $value
                end
            end

            join(items)
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("local -a values=(\"${@:1}\")", bash);
        Assert.Contains("join \"${items[@]}\"", bash);
    }

    [Fact]
    public void BashGenerator_DoesNotAutoInvokeMainWhenDeclared()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            fn main()
                echo done
            end
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.DoesNotContain("main \"$@\"", bash);
    }

    [Fact]
    public void BashGenerator_EmitsIfElifElseAsBashConditionChain()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            var x = 5
            if x > 10
                echo high
            elif x > 0
                echo mid
            else
                echo low
            end
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("if (( x > 10 )); then", bash);
        Assert.Contains("elif (( x > 0 )); then", bash);
        Assert.Contains("else", bash);
        Assert.Contains("echo high", bash);
        Assert.Contains("echo mid", bash);
        Assert.Contains("echo low", bash);
        Assert.Contains("fi", bash);
    }

    [Fact]
    public void BashGenerator_LowersPipeFunctionStageIntoAssignmentCapture()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            fn greet(word)
                return "hello-" + word
            end

            let word = "Rob"
            var greeting = ""
            word | greet() | greeting
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("greeting=$(greet", bash);
        Assert.DoesNotContain("word | greet() | greeting", bash);
    }

    [Fact]
    public void BashGenerator_UsesDirectPositionalBindingForRequiredParams()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            fn greet(name, greeting = "Hello")
                return greeting + ", " + name
            end
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("local name=\"$1\"", bash);
        Assert.Contains("local greeting=\"${2-}\"", bash);
        Assert.Contains("if (( $# < 2 )); then greeting=\"Hello\"; fi", bash);
    }

    [Fact]
    public void BashGenerator_EmitsGlobalAssignmentInsideFunctionWithoutLocal()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            global var counter = 0
            fn bump()
                global counter = 1
            end
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("counter=0", bash);
        Assert.Contains("counter=1", bash);
        Assert.DoesNotContain("local counter=", bash);
    }

    [Fact]
    public void BashGenerator_EmitsInterpolatedStringsAsBashExpansion()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            var name = "Rob"
            let greeting = $"Hello {name}!"
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("greeting=\"Hello ${name}!\"", bash);
    }

    [Fact]
    public void BashGenerator_InterpolatesDottedIdentifierPaths()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            var user_name = "Rob"
            let greeting = $"Hello { user.name }!"
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("greeting=\"Hello ${user_name}!\"", bash);
    }

    [Fact]
    public void BashGenerator_LeavesMalformedDottedPlaceholdersLiteral()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            var name_bad = "expanded"
            let leading = $"A {.name} B"
            let trailing = $"A {name.} B"
            let doubled = $"A {name..bad} B"
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("leading=\"A {.name} B\"", bash);
        Assert.Contains("trailing=\"A {name.} B\"", bash);
        Assert.Contains("doubled=\"A {name..bad} B\"", bash);
        Assert.DoesNotContain("${name_bad}", bash);
    }

    [Fact]
    public void BashGenerator_UsesBackslashParityForEscapedInterpolationBraces()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            var name = "Rob"
            let odd = $"A \{name}"
            let even = $"A \\{name}"
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("odd=\"A \\\\{name}\"", bash);
        Assert.Contains("even=\"A \\\\\\\\${name}\"", bash);
    }

    [Fact]
    public void BashGenerator_FoldsInterpolatedStringWithConstInputs()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let name = "Rob"
            let greeting = $"Hello {name}!"
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("greeting=\"Hello Rob!\"", bash);
    }

    [Fact]
    public void BashGenerator_LowersEnumAccessToStringLiteral()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            enum AccountType
                Checking
                Savings
            end

            let selected = AccountType::Checking
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.DoesNotContain("enum AccountType", bash);
        Assert.Contains("selected=\"AccountTypeChecking\"", bash);
    }

    [Fact]
    public void BashGenerator_EmitsRedirectionOperators()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            fn write()
                echo "out"
                echo "err" 1>&2
            end

            fn feed()
                cat
            end

            write() >> "out.log"
            write() 2>> "err.log"
            write() &>> "all.log"
            write() > "out-truncate.log"
            write() 2> "err-truncate.log"
            write() &> "all-truncate.log"
            feed() < "input.log"
            feed() <> "rw.log"
            feed() << "payload"
            feed() << [[line1
            line2]]
            feed() <<- [[line3
            line4]]
            feed() 3>&1
            feed() 1>&-
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("write >> \"out.log\"", bash);
        Assert.Contains("write 2>> \"err.log\"", bash);
        Assert.Contains("write &>> \"all.log\"", bash);
        Assert.Contains("write > \"out-truncate.log\"", bash);
        Assert.Contains("write 2> \"err-truncate.log\"", bash);
        Assert.Contains("write &> \"all-truncate.log\"", bash);
        Assert.Contains("feed < \"input.log\"", bash);
        Assert.Contains("feed <> \"rw.log\"", bash);
        Assert.Contains("feed <<< \"payload\"", bash);
        Assert.Contains("feed <<'EOF", bash);
        Assert.Contains("feed <<-'EOF", bash);
        Assert.DoesNotContain("LASH_HEREDOC", bash);
        Assert.Contains("line1", bash);
        Assert.Contains("line2", bash);
        Assert.Contains("line3", bash);
        Assert.Contains("line4", bash);
        Assert.Contains("feed 3>&1", bash);
        Assert.Contains("feed 1>&-", bash);
    }

    [Fact]
    public void BashGenerator_EmitsProcessSubstitutionWithoutQuoting()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            fn diff_files(left, right)
                diff(left, right)
            end

            diff_files(<(sort "a.txt"), >(cat))
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("diff_files <(sort \"a.txt\") >(cat)", bash);
    }

    [Fact]
    public void BashGenerator_UsesUnquotedDelimiterForInterpolatedMultilineStdinRedirect()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            fn feed()
                cat
            end

            var name = "world"
            feed() << $[[hello
            {name}
            ]]
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("feed <<EOF", bash);
        Assert.DoesNotContain("feed <<'EOF", bash);
        Assert.Contains("${name}", bash);
    }

    [Fact]
    public void BashGenerator_PreservesMultilineRawStringContent()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let raw = [[line1
            echo "still text"
            line3]]
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("raw=\"line1", bash);
        Assert.Contains("echo \\\"still text\\\"", bash);
        Assert.DoesNotContain("__cmd echo \\\"still text\\\"", bash);
    }

    [Fact]
    public void BashGenerator_EmitsBreakAndContinueInLoopBodies()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            var keep_looping = true
            while true
            if keep_looping
                    continue
                end
                break
            end
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("while ", bash);
        Assert.Contains("; do", bash);
        Assert.Contains("continue", bash);
        Assert.Contains("break", bash);
        Assert.Contains("done", bash);
    }

    [Fact]
    public void BashGenerator_EmitsUntilLoops()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            var i = 0
            until i >= 3
                i = i + 1
            end
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("until ", bash);
        Assert.Contains("; do", bash);
        Assert.Contains("done", bash);
    }

    [Fact]
    public void BashGenerator_EmitsSelectLoops()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            select choice in ["a", "b"]
                break
            end
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("select choice in \"a\" \"b\"; do", bash);
        Assert.DoesNotContain("select choice in (", bash);
        Assert.Contains("break", bash);
        Assert.Contains("done", bash);
    }

    [Fact]
    public void BashGenerator_EmitsArrayLiteralForLoopIterableAsWordList()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            for item in ["a", "b"]
                echo item
            end
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("for item in \"a\" \"b\"; do", bash);
        Assert.DoesNotContain("for item in (", bash);
    }

    [Fact]
    public void BashGenerator_EliminatesConstantDeadIfBranches()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            if false
                echo "never"
            elif true
                echo "chosen"
            else
                echo "also-never"
            end
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("echo \"chosen\"", bash);
        Assert.DoesNotContain("if ", bash);
        Assert.DoesNotContain("elif ", bash);
        Assert.DoesNotContain("else", bash);
        Assert.DoesNotContain("never", bash);
        Assert.DoesNotContain("also-never", bash);
    }

    [Fact]
    public void BashGenerator_UsesPositionalParametersForArgvAccess()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let first = argv[0]
            let count = #argv
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.DoesNotContain("__lash_argv", bash);
        Assert.Contains("first=${@:1:1}", bash);
        Assert.Contains("count=$#", bash);
    }

    [Fact]
    public void BashGenerator_EmitsShiftAsPositionalParameterMutation()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            fn consume()
                shift 2
            end
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("__lash_shift_n=$(( 2 ))", bash);
        Assert.Contains("if (( __lash_shift_n >= $# )); then set --; else shift \"${__lash_shift_n}\"; fi", bash);
    }

    [Fact]
    public void BashGenerator_EmitsShellCaptureExpression()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let size = $(du -sh .)
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("size=$(du -sh .)", bash);
    }

    [Fact]
    public void BashGenerator_EmitsShStatementPayloadAsRawCommand()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            sh "bash dothing"
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("bash dothing", bash);
    }

    [Fact]
    public void BashGenerator_EmitsTestStatementPayloadAsBashTest()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            test "-n \"ok\""
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("[[ -n \"ok\" ]]", bash);
    }

    [Fact]
    public void BashGenerator_EmitsTestCaptureAsNumericTruthiness()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let ok = $(test "-n \"ok\"")
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("ok=$(if [[ -n \"ok\" ]]; then echo 1; else echo 0; fi)", bash);
    }

    [Fact]
    public void BashGenerator_EmitsTrapIntoFunctionAndUntrap()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            fn cleanup()
                echo "bye"
            end
            trap INT into cleanup()
            untrap INT
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("trap 'cleanup' INT", bash);
        Assert.Contains("trap - INT", bash);
    }

    [Fact]
    public void BashGenerator_EmitsTrapRawCommandPayload()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            trap EXIT "echo done"
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("trap 'echo done' EXIT", bash);
    }

    [Fact]
    public void BashGenerator_InterpolatesVariablesInShAndShellCapturePayloads()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            var name = "ok"
            let value = $(echo $"$name")
            sh $"echo {name}"
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("value=$(echo \"$name\")", bash);
        Assert.Contains("echo ${name}", bash);
    }

    [Fact]
    public void BashGenerator_InterpolatesTemplateSegmentsInsideRawCapturePayload()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            var name = "World"
            let greeting = $(printf $"Hello, {name}")
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("greeting=$(printf \"Hello, ${name}\")", bash);
    }

    [Fact]
    public void BashGenerator_ExpandsCaptureSpreadSyntaxIntoArrayExpansion()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            var values = ["a", "b", "c"]
            let csv = $(printf '%s,' $values... | sed 's/,$//')
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("csv=$(printf '%s,' \"${values[@]}\" | sed 's/,$//')", bash);
    }

    [Fact]
    public void BashGenerator_InterpolatesVariablesInsideSingleQuotedShPayloadSegments()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let raw_version = "v1.4.5"
            let version = $(echo $raw_version | sed 's/^v//')
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("version=$(echo $raw_version | sed 's/^v//')", bash);
    }

    [Fact]
    public void BashGenerator_EmitsAssociativeArraySyntaxWhenStringKeysAreUsed()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            var meta = []
            meta["name"] = "lash"
            let selected = meta["name"]
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("declare -A meta=()", bash);
        Assert.Contains("meta[\"name\"]=\"lash\"", bash);
        Assert.Contains("selected=\"lash\"", bash);
    }

    [Fact]
    public void BashGenerator_EmitsAssociativeArraySyntaxForMapLiterals()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let meta = {"name": "lash", "version": "0.14"}
            let selected = meta["name"]
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("declare -A meta=([\"name\"]=\"lash\" [\"version\"]=\"0.14\")", bash);
        Assert.Contains("selected=${meta[\"name\"]}", bash);
    }

    [Fact]
    public void BashGenerator_DoesNotFoldIndexedValueAcrossDivergentUnknownBranches()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            var meta = []
            if flag
                meta["name"] = "alpha"
            else
                meta["name"] = "beta"
            end
            let selected = meta["name"]
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("selected=${meta[\"name\"]}", bash);
    }

    [Fact]
    public void BashGenerator_OnlyFoldsInterpolatedStringWhenAllPlaceholdersResolve()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let greeting = "Hello"
            let name = $(echo who)
            let message = $"[{greeting}] {name}"
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("message=\"[${greeting}] ${name}\"", bash);
        Assert.DoesNotContain("message=\"[Hello] {name}\"", bash);
    }

    [Fact]
    public void BashGenerator_EmitsCollectionConcatForPlusEquals()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            var items = ["a"]
            items += ["b", "c"]
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("items+=(\"b\" \"c\")", bash);
    }

    [Fact]
    public void BashGenerator_EmitsArithmeticUpdatesForCompoundAndPostfixOperators()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            var n = 10
            n += 2
            n -= 3
            n *= 4
            n /= 5
            n %= 6
            n++
            n--
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("(( n += 2 ))", bash);
        Assert.Contains("(( n -= 3 ))", bash);
        Assert.Contains("(( n *= 4 ))", bash);
        Assert.Contains("(( n /= 5 ))", bash);
        Assert.Contains("(( n %= 6 ))", bash);
        Assert.Contains("(( n++ ))", bash);
        Assert.Contains("(( n-- ))", bash);
    }

    [Fact]
    public void BashGenerator_EmitsLoopDepthForBreakAndContinue()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            while true
                if flag
                    continue 2
                else
                    break 3
                end
            end
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("continue 2", bash);
        Assert.Contains("break 3", bash);
    }

    [Fact]
    public void BashGenerator_EmitsRegexMatchConditions()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let name = $(echo lash)
            if name =~ "^la"
                echo ok
            end
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("[[ ${name} =~ ^la ]]", bash);
    }

    [Fact]
    public void BashGenerator_EmitsMultiPatternSwitchCases()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let answer = $(echo yes)
            switch answer
                case "y", "yes":
                    echo ok
            end
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("y|yes)", bash);
    }

    [Fact]
    public void BashGenerator_EmitsSubshellAndWaitSyntax()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            var pid = 0
            var status = 0
            subshell into pid
                echo hi
            end &
            wait pid into status
            wait jobs into status
            wait
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("declare -a __lash_jobs=()", bash);
        Assert.Contains(") &", bash);
        Assert.Contains("pid=$!", bash);
        Assert.Contains("__lash_jobs+=(\"$!\")", bash);
        Assert.Contains("wait \"${pid}\"", bash);
        Assert.Contains("for __lash_wait_pid in \"${__lash_jobs[@]}\"; do", bash);
        Assert.Contains("wait \"${__lash_wait_pid}\"", bash);
        Assert.Contains("status=$?", bash);
    }

    [Fact]
    public void BashGenerator_EmitsCoprocAndTracksJobsForWaitJobs()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            var status = 0
            var pid = 0
            coproc into pid
                echo hi
            end
            wait jobs into status
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("coproc {", bash);
        Assert.Contains("pid=${COPROC_PID}", bash);
        Assert.Contains("__lash_jobs+=(\"${COPROC_PID}\")", bash);
        Assert.Contains("for __lash_wait_pid in \"${__lash_jobs[@]}\"; do", bash);
    }

    [Fact]
    public void BashGenerator_EmitsForegroundSubshellIntoAsExitStatusCapture()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            var status = 0
            subshell into status
                echo hi
            end
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("(", bash);
        Assert.Contains(")", bash);
        Assert.Contains("status=$?", bash);
        Assert.DoesNotContain("status=$!", bash);
    }
}
