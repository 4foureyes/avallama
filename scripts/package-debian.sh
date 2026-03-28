#!/usr/bin/env bash
set -euo pipefail

log_ts() { date -u +"%Y-%m-%dT%H:%M:%SZ"; }
log()    { printf '%s [INFO] %s\n' "$(log_ts)" "$*"; }

log "Starting Debian package script"

VERSION="${1:-}"
if [ -z "$VERSION" ]; then
  echo "Usage: $0 <version>"
  exit 1
fi

PROJECT="avallama/avallama.csproj"

log "Running dotnet publish"
dotnet publish "${PROJECT}" \
  --verbosity normal \
  --nologo \
  --configuration Release \
  --self-contained true \
  --runtime linux-x64 \
  --output "./out/linux-x64"

log "Preparing staging directory"
rm -rf staging_folder
mkdir -p staging_folder/DEBIAN
mkdir -p staging_folder/usr/{bin,lib/avallama,share/{applications,pixmaps,icons/hicolor,doc/avallama}}

log "Creating control file"
cat > ./staging_folder/DEBIAN/control <<EOF
Package: avallama
Version: ${VERSION}
Section: devel
Priority: optional
Architecture: amd64
Depends: libc6 (>= 2.34), libicu70 | libicu72 | libicu74 | libicu76, libfontconfig1, libfreetype6, libx11-6, libxrender1, libxcb1, libgl1, libpng16-16
Maintainer: Márk Csörgő <mcsorgo@proton.me>
Homepage: https://github.com/4foureyes/avallama
Description: User-friendly GUI for Ollama
 A cross-platform Avalonia-based GUI for running local AI models through
 Ollama. Designed for simplicity and performance.
EOF

log "Creating copyright file"
cat > ./staging_folder/usr/share/doc/avallama/copyright <<EOF
Format: https://www.debian.org/doc/packaging-manuals/copyright-format/1.0/
Upstream-Name: Avallama
Source: https://github.com/4foureyes/avallama

Files: avallama avallama.dll avallama.pdb avallama.deps.json avallama.runtimeconfig.json Assets/* hu/*
Copyright: Márk Csörgő <mcsorgo@proton.me>
           Martin Bartos <bmartin217@proton.me>
License: MIT

Files: Avalonia.*.dll
Copyright: AvaloniaUI OÜ
License: MIT

Files: CommunityToolkit.Mvvm.dll
Copyright: .NET Foundation and Contributors
License: MIT

Files: HtmlAgilityPack.dll
Copyright: ZZZ Projects Inc.
License: MIT

Files: Svg.Controls.Avalonia.dll
       Svg.Custom.dll
       Svg.Model.dll
Copyright: Wiesław Šoltés
License: MIT

Files: SkiaSharp.dll
       HarfBuzzSharp.dll
       ShimSkiaSharp.dll
       libSkiaSharp.so
       libHarfBuzzSharp.so
Copyright: Xamarin, Inc.
           Microsoft Corporation
License: MIT

Files: ExCSS.dll
Copyright: Tyler Brinks
License: MIT

Files: SQLitePCLRaw.*.dll
       libe_sqlite3.so
Copyright: Eric Sink
License: Apache-2.0

Files: System.*.dll
       Microsoft.*.dll
       mscorlib.dll
       netstandard.dll
       libcoreclr*.so
       libclr*.so
       libhostfxr.so
       libhostpolicy.so
       libmscordaccore.so
       libmscordbi.so
       libSystem.*.so
       createdump
Copyright: Microsoft Corporation and .NET Foundation
License: MIT

Files: Tmds.DBus.Protocol.dll
Copyright: Alp Toker <alp@toker.com>
           Tom Deseyn <tom.deseyn@gmail.com>
License: MIT

Files: MicroCom.Runtime.dll
Copyright: Nikita Tsukanov
License: MIT

License: MIT
 Permission is hereby granted, free of charge, to any person obtaining a copy
 of this software and associated documentation files (the "Software"), to deal
 in the Software without restriction, including without limitation the rights
 to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 copies of the Software, and to permit persons to whom the Software is
 furnished to do so, subject to the following conditions:
 .
 The above copyright notice and this permission notice shall be included in all
 copies or substantial portions of the Software.
 .
 THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 SOFTWARE.

License: Apache-2.0
 Licensed under the Apache License, Version 2.0 (the "License");
 you may not use this file except in compliance with the License.
 You may obtain a copy of the License at
 .
     http://www.apache.org/licenses/LICENSE-2.0
 .
 Unless required by applicable law or agreed to in writing, software
 distributed under the License is distributed on an "AS IS" BASIS,
 WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 See the License for the specific language governing permissions and
 limitations under the License.
 .
 On Debian systems, the full text of the Apache License version 2.0
 can be found in the file '/usr/share/common-licenses/Apache-2.0'.
EOF

# Starter script
log "Copying starter script"
cp scripts/debian/avallama.sh ./staging_folder/usr/bin/avallama

# Application files
log "Copying application files"
cp -a ./out/linux-x64/. ./staging_folder/usr/lib/avallama/

# Fix permissions: no world-writable or executable .dll/.so
log "Setting permissions"
find ./staging_folder/usr/lib/avallama -type d -exec chmod 755 {} +
find ./staging_folder/usr/lib/avallama -type f -exec chmod 644 {} +

# Strip unneeded symbols from binaries
log "Stripping binaries"
find ./staging_folder/usr/lib/avallama -type f -name "*.so" -exec strip --strip-unneeded {} + || true
strip --strip-unneeded ./staging_folder/usr/lib/avallama/avallama || true

# Desktop entry and icons
log "Copying desktop entry and icons"
cp scripts/debian/Avallama.desktop ./staging_folder/usr/share/applications/
cp scripts/debian/pixmaps/avallama.png ./staging_folder/usr/share/pixmaps/
cp -a scripts/debian/icons/hicolor/. ./staging_folder/usr/share/icons/hicolor/

# Normalize permissions
log "Normalizing permissions"
find ./staging_folder -type d -exec chmod 755 {} +
find ./staging_folder -type f -exec chmod 644 {} +
chmod 755 ./staging_folder/usr/bin/avallama
chmod 755 ./staging_folder/usr/lib/avallama/avallama

# Build .deb package
log "Building .deb package via dpkg-deb"
dpkg-deb --root-owner-group --build ./staging_folder ./avallama_"${VERSION}"_amd64.deb

log "Debian package created: avallama_${VERSION}_amd64.deb"
