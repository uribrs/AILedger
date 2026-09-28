#!/usr/bin/env python3
"""Verify the frozen task-1 data without a live ledger or third-party packages."""
import hashlib
import json
from pathlib import Path

root = Path(__file__).resolve().parents[2] / 'tests/Fixtures/structured-findings-v1'
manifest = json.loads((root / 'manifest.json').read_text())
for relative, expected in manifest['sha256'].items():
    actual = hashlib.sha256((root / relative).read_bytes()).hexdigest()
    if actual != expected:
        raise SystemExit(f'Frozen fixture changed: {relative}')
print(f"Verified {len(manifest['sha256'])} frozen fixture hashes.")
