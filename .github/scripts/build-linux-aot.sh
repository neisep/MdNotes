#!/usr/bin/env bash
# Builds the Native AOT Linux release and packs it as <out>/mdnotes-linux-x86_64.tar.gz.
# Usage: build-linux-aot.sh <out-dir> [max-glibc]   (max-glibc defaults to 2.35)
#
# Runs inside an ubuntu:22.04 container in CI. An AOT binary needs at least the glibc it was linked
# against, so the build machine's glibc is the oldest one MdNotes will run on: 2.35 means Ubuntu 22.04,
# Debian 12, Fedora 36 or newer. The check at the end fails the build if that floor ever creeps up.
set -euo pipefail

out="${1:?usage: build-linux-aot.sh <out-dir> [max-glibc]}"
max_glibc="${2:-2.35}"
publish="$out/publish"
pkg="$out/mdnotes-linux-x86_64"

dotnet publish MdNotes -c Release -r linux-x64 -p:PublishAot=true -o "$publish"

# The executable plus the two native libraries it loads; the .dbg symbols stay out of the download.
rm -rf "$pkg" && mkdir -p "$pkg"
cp "$publish/MdNotes" "$publish/libSkiaSharp.so" "$publish/libHarfBuzzSharp.so" "$pkg/"
tar -czf "$out/mdnotes-linux-x86_64.tar.gz" -C "$out" mdnotes-linux-x86_64

needed="$(objdump -T "$pkg"/MdNotes "$pkg"/*.so | grep -oE 'GLIBC_[0-9]+\.[0-9]+' | sed 's/GLIBC_//' | sort -V | tail -1)"
echo "Requires glibc $needed (allowed: $max_glibc)"
if [[ "$(printf '%s\n%s\n' "$needed" "$max_glibc" | sort -V | tail -1)" != "$max_glibc" ]]; then
  echo "error: the build needs glibc $needed, newer than the supported $max_glibc" >&2
  exit 1
fi
