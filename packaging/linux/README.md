# Linux packaging (Avalonia UI)

Builds a self-contained tarball of `GitExtensions.Avalonia` — no .NET runtime is needed on the target machine.

```bash
packaging/linux/build-tarball.sh                 # linux-x64  -> artifacts/publish/git-extensions-avalonia-linux-x64.tar.gz
RID=linux-arm64 packaging/linux/build-tarball.sh # other runtime identifiers work the same way
```

Install from the extracted tarball (user-level, nothing needs root):

```bash
tar xzf git-extensions-avalonia-linux-x64.tar.gz && cd git-extensions
./install.sh                # ~/.local/opt/git-extensions, ~/.local/bin/git-extensions, desktop entry + icon
./install.sh --uninstall
```

Notes
- `GitExtensions.AskPass` (the `GIT_ASKPASS` / `SSH_ASKPASS` helper used for in-window credential prompts) is published next to the app; it is self-contained too.
- The repository sets `IsPublishable=false` globally; the Avalonia project opts back in.
- `git-extensions.svg` has not been rendered by a tool in CI; check it when changing it.
- Not covered yet: AppImage / Flatpak / .deb packaging, arm64 testing, macOS and Windows installers.
