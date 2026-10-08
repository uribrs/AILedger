#!/bin/sh
# Pack and install the kernel as the `ailedger` global tool, idempotently.
#
# Install in an isolated tool directory first, then publish the complete version and its shim.
# Active worker hooks name the old version's DLL directly: uninstalling it breaks those runs.
# Retain complete prior packages; .NET's global updater cannot manage multiple retained versions,
# so use this script for subsequent installs too. See Backlog/kernel-version-stamp.md.
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

staging=$(mktemp -d "${TMPDIR:-/tmp}/ailedger-install.XXXXXX")
trap 'rm -rf "$staging"' EXIT HUP INT TERM
NUGET_PACKAGES="$staging/packages" dotnet tool install --tool-path "$staging" --source "$PWD/artifacts/nupkg" \
    AILedger.Cli --version "$version$suffix" >/dev/null
# Check the staged command before touching the current installation.
"$staging/ailedger" version >/dev/null
tool_home="$HOME/.dotnet/tools"
installed="$tool_home/.store/ailedger.cli/$version$suffix"
mkdir -p "$installed"
cp -R "$staging/.store/ailedger.cli/$version$suffix/." "$installed/"
# The generated shim refers to its .store path relative to itself, so it remains relocatable.
cp "$staging/ailedger" "$tool_home/.ailedger-next-$$"
mv -f "$tool_home/.ailedger-next-$$" "$tool_home/ailedger"

echo "installed: $version$suffix  from $sha$( [ -n "$dirty" ] && echo " (dirty tree)")"
# The .NET installer's /etc/paths.d/dotnet-cli-tools holds the literal '~/.dotnet/tools' with the
# tilde unexpanded, so it resolves in a login shell and fails in the non-login shell a tool call
# gets. A symlink where the other tools live is on PATH absolutely and works in both.
mkdir -p "$HOME/.local/bin"
ln -sf "$HOME/.dotnet/tools/ailedger" "$HOME/.local/bin/ailedger"
command -v ailedger >/dev/null || echo "note: $HOME/.local/bin is not on PATH in this shell"
