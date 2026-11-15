#!/usr/bin/env python3
"""Puerta estatica rapida para GrokoEngine.

No necesita .NET SDK. Sirve para detectar problemas obvios antes de compilar:
- catch vacios;
- rutas absolutas accidentales;
- mojibake común;
- archivos base faltantes.
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent
EXCLUDED_DIRS = {".git", "bin", "obj", "publish", "dist", ".vs"}


def source_files():
    for path in ROOT.rglob("*.cs"):
        if not any(part in EXCLUDED_DIRS for part in path.parts):
            yield path


def rel(path: Path) -> str:
    return path.relative_to(ROOT).as_posix()


errors: list[str] = []
warnings: list[str] = []

empty_catch = re.compile(r"catch\s*(?:\([^)]*\))?\s*\{\s*\}", re.S)
absolute_windows = re.compile(r"(?i)[A-Z]:\\(?:Users|Program Files|Windows)\\")
mojibake = re.compile(r"[ÃÂâ�]")

files = list(source_files())
for path in files:
    text = path.read_text(encoding="utf-8-sig", errors="replace")
    if empty_catch.search(text):
        warnings.append(f"{rel(path)}: contiene catch vacio")
    if absolute_windows.search(text):
        warnings.append(f"{rel(path)}: contiene una ruta absoluta de Windows")
    if "\ufffd" in text or mojibake.search(text):
        warnings.append(f"{rel(path)}: posible texto con encoding roto")

required = [
    ROOT / "GrokoEngine.sln",
    ROOT / "global.json",
]

for path in required:
    if not path.exists():
        errors.append(f"Falta archivo requerido: {rel(path)}")

print(f"Archivos C# revisados: {len(files)}")
for item in warnings:
    print("WARNING:", item)
for item in errors:
    print("ERROR:", item)

if errors:
    print(f"\nSTATIC VERIFY FAILED: {len(errors)} error(es), {len(warnings)} advertencia(s).")
    sys.exit(1)

print(f"\nSTATIC VERIFY OK: {len(warnings)} advertencia(s).")
