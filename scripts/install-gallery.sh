#!/usr/bin/env sh
set -eu

repository="RazorConsole/RazorConsole"
install_root="${RAZORCONSOLE_GALLERY_INSTALL_DIR:-${HOME}/.local/share/razorconsole-gallery}"
bin_dir="${RAZORCONSOLE_GALLERY_BIN_DIR:-${HOME}/.local/bin}"
channel="stable"

while [ "$#" -gt 0 ]; do
  case "$1" in
    --channel)
      if [ "$#" -lt 2 ]; then
        echo "--channel requires stable or nightly." >&2
        exit 1
      fi
      channel="$2"
      shift 2
      ;;
    *)
      echo "Unknown option: $1" >&2
      exit 1
      ;;
  esac
done

case "$(uname -s)" in
  Darwin) platform="macos" ;;
  Linux) platform="linux" ;;
  *) echo "Unsupported operating system: $(uname -s)" >&2; exit 1 ;;
esac

case "$(uname -m)" in
  x86_64|amd64) architecture="x64" ;;
  arm64|aarch64) architecture="arm64" ;;
  *) echo "Unsupported architecture: $(uname -m)" >&2; exit 1 ;;
esac

case "$channel" in
  stable)
    release_url="$(curl -fsSL -o /dev/null -w '%{url_effective}' "https://github.com/${repository}/releases/latest")"
    tag="${release_url##*/}"
    ;;
  nightly)
    releases="$(curl -fsSL \
      -H "Accept: application/vnd.github+json" \
      -H "X-GitHub-Api-Version: 2022-11-28" \
      "https://api.github.com/repos/${repository}/releases?per_page=100")"
    tag="$(
      printf '%s' "$releases" |
        grep -Eo '"tag_name"[[:space:]]*:[[:space:]]*"nightly-[0-9]{8}-[0-9]{6}-[0-9a-f]{7}"' |
        head -n 1 |
        sed -E 's/.*"(nightly-[0-9]{8}-[0-9]{6}-[0-9a-f]{7})"/\1/'
    )"
    if [ -z "$tag" ]; then
      echo "No published nightly release was found." >&2
      exit 1
    fi
    ;;
  *)
    echo "Channel must be stable or nightly." >&2
    exit 1
    ;;
esac

version="${tag#v}"
archive="razorconsole-gallery-${version}-${platform}-${architecture}.tar.gz"
download_url="https://github.com/${repository}/releases/download/${tag}/${archive}"
checksums_url="https://github.com/${repository}/releases/download/${tag}/checksums-sha256.txt"
temporary_dir="$(mktemp -d)"
trap 'rm -rf "$temporary_dir"' EXIT HUP INT TERM

echo "Downloading RazorConsole Gallery ${version} (${channel}) for ${platform}-${architecture}..."
curl -fL "$download_url" -o "$temporary_dir/$archive"
curl -fL "$checksums_url" -o "$temporary_dir/checksums-sha256.txt"

expected="$(awk -v archive="$archive" '$2 == archive { print $1 }' "$temporary_dir/checksums-sha256.txt")"
if [ -z "$expected" ]; then
  echo "No checksum was published for $archive." >&2
  exit 1
fi

if command -v sha256sum >/dev/null 2>&1; then
  actual="$(sha256sum "$temporary_dir/$archive" | awk '{ print $1 }')"
else
  actual="$(shasum -a 256 "$temporary_dir/$archive" | awk '{ print $1 }')"
fi
if [ "$actual" != "$expected" ]; then
  echo "Checksum verification failed for $archive." >&2
  exit 1
fi

tar -xzf "$temporary_dir/$archive" -C "$temporary_dir"
extracted="$temporary_dir/razorconsole-gallery-${version}-${platform}-${architecture}"
mkdir -p "$install_root/Fonts" "$bin_dir"
cp "$extracted/razorconsole-gallery" "$install_root/razorconsole-gallery"
cp "$extracted/Fonts/Slant Relief.flf" "$install_root/Fonts/Slant Relief.flf"
chmod +x "$install_root/razorconsole-gallery"
ln -sf "$install_root/razorconsole-gallery" "$bin_dir/razorconsole-gallery"

echo "Installed razorconsole-gallery to $install_root."
case ":${PATH}:" in
  *":${bin_dir}:"*) ;;
  *) echo "Add $bin_dir to PATH, then run: razorconsole-gallery" ;;
esac
