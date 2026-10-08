#!/bin/sh
# Pack and install the kernel as the `ailedger` global tool, idempotently.
#
# Why a script: `dotnet tool install` refuses with "already installed" whenever the version is
# unchanged, and `dotnet tool update` refuses an identical version too, so an iterating developer
# has to uninstall first every time. Forgetting that is how the installed tool goes stale while the
# build and the suite stay green — see Backlog/kernel-version-stamp.md.
#
# Directory.Build.props declares the release version. Commit and build-time metadata retain
# traceability; reinstalling the same release does not advance its patch number.
set -e
cd "$(dirname "$0")/.."

version=$(dotnet msbuild src/AILedger.Cli/AILedger.Cli.csproj -nologo -getProperty:Version)
sha=$(git rev-parse --short HEAD 2>/dev/null || echo unknown)
build_time=$(date -u +%s)
dirty=$(git status --porcelain 2>/dev/null | head -1)
[ -n "$dirty" ] && suffix="-dirty" || suffix=""

echo "packing $version$suffix from $sha"
dotnet pack src/AILedger.Cli/AILedger.Cli.csproj -c Release -m:1 --nologo \
    -p:Version="$version$suffix" -p:InformationalVersion="$version$suffix+$sha.t$build_time" >/dev/null

dotnet tool uninstall --global AILedger.Cli >/dev/null 2>&1 || true
dotnet tool install --global --add-source artifacts/nupkg AILedger.Cli --version "$version$suffix" >/dev/null

echo "installed: $(dotnet tool list --global | awk '/ailedger.cli/{print $2}')  from $sha$( [ -n "$dirty" ] && echo " (dirty tree)")"
# The .NET installer's /etc/paths.d/dotnet-cli-tools holds the literal '~/.dotnet/tools' with the
# tilde unexpanded, so it resolves in a login shell and fails in the non-login shell a tool call
# gets. A symlink where the other tools live is on PATH absolutely and works in both.
mkdir -p "$HOME/.local/bin"
ln -sf "$HOME/.dotnet/tools/ailedger" "$HOME/.local/bin/ailedger"
command -v ailedger >/dev/null || echo "note: $HOME/.local/bin is not on PATH in this shell"
