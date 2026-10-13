#!/usr/bin/env python3
"""Fail if any tracked file looks like game data or another binary."""
import pathlib
import subprocess
import sys

ASSET_EXTENSIONS = {
    '.ipa', '.apk', '.tbl', '.bin', '.phy', '.bdae', '.trn', '.itm', '.beff', '.pak',
    '.pvr', '.tga', '.dds', '.ktx', '.pkm', '.astc', '.png', '.jpg', '.jpeg', '.gif',
    '.bmp', '.webp', '.ogg', '.wav', '.mp3', '.m4a', '.aac', '.caf', '.mp4', '.webm',
    '.zip', '.gz', '.7z', '.tar', '.rar', '.xz', '.pcap', '.pcapng', '.har', '.db',
    '.sqlite', '.glb', '.gltf', '.obj', '.fbx', '.ttf', '.otf', '.woff', '.woff2',
    '.graphml', '.sql', '.csv', '.frm', '.myd', '.myi', '.strings', '.nib', '.mov',
    '.m4v', '.bar',
}

MAX_JSON_BYTES = 64 * 1024

ALLOWED: dict[str, str] = {}


def main() -> int:
    files = subprocess.check_output(['git', 'ls-files', '-z'], text=True).split('\0')
    bad = []
    for name in filter(None, files):
        if name in ALLOWED:
            continue
        path = pathlib.Path(name)
        if path.suffix.lower() in ASSET_EXTENSIONS:
            bad.append(f'{name}: asset or archive extension')
            continue
        if path.suffix.lower() == '.json' and path.is_file() and path.stat().st_size > MAX_JSON_BYTES:
            bad.append(f'{name}: JSON over {MAX_JSON_BYTES // 1024} KiB')
            continue
        if path.is_file() and b'\0' in path.read_bytes()[:8192]:
            bad.append(f'{name}: binary content')
    for line in bad:
        print(line, file=sys.stderr)
    print(f'{len(files) - 1} tracked files checked, {len(bad)} refused', file=sys.stderr)
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main())
