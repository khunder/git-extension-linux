# One spec for Fedora (and RHEL-like) and openSUSE. It packages an already published, self-contained tree that
# build-rpm.sh stages first, so nothing is compiled here.
#
#   rpmbuild -bb --target x86_64 --define "ge_version 0.7.1" --define "ge_stage /path/to/stage" git-extensions.spec

%{!?ge_version: %global ge_version 0.7.1}
%{!?ge_release: %global ge_release 1}
%{!?ge_stage: %{error:define ge_stage (output of stage_root)}}

# The payload is a bundled .NET runtime plus prebuilt native libraries: no debug packages, no stripping, no
# byte-compilation, no automatic dependency or provides generation (the bundled libs must not be provided to other packages).
%global debug_package %{nil}
%global _build_id_links none
%global __strip /bin/true
%global __os_install_post %{nil}
%global __requires_exclude_from ^/opt/git-extensions/.*$
%global __provides_exclude_from ^/opt/git-extensions/.*$

Name:           git-extensions
Version:        %{ge_version}
Release:        %{ge_release}
Summary:        Graphical Git client (Git Extensions Next)
License:        GPL-3.0-or-later
URL:            https://github.com/khunder/git-extensions-next
AutoReqProv:    no
ExclusiveArch:  x86_64 aarch64

Requires:       git
%if 0%{?suse_version}
Requires:       libX11-6
Requires:       libXcursor1
Requires:       libXi6
Requires:       libXrandr2
Requires:       libICE6
Requires:       libSM6
Requires:       libfontconfig1
Requires:       libfreetype6
Requires:       libpng16-16
Requires:       libopenssl3
Requires:       libz1
Requires:       libstdc++6
Requires:       libgcc_s1
Recommends:     openssh-clients
%else
Requires:       libX11
Requires:       libXcursor
Requires:       libXi
Requires:       libXrandr
Requires:       libICE
Requires:       libSM
Requires:       fontconfig
Requires:       freetype
Requires:       libpng
Requires:       openssl-libs
Requires:       zlib
Requires:       libstdc++
Requires:       libgcc
Recommends:     openssh-clients
%endif

%description
Git Extensions is a graphical user interface for Git: browse history, stage changes, commit, manage branches,
remotes and stashes. This package bundles the .NET runtime, so nothing else needs to be installed besides Git.

%prep
# Nothing to unpack: the tree is staged by build-rpm.sh.

%build
# Nothing to build.

%install
rm -rf %{buildroot}
mkdir -p %{buildroot}
cp -a %{ge_stage}/. %{buildroot}/

%post
/usr/bin/update-desktop-database -q /usr/share/applications >/dev/null 2>&1 || :
/usr/bin/gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor >/dev/null 2>&1 || :

%postun
/usr/bin/update-desktop-database -q /usr/share/applications >/dev/null 2>&1 || :
/usr/bin/gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor >/dev/null 2>&1 || :

%files
%license /usr/share/doc/git-extensions/LICENSE.md
/opt/git-extensions
/usr/bin/git-extensions
/usr/share/applications/git-extensions.desktop
/usr/share/icons/hicolor/*/apps/git-extensions.*

%changelog
* Sat Nov 14 2026 Git Extensions Next <noreply@github.com> - 0.1.0-1
- First release of the Linux build with the Avalonia user interface

* Sat Nov 14 2026 Git Extensions Next <noreply@github.com> - 0.2.0-1
- Platform upgrade to .NET 11 and Avalonia 12

* Wed Nov 18 2026 Git Extensions Next <noreply@github.com> - 0.7.0-1
- Windows builds (installer and zip), renamed to Git Extensions Next, documentation site

* Wed Nov 18 2026 Git Extensions Next <noreply@github.com> - 0.7.1-1
- Release workflow: retry the openSUSE package repository refresh
