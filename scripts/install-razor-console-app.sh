#!/usr/bin/env sh
set -eu

repository="RazorConsole/RazorConsole"
app=""
channel="stable"

usage() {
  echo "Usage: install-razor-console-app.sh --app <Gallery|Snake|TankBattle> [--channel <stable|nightly>]" >&2
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    --app)
      if [ "$#" -lt 2 ]; then
        echo "--app requires Gallery, Snake, or TankBattle." >&2
        exit 1
      fi
      app="$2"
      shift 2
      ;;
    --channel)
      if [ "$#" -lt 2 ]; then
        echo "--channel requires stable or nightly." >&2
        exit 1
      fi
      channel="$2"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      usage
      exit 1
      ;;
  esac
done

case "$app" in
  Gallery|gallery)
    display_name="Component Gallery"
    command_name="razorconsole-gallery"
    install_root="${RAZORCONSOLE_GALLERY_INSTALL_DIR:-${HOME}/.local/share/razorconsole-gallery}"
    bin_dir="${RAZORCONSOLE_GALLERY_BIN_DIR:-${RAZORCONSOLE_BIN_DIR:-${HOME}/.local/bin}}"
    ;;
  Snake|snake)
    display_name="Snake"
    command_name="razorconsole-snake"
    install_root="${RAZORCONSOLE_SNAKE_INSTALL_DIR:-${HOME}/.local/share/razorconsole-snake}"
    bin_dir="${RAZORCONSOLE_SNAKE_BIN_DIR:-${RAZORCONSOLE_BIN_DIR:-${HOME}/.local/bin}}"
    ;;
  TankBattle|tankbattle|tank-battle)
    display_name="Tank Battle"
    command_name="razorconsole-tank-battle"
    install_root="${RAZORCONSOLE_TANK_BATTLE_INSTALL_DIR:-${HOME}/.local/share/razorconsole-tank-battle}"
    bin_dir="${RAZORCONSOLE_TANK_BATTLE_BIN_DIR:-${RAZORCONSOLE_BIN_DIR:-${HOME}/.local/bin}}"
    ;;
  "")
    echo "--app is required." >&2
    usage
    exit 1
    ;;
  *)
    echo "Unsupported app: $app" >&2
    usage
    exit 1
    ;;
esac

case "$channel" in
  Stable|stable) channel="stable" ;;
  Nightly|nightly) channel="nightly" ;;
  *)
    echo "Channel must be stable or nightly." >&2
    exit 1
    ;;
esac

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
esac

version="${tag#v}"
archive="${command_name}-${version}-${platform}-${architecture}.tar.gz"
download_url="https://github.com/${repository}/releases/download/${tag}/${archive}"
checksums_url="https://github.com/${repository}/releases/download/${tag}/checksums-sha256.txt"
temporary_dir="$(mktemp -d)"
trap 'rm -rf "$temporary_dir"' EXIT HUP INT TERM

echo "Downloading ${display_name} ${version} (${channel}) for ${platform}-${architecture}..."
curl -fL "$download_url" -o "$temporary_dir/$archive"
curl -fL "$checksums_url" -o "$temporary_dir/checksums-sha256.txt"

expected="$(
  awk -v archive="$archive" '
    {
      name = $2
      sub(/^\*/, "", name)
      sub(/^\.\//, "", name)
      if (name == archive) {
        print $1
      }
    }
  ' "$temporary_dir/checksums-sha256.txt"
)"
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
extracted="$temporary_dir/${command_name}-${version}-${platform}-${architecture}"
mkdir -p "$install_root" "$bin_dir"
cp -R "$extracted/." "$install_root/"
chmod +x "$install_root/$command_name"
ln -sf "$install_root/$command_name" "$bin_dir/$command_name"

echo "Installed $command_name ${version} (${channel}) to $install_root."
case ":${PATH}:" in
  *":${bin_dir}:"*) echo "Run: $command_name" ;;
  *) echo "Add $bin_dir to PATH, then run: $command_name" ;;
esac
