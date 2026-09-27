### Interactive Component Gallery

Explore every RazorConsole component in a live playground. The Gallery is distributed as a Native AOT application and does not require the .NET SDK or runtime.

#### macOS and Linux

```shell
curl -fsSL https://raw.githubusercontent.com/RazorConsole/RazorConsole/main/scripts/install-gallery.sh | sh
```

The installer detects the operating system and CPU architecture, verifies the SHA-256 checksum, and installs `razorconsole-gallery` under `~/.local`. If `~/.local/bin` is not already on `PATH`, the installer prints the command needed to add it.

#### Windows

Run the following command in PowerShell:

```shell
irm https://raw.githubusercontent.com/RazorConsole/RazorConsole/main/scripts/install-gallery.ps1 | iex
```

The installer selects the x64 or Arm64 package, verifies its SHA-256 checksum, installs it under `%LOCALAPPDATA%\RazorConsole\Gallery`, and adds that directory to the user `PATH`.

#### Nightly channel

Each successful `main` build is published as a uniquely versioned prerelease. Install the newest nightly build on macOS or Linux with:

```shell
curl -fsSL https://raw.githubusercontent.com/RazorConsole/RazorConsole/main/scripts/install-gallery.sh | sh -s -- --channel nightly
```

On Windows PowerShell:

```shell
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/RazorConsole/RazorConsole/main/scripts/install-gallery.ps1))) -Channel Nightly
```

Nightly builds are unstable and replace an existing Gallery installation. Run the stable installer again to switch back. The installer selects the newest published `nightly-*` prerelease; draft or partially uploaded releases are never selected.

Open a new terminal after installation and run:

```shell
razorconsole-gallery
```

#### Manual installation

Download the archive for your operating system and architecture from the [latest GitHub Release](https://github.com/RazorConsole/RazorConsole/releases/latest):

| Operating system | Architectures | Archive |
| --- | --- | --- |
| Windows | x64, Arm64 | `.zip` |
| Linux | x64, Arm64 | `.tar.gz` |
| macOS | Intel x64, Apple Silicon Arm64 | `.tar.gz` |

Extract the complete directory, including `Fonts/`, and run `razorconsole-gallery`. The release also contains `checksums-sha256.txt` for verifying downloads.

The macOS archives are not yet signed or notarized. A browser download might therefore be stopped by Gatekeeper. After verifying the checksum, open **System Settings → Privacy & Security** to approve the application. The shell installer uses `curl`, which normally does not add the browser quarantine attribute.

The gallery lets you experiment with layouts, inputs, and utilities before pulling them into your own app. For build details, see [Native Ahead-of-Time Compilation](/docs/native-aot).
