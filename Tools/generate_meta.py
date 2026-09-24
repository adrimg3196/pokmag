#!/usr/bin/env python3
"""Create missing Unity .meta files for the HoloTable package (deterministic GUIDs).

Packages installed from a git URL are immutable: Unity ignores any file without a committed
.meta. Run this after adding files, or with --check in CI to fail when one is missing.

    python3 Tools/generate_meta.py          # write missing metas
    python3 Tools/generate_meta.py --check  # exit 1 if any meta is missing
"""
import hashlib
import os
import sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Packages", "com.adrimg.holotable")
ROOT = os.path.normpath(ROOT)

TAIL = "  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
IMPORTERS = {
    ".cs": "MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n",
    ".asset": "NativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 11400000\n",
    ".asmdef": "AssemblyDefinitionImporter:\n  externalObjects: {}\n",
    ".json": "TextScriptImporter:\n  externalObjects: {}\n",
    ".md": "TextScriptImporter:\n  externalObjects: {}\n",
}


def guid_for(relative_path: str) -> str:
    # Kept compatible with the GUIDs generated when the files lived under Assets/.
    return hashlib.md5(("pokmag-holotable:package:" + relative_path).encode()).hexdigest()


def meta_content(path: str, is_dir: bool) -> str:
    guid = guid_for(os.path.relpath(path, ROOT).replace(os.sep, "/"))
    if is_dir:
        return f"fileFormatVersion: 2\nguid: {guid}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n" + TAIL
    importer = IMPORTERS.get(os.path.splitext(path)[1], "DefaultImporter:\n  externalObjects: {}\n")
    return f"fileFormatVersion: 2\nguid: {guid}\n" + importer + TAIL


def missing_metas():
    for dirpath, dirnames, filenames in os.walk(ROOT):
        # Folders ending in "~" (Samples~, Documentation~) are not imported, but their
        # contents are copied into projects on sample import, so they still need metas.
        for name in dirnames:
            if name.endswith("~"):
                continue
            path = os.path.join(dirpath, name)
            if not os.path.exists(path + ".meta"):
                yield path, True
        for name in filenames:
            if name.endswith(".meta") or name.startswith("."):
                continue
            path = os.path.join(dirpath, name)
            if not os.path.exists(path + ".meta"):
                yield path, False


def main() -> int:
    check = "--check" in sys.argv
    missing = list(missing_metas())
    if check:
        for path, _ in missing:
            print(f"missing .meta: {os.path.relpath(path, ROOT)}")
        return 1 if missing else 0

    for path, is_dir in missing:
        with open(path + ".meta", "w", encoding="utf-8") as handle:
            handle.write(meta_content(path, is_dir))
        print(f"created {os.path.relpath(path, ROOT)}.meta")
    return 0


if __name__ == "__main__":
    sys.exit(main())
