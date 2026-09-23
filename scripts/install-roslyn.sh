#!/bin/sh
# Optional C# navigation for governed Codex launches. No ambient MCP configuration is imported.
set -eu
version=2.18.1
destination="$HOME/.ailedger/tools/roslyn/$version"
if ! dotnet tool list --tool-path "$destination" 2>/dev/null | awk -v v="$version" \
    'tolower($1) == "roslyncodelens.mcp" && $2 == v { found=1 } END { exit !found }'; then
    mkdir -p "$destination"
    dotnet tool install RoslynCodeLens.Mcp --version "$version" --tool-path "$destination"
fi
echo "Roslyn navigation installed: $destination/roslyn-codelens-mcp"
echo "Requires .NET 10. Governed Codex launches discover a single C# solution in the working directory or its git ancestors."
