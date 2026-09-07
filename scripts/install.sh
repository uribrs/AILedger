#!/bin/sh
# Pack and install the kernel as the `ailedger` global tool, idempotently.
#
# Why a script: `dotnet tool install` refuses with "already installed" whenever the version is
# unchanged, and `dotnet tool update` refuses an identical version too, so an iterating developer
# has to uninstall first every time. Forgetting that is how the installed tool goes stale while the
# build and the suite stay green — see Backlog/kernel-version-stamp.md.
#
# The version carries the commit count and the short sha, so every build is a distinct version and
# what is installed can be traced back to a tree.
set -e
cd "$(dirname "$0")/.."

count=$(git rev-list --count HEAD 2>/dev/null || echo 0)
sha=$(git rev-parse --short HEAD 2>/dev/null || echo unknown)
dirty=$(git status --porcelain 2>/dev/null | head -1)
version="2.0.$count"
[ -n "$dirty" ] && suffix="-dirty" || suffix=""

echo "packing $version$suffix from $sha"
dotnet pack src/AILedger.Cli/AILedger.Cli.csproj -c Release -m:1 --nologo \
    -p:Version="$version$suffix" -p:InformationalVersion="$version$suffix+$sha" >/dev/null

dotnet tool uninstall --global AILedger.Cli >/dev/null 2>&1 || true
dotnet tool install --global --add-source artifacts/nupkg AILedger.Cli --version "$version$suffix" >/dev/null

echo "installed: $(dotnet tool list --global | awk '/ailedger.cli/{print $2}')  from $sha$( [ -n "$dirty" ] && echo " (dirty tree)")"
command -v ailedger >/dev/null || echo "note: \$HOME/.dotnet/tools is not on PATH in this shell"
