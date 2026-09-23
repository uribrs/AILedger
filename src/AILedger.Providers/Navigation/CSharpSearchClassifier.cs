using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AILedger.Providers.Navigation;

// Recognizes ordinary search tools. Arbitrary programs and interactive stdin are not a
// filesystem sandbox; provider hooks cannot supply that guarantee.
internal static partial class CSharpSearchClassifier
{
    internal sealed record Search(IReadOnlyList<string> Targets, bool CanUseFallback);

    internal static Search? Classify(JsonObject hook, int nesting = 0)
    {
        if (nesting > 4) { throw new ArgumentException("Nested shell search requires a direct tool call."); }
        var tool = hook["tool_name"]?.GetValue<string>() ?? throw new ArgumentException("Missing tool_name.");
        var input = hook["tool_input"] as JsonObject ?? throw new ArgumentException("Missing tool_input.");
        var cwd = input["workdir"]?.GetValue<string>() ?? hook["cwd"]?.GetValue<string>()
            ?? throw new ArgumentException("Missing tool working directory.");
        cwd = RoslynSolutionPaths.Canonicalize(cwd);
        if (tool is "Grep" or "grep")
        {
            var target = Resolve(input["path"]?.GetValue<string>() ?? cwd, cwd);
            var filters = new[] { input["glob"]?.GetValue<string>(), TypeGlob(input["type"]?.GetValue<string>()) }
                .OfType<string>().ToArray();
            return MaySearchCSharp([target], filters) ? new Search([target], true) : null;
        }

        if (tool is not ("Bash" or "PowerShell" or "exec_command" or "shell" or "shell_command"))
        {
            return null;
        }

        var value = input["command"] ?? input["cmd"];
        var command = value is JsonArray array
            ? string.Join(" ", array.Select(item => item?.GetValue<string>()))
            : value?.GetValue<string>() ?? throw new ArgumentException("Missing shell command.");
        var tokens = Tokens().Matches(command).Select(match => match.Value is "\n" or "\r\n" ? ";" : Unquote(match.Value)).ToArray();
        var searches = new List<Search>();
        for (var index = 0; index < tokens.Length; index++)
        {
            var name = Path.GetFileName(tokens[index]);
            if (name == "git" && index + 2 < tokens.Length && tokens[index + 1] == "-C")
            {
                cwd = Resolve(tokens[index + 2], cwd);
                index += 2;
                continue;
            }

            if (name is "bash" or "sh" or "zsh" or "fish" && index + 2 < tokens.Length &&
                tokens[index + 1] is "-c" or "-lc" or "-ic")
            {
                var nested = Classify(new JsonObject { ["tool_name"] = "Bash", ["cwd"] = cwd,
                    ["tool_input"] = new JsonObject { ["command"] = tokens[index + 2] } }, nesting + 1);
                if (nested is not null) { searches.Add(nested with { CanUseFallback = false }); }
                index += 2;
                continue;
            }

            if (name == "cd" && index + 1 < tokens.Length && !tokens[index + 1].StartsWith('-'))
            {
                cwd = Resolve(tokens[++index], cwd);
                continue;
            }

            if (name is not ("rg" or "grep" or "egrep" or "fgrep" or "Select-String"))
            {
                continue;
            }

            var end = index + 1;
            while (end < tokens.Length && tokens[end] is not (";" or "&&" or "||" or "|"))
            {
                end++;
            }

            var search = ClassifyArguments(tokens[(index + 1)..end], cwd);
            if (search is not null)
            {
                searches.Add(search);
            }

            index = end - 1;
        }

        if (searches.Count == 0)
        {
            // Catch explicit scripted/bulk C# scans without confusing a targeted source read
            // with a symbol search. Opaque scripts remain outside this classifier's coverage.
            if (command.Contains(".cs", StringComparison.OrdinalIgnoreCase) &&
                ScriptOrBulkScan().IsMatch(command))
            {
                return new Search([cwd], false);
            }

            return null;
        }

        var targets = searches.SelectMany(search => search.Targets).Distinct(RoslynSolutionPaths.Comparer).ToArray();
        var compound = tokens.Any(token => token is ";" or "&&" or "||" or "|");
        return new Search(targets, searches.Count == 1 && !compound && searches[0].CanUseFallback);
    }

    private static Search? ClassifyArguments(string[] arguments, string cwd)
    {
        if (arguments.Contains("--files", StringComparer.Ordinal))
        {
            return null; // File-name discovery, unlike -l / --files-with-matches.
        }

        var filters = new List<string>();
        var targets = new List<string>();
        var patternSeen = false;
        var opaque = false;
        for (var index = 0; index < arguments.Length; index++)
        {
            var argument = arguments[index];
            if (argument is ">" or ">>" or "2>" or "2>>" or "1>" or "1>>")
            {
                index++; // Output redirects are not search targets; normal write permissions still apply.
                continue;
            }

            if (argument is "-g" or "--glob" or "--iglob" or "--include" or "-t" or "--type")
            {
                if (++index == arguments.Length) { break; }
                filters.Add(argument is "-t" or "--type" ? TypeGlob(arguments[index]) ?? "*" : arguments[index]);
            }
            else if (argument.StartsWith("--glob=", StringComparison.Ordinal) || argument.StartsWith("--include=", StringComparison.Ordinal))
            {
                filters.Add(Unquote(argument[(argument.IndexOf('=') + 1)..]));
            }
            else if (argument.StartsWith("-g", StringComparison.Ordinal) && argument.Length > 2)
            {
                filters.Add(Unquote(argument[2..]));
            }
            else if (argument is "-e" or "--regexp" or "-f" or "--file")
            {
                patternSeen = true;
                index++;
            }
            else if (argument is "-A" or "-B" or "-C" or "-m" or "--max-count" or "--context" or "--threads" or "--type-not" or "--exclude" or "--exclude-dir")
            {
                index++;
            }
            else if (argument.StartsWith('-'))
            {
                // Unknown options can take arguments. Do not authorize fallback for an
                // ambiguous command; ordinary flag-only options remain supported.
                opaque |= !KnownFlag().IsMatch(argument);
            }
            else if (!patternSeen)
            {
                patternSeen = true;
            }
            else
            {
                targets.Add(Resolve(argument, cwd));
                opaque |= argument.IndexOfAny(['$', '`', '*', '?', '{']) >= 0;
            }
        }

        if (targets.Count == 0 || opaque)
        {
            targets.Add(cwd);
        }

        return MaySearchCSharp(targets, opaque ? [] : filters)
            ? new Search(targets, !opaque) : null;
    }

    private static bool MaySearchCSharp(IReadOnlyList<string> targets, IReadOnlyList<string> filters)
    {
        if (targets.Any(target => target.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var positive = filters.Where(filter => !filter.StartsWith('!')).ToArray();
        if (positive.Length > 0 && positive.All(filter => NonCSharpGlob().IsMatch(filter)))
        {
            return false;
        }

        return targets.Any(ContainsCSharp);
    }

    private static bool ContainsCSharp(string target)
    {
        if (File.Exists(target))
        {
            return Path.GetExtension(target).Equals(".cs", StringComparison.OrdinalIgnoreCase);
        }

        if (!Directory.Exists(target)) { return true; }
        var pending = new Stack<string>();
        pending.Push(target);
        var visited = 0;
        while (pending.TryPop(out var directory))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (++visited > 10000) { return true; } // Unknown coverage is not evidence of absence.
                if (Path.GetExtension(entry).ToLowerInvariant() is ".cs" or ".csproj" or ".sln" or ".slnx") { return true; }
                if (Directory.Exists(entry) && Path.GetFileName(entry) is not (".git" or "node_modules" or "bin" or "obj" or ".ailedger"))
                {
                    if (new DirectoryInfo(entry).LinkTarget is not null) { return true; }
                    pending.Push(entry);
                }
            }
        }

        return false;
    }

    private static string Resolve(string value, string cwd) => RoslynSolutionPaths.Canonicalize(Path.GetFullPath(value, cwd));
    private static string Unquote(string value) => value.Length >= 2 && value[0] is '\'' or '"' && value[^1] == value[0]
        ? value[1..^1] : value;
    private static string? TypeGlob(string? type) => type switch
    {
        "cs" or "csharp" => "*.cs", "py" or "python" => "*.py", "js" => "*.js", "ts" => "*.ts",
        "json" => "*.json", "yaml" => "*.yaml", "md" or "markdown" => "*.md", null => null, _ => "*"
    };

    [GeneratedRegex("\"(?:\\\\.|[^\"])*\"|'[^']*'|&&|\\|\\||\\r?\\n|[;|]|[^\\s;|]+")]
    private static partial Regex Tokens();
    [GeneratedRegex(@"^--(?:hidden|no-ignore|line-number|files-with-matches|files-without-match|count|fixed-strings|ignore-case|word-regexp)$|^-[nrilLRwFoHvscqUE]+$|^--$")]
    private static partial Regex KnownFlag();
    [GeneratedRegex(@"^\*\.(?:py|js|ts|tsx|jsx|json|ya?ml|md|txt|xml|toml|sh|html|css|sql|csv)$", RegexOptions.IgnoreCase)]
    private static partial Regex NonCSharpGlob();
    [GeneratedRegex(@"\b(?:python[0-9.]*|node|perl|awk|xargs)\b|\bcat\s+[^;|]*\*[^\s]*\.cs", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptOrBulkScan();
}
