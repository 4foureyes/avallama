#!/usr/bin/env bash
set -euo pipefail

log_ts() { date -u +"%Y-%m-%dT%H:%M:%SZ"; }
log()    { printf '%s [INFO] %s\n' "$(log_ts)" "$*"; }

log "Starting Arch package script"

VERSION="${1:-}"
if [ -z "$VERSION" ]; then
  echo "Usage: $0 <version>"
  exit 1
fi

PROJECT="./avallama/avallama.csproj"

rm -rf src pkg
mkdir -p src

log "Running dotnet publish"
dotnet publish "${PROJECT}" \
  --verbosity normal \
  --nologo \
  --configuration Release \
  --self-contained true \
  --runtime linux-x64 \
  --output "./src/avallama"

log "Creating desktop entry"
cat > src/avallama.desktop <<EOF
[Desktop Entry]
Name=Avallama
Comment=User-friendly GUI for Ollama
Icon=avallama
Exec=avallama
Terminal=false
Type=Application
Categories=Utility
GenericName=Avallama
Keywords=ollama; gui; avallama; artificial; intelligence
EOF

log "Copying icon"
cp scripts/debian/pixmaps/avallama.png src/

log "Creating PKGBUILD"
cat > PKGBUILD <<EOF
# Maintainer: Márk Csörgő, Martin Bartos (4foureyes)
pkgname=avallama
pkgver=${VERSION}
pkgrel=1
arch=('x86_64')
pkgdesc="User-friendly GUI for Ollama"
url="https://www.github.com/4foureyes/avallama"
license=('MIT')
depends=('icu' 'fontconfig' 'freetype2' 'libx11' 'libxrender' 'libxcb' 'mesa' 'libpng' 'zlib')
source=()
sha256sums=()
options=(!debug)

package() {
    mkdir -p "\${pkgdir}/opt/avallama"
    cp -r "\${srcdir}/../src/avallama/"* "\${pkgdir}/opt/avallama/"
    chmod +x "\${pkgdir}/opt/avallama/avallama"

    mkdir -p "\${pkgdir}/usr/bin"
    ln -s /opt/avallama/avallama "\${pkgdir}/usr/bin/avallama"

    install -Dm644 "\${srcdir}/../src/avallama.desktop" "\${pkgdir}/usr/share/applications/avallama.desktop"
    install -Dm644 "\${srcdir}/../src/avallama.png" "\${pkgdir}/usr/share/icons/hicolor/512x512/apps/avallama.png"
}
EOF

log "Building package via makepkg"
makepkg -fs --noconfirm

log "Arch package created: avallama-${VERSION}-1-x86_64.pkg.tar.zst"
