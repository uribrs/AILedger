#!/usr/bin/env python3
"""Copy tracked build/source inputs for the experimental worker, without host state.

This intentionally incomplete source-only snapshot is for a bounded recon experiment.
It is not a general repository export: content files, untracked files and custom SDK
inputs may require additional explicit preparation. No credentials are copied.
"""
import argparse
from pathlib import Path
import shutil
import subprocess

EXTENSIONS = {".cs", ".vb", ".fs", ".csproj", ".vbproj", ".fsproj",
              ".props", ".targets", ".sln", ".slnx", ".resx"}


def prepare(source, destination):
    source = source.resolve(strict=True)
    destination = destination.resolve()
    destination.mkdir(parents=True, exist_ok=False)
    files = subprocess.check_output(["git", "-C", str(source), "ls-files", "-z"])
    copied = 0
    for name in files.decode().split("\0"):
        relative = Path(name)
        if not name or any(p.startswith(".") or p in {"bin", "obj"} for p in relative.parts):
            continue
        if relative.suffix not in EXTENSIONS and relative.name != "global.json":
            continue
        path = source / relative
        if path.is_symlink() or any(p.is_symlink() for p in path.parents if source in p.parents):
            raise ValueError("Snapshot input cannot traverse a symbolic link")
        if not path.is_file():
            continue
        path.resolve().relative_to(source)
        target = destination / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(path, target)
        copied += 1
    (destination / "NuGet.Config").write_text(
        '<configuration><packageSources><clear/>'
        '<add key="offline-cache" value="/packages"/></packageSources></configuration>')
    print(f"Copied {copied} tracked source/build files to {destination}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("destination", type=Path)
    args = parser.parse_args()
    prepare(args.source, args.destination)
