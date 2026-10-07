#!/bin/sh
# C# navigation for governed Codex and Claude launches. No ambient MCP configuration is imported.
set -eu
version=2.18.1
# .ailedger directories are hidden by provider confinement, including the legacy tool location.
destination="$HOME/.local/share/ailedger/tools/roslyn/$version"
if ! dotnet tool list --tool-path "$destination" 2>/dev/null | awk -v v="$version" \
    'tolower($1) == "roslyncodelens.mcp" && $2 == v { found=1 } END { exit !found }'; then
    mkdir -p "$destination"
    dotnet tool install RoslynCodeLens.Mcp --version "$version" --tool-path "$destination"
fi
echo "Roslyn navigation installed: $destination/roslyn-codelens-mcp"
echo "Requires .NET 10. Governed launches start with no solution selected; use load_solution with an authorized absolute path."
