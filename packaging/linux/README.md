# Linux packaging (Avalonia UI)

Git Extensions Next is shipped as self-contained builds of `GitExtensions.Avalonia` (plus the `GitExtensions.AskPass` helper): the .NET runtime is bundled, the only hard requirement besides the native libraries listed below is `git`. Invariant globalization is enabled, so there is no `libicu` dependency.

Packages are not built by hand: every release is produced by the [Release workflow](../../.github/workflows/release.yml) and published as assets of the [GitHub release](https://github.com/khunder/git-extensions-next/releases/latest). Download them from there (version 0.2.0 shown; builds exist for x86-64 and arm64):

| Asset | For |
| --- | --- |
| [`git-extensions-linux-0.2.0-x64.tar.gz`](https://github.com/khunder/git-extensions-next/releases/download/v0.2.0/git-extensions-linux-0.2.0-x64.tar.gz), `-arm64.tar.gz` | any distribution, user-level install |
| [`git-extensions_0.2.0_amd64.deb`](https://github.com/khunder/git-extensions-next/releases/download/v0.2.0/git-extensions_0.2.0_amd64.deb), `_arm64.deb` | Debian, Ubuntu and derivatives |
| [`git-extensions-0.2.0-1.fedora.x86_64.rpm`](https://github.com/khunder/git-extensions-next/releases/download/v0.2.0/git-extensions-0.2.0-1.fedora.x86_64.rpm), `.aarch64.rpm` | Fedora |
| [`git-extensions-0.2.0-1.opensuse.x86_64.rpm`](https://github.com/khunder/git-extensions-next/releases/download/v0.2.0/git-extensions-0.2.0-1.opensuse.x86_64.rpm), `.aarch64.rpm` | openSUSE (Tumbleweed, Leap) |
| [`git-extensions-0.2.0-1-x86_64.pkg.tar.zst`](https://github.com/khunder/git-extensions-next/releases/download/v0.2.0/git-extensions-0.2.0-1-x86_64.pkg.tar.zst) | Arch Linux and derivatives |
| [`git-extensions-0.2.0-x86_64.AppImage`](https://github.com/khunder/git-extensions-next/releases/download/v0.2.0/git-extensions-0.2.0-x86_64.AppImage), `-aarch64.AppImage` | any distribution, portable single file |
| [`SHA256SUMS`](https://github.com/khunder/git-extensions-next/releases/download/v0.2.0/SHA256SUMS) | checksums of all of the above |

Verify a download with `sha256sum -c SHA256SUMS --ignore-missing`.

## Installing

Debian / Ubuntu (resolves dependencies through apt):

```bash
sudo apt install ./git-extensions_0.2.0_amd64.deb
sudo apt remove git-extensions
```

Fedora:

```bash
sudo dnf install ./git-extensions-0.2.0-1.fedora.x86_64.rpm
sudo dnf remove git-extensions
```

openSUSE (the packages are not signed):

```bash
sudo zypper --no-gpg-checks install ./git-extensions-0.2.0-1.opensuse.x86_64.rpm
# newer zypper: sudo zypper install --allow-unsigned-rpm ./git-extensions-...rpm
sudo zypper remove git-extensions
```

Arch Linux:

```bash
sudo pacman -U git-extensions-0.2.0-1-x86_64.pkg.tar.zst
sudo pacman -R git-extensions
```

AppImage (portable, nothing is installed):

```bash
chmod +x git-extensions-0.2.0-x86_64.AppImage
./git-extensions-0.2.0-x86_64.AppImage [path-to-repository]
# without FUSE 2: ./git-extensions-0.2.0-x86_64.AppImage --appimage-extract-and-run
```

Tarball (nothing needs root; installs under `~/.local`):

```bash
tar xzf git-extensions-linux-0.2.0-x64.tar.gz && cd git-extensions
./install.sh                # ~/.local/opt/git-extensions, ~/.local/bin/git-extensions, desktop entry + icons
./install.sh --uninstall
```

All packages install the application under `/opt/git-extensions`, a launcher symlink `/usr/bin/git-extensions`, a desktop entry in `/usr/share/applications` and icons in the `hicolor` theme. Start it from the application menu or with `git-extensions [path-to-repository]`.

Runtime libraries the packages depend on: glibc, libstdc++, libgcc, zlib, OpenSSL 3, X11 (`libX11`, `libXcursor`, `libXi`, `libXrandr`, `libICE`, `libSM`), fontconfig, FreeType, libpng. `openssh-client` is recommended for SSH remotes.

## Building the packages

Every script takes its inputs from environment variables (`VERSION`, the published tree, ...) and writes into `artifacts/dist` (override with `OUT_DIR`). `packaging/linux/common.sh` holds the shared installed layout.

```bash
# 1. Publish a self-contained tree (cross publishing linux-arm64 from x64 works)
packaging/linux/publish.sh linux-x64 artifacts/publish/linux-x64
packaging/linux/publish.sh linux-arm64 artifacts/publish/linux-arm64

# 2. Packages from a published tree
VERSION=0.2.0 ARCH=x64 PUBLISH_DIR=artifacts/publish/linux-x64 packaging/linux/build-tarball.sh   # ARCH: x64 | arm64
VERSION=0.2.0 ARCH=amd64 PUBLISH_DIR=artifacts/publish/linux-x64 packaging/linux/build-deb.sh     # ARCH: amd64 | arm64
VERSION=0.2.0 ARCH=x86_64 DISTRO=fedora PUBLISH_DIR=artifacts/publish/linux-x64 packaging/linux/build-rpm.sh   # needs rpm-build; DISTRO only names the file
VERSION=0.2.0 ARCH=x86_64 TARBALL=artifacts/dist/git-extensions-linux-0.2.0-x64.tar.gz packaging/linux/build-arch.sh   # as a non-root user, needs makepkg

# 3. Check an installed package (files present, native libraries resolve)
packaging/linux/smoke-check.sh
```

- `build-deb.sh` uses `dpkg-deb` when present and otherwise assembles the archive with `ar` and `tar`.
- `git-extensions.spec` is one spec for Fedora and openSUSE: it switches package names on `%{suse_version}`, disables debuginfo, stripping and automatic dependency generation (the bundled runtime must not be scanned) and lists its `Requires` explicitly.
- `PKGBUILD` repackages the tarball (`source_x86_64` / `source_aarch64` point at the local file) and builds nothing; `makepkg` builds for the host architecture only.
- The CI that does all of this on a tag is `.github/workflows/release.yml`; it also installs every package in a matching container and runs `smoke-check.sh`.

Notes
- `GitExtensions.AskPass` (the `GIT_ASKPASS` / `SSH_ASKPASS` helper used for in-window credential prompts) is published next to the app and is self-contained too.
- The repository sets `IsPublishable=false` globally; the Avalonia project opts back in.
- The version (`0.2.0`) is set in `src/app/GitExtensions.Avalonia/GitExtensions.Avalonia.csproj` and shown in Help > About; pass the same value as `VERSION` to the scripts.
- Not covered yet: AppImage / Flatpak, package signing, macOS and Windows installers.
