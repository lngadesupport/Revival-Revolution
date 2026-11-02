#!/usr/bin/env python3
"""List managed .NET metadata from Unity assemblies using dnfile."""
from __future__ import annotations

import argparse
import pathlib
import sys


def inspect(path):
    try:
        import dnfile
    except ImportError as exc:
        raise RuntimeError("install dnfile first: python -m pip install dnfile") from exc
    pe = dnfile.dnPE(str(path))
    if not getattr(pe, "net", None):
        raise ValueError("no CLR/.NET metadata found")
    lines = [f"ASSEMBLY: {path}", f"SIZE: {pathlib.Path(path).stat().st_size} bytes"]
    md = pe.net.mdtables
    for table_name in ("TypeDef", "TypeRef"):
        table = getattr(md, table_name, None)
        lines.append(table_name + ":")
        if table:
            for row in table.rows:
                if table_name == "TypeDef":
                    namespace = str(row.TypeNamespace)
                    name = str(row.TypeName)
                    full_name = f"{namespace}.{name}" if namespace else name
                    lines.append(f"  TYPE {full_name}")
                    for method in row.MethodList:
                        try:
                            lines.append(f"    METHOD {method.row.Name}")
                        except Exception:
                            pass
                    for field in row.FieldList:
                        try:
                            lines.append(f"    FIELD {field.row.Name}")
                        except Exception:
                            pass
                else:
                    namespace = str(row.TypeNamespace)
                    name = str(row.TypeName)
                    scope = getattr(row.ResolutionScope, "row", None)
                    scope_name = getattr(scope, "Name", "")
                    full_name = f"{namespace}.{name}" if namespace else name
                    lines.append(f"  {full_name} [{scope_name}]")
    lines.append("MEMBER REFERENCES:")
    table = getattr(md, "MemberRef", None)
    if table:
        for row in table.rows:
            parent = getattr(row.Class, "row", None)
            parent_name = getattr(parent, "TypeName", None) or getattr(parent, "Name", None) or "?"
            lines.append(f"  {parent_name}.{row.Name}")
    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("files", nargs="+")
    args = parser.parse_args()
    failed = False
    for filename in args.files:
        try:
            print(inspect(pathlib.Path(filename)))
        except Exception as exc:
            failed = True
            print(f"ASSEMBLY: {filename}\nERROR: {exc}")
        print("\n" + "=" * 78 + "\n")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
