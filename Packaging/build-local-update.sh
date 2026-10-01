#!/usr/bin/env bash
# Builds a LOCAL Velopack update package for testing on the developer's own PC (WSL + Windows
# dotnet/vpk), checks it, and puts only the Setup.exe on the Desktop.
#
#   Packaging/build-local-update.sh 0.9.2        # 0.9.2 = the NEXT release number
#
# The package version is "<next>-local.<unix time>": a semver pre-release, so the real release of
# that number is always newer and the installed copy updates to it by itself. The next number must
# be above the last published release, or the local copy would shadow it.
# Install the result as Administrator (Velopack's install hook needs elevation).
set -euo pipefail

NEXT="${1:?usage: $0 <next release number, e.g. 0.9.2>}"
REPO="$(cd "$(dirname "$0")/.." && pwd)"
WIN_ROOT="/mnt/c/Users/ralf/aiondps-localbuild"
WIN_B='C:\Users\ralf\aiondps-localbuild'
DOTNET="/mnt/c/Program Files/dotnet/dotnet.exe"
VPK="/mnt/c/Users/ralf/.dotnet/tools/vpk.exe"
VERSION="${NEXT}-local.$(date +%s)"

mkdir -p "$WIN_ROOT"
cp "$REPO/Client/assets/app/aiondps.ico" "$WIN_ROOT/aiondps.ico"
rm -rf "$WIN_ROOT/releases" "$WIN_ROOT/publish"

echo "== publish ($VERSION)"
(cd "$REPO" && "$DOTNET" publish Client/AionDPS.csproj -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=false -o "$WIN_B\\publish" 2>&1 | grep -E "error|Fehler" || true)
[ -f "$WIN_ROOT/publish/Aion DPS.exe" ] || { echo "FAIL: publish produced no Aion DPS.exe"; exit 1; }

echo "== pack"
"$VPK" pack --packId AionDpsMeter --packTitle "Aion DPS Meter" --packAuthors SkeeveAN \
  --packVersion "$VERSION" --packDir "$WIN_B\\publish" --mainExe "Aion DPS.exe" \
  --icon "$WIN_B\\aiondps.ico" --channel win --outputDir "$WIN_B\\releases" 2>&1 | tail -5

echo "== checks"
SETUP="$WIN_ROOT/releases/AionDpsMeter-win-Setup.exe"
[ -f "$SETUP" ] || { echo "FAIL: no Setup.exe"; exit 1; }
[ -f "$WIN_ROOT/releases/AionDpsMeter-$VERSION-full.nupkg" ] || { echo "FAIL: no full nupkg for $VERSION"; exit 1; }
grep -q "$VERSION" "$WIN_ROOT/releases/releases.win.json" || { echo "FAIL: version missing in releases.win.json"; exit 1; }

# The Setup must carry the app's icon (a missing icon means vpk was given a bad --icon).
cat > "$WIN_ROOT/iconcheck.ps1" <<'PS'
param($a, $b)
Add-Type -AssemblyName System.Drawing
function IconHash($p) {
  $i = [System.Drawing.Icon]::ExtractAssociatedIcon($p); $m = New-Object IO.MemoryStream
  $i.ToBitmap().Save($m, [Drawing.Imaging.ImageFormat]::Png)
  [BitConverter]::ToString((New-Object Security.Cryptography.SHA1Managed).ComputeHash($m.ToArray()))
}
if ((IconHash $a) -eq (IconHash $b)) { 'ICON_OK' } else { 'ICON_MISMATCH' }
PS
RESULT="$(powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$WIN_B\\iconcheck.ps1" "$WIN_B\\releases\\AionDpsMeter-win-Setup.exe" "$WIN_B\\publish\\Aion DPS.exe" | tr -d '\r')"
[ "$RESULT" = "ICON_OK" ] || { echo "FAIL: Setup.exe does not carry the app icon ($RESULT)"; exit 1; }

cp "$SETUP" /mnt/c/Users/ralf/Desktop/AionDPS-Update-lokal.exe
echo "OK: $VERSION -> Desktop/AionDPS-Update-lokal.exe ($(stat -c %s "$SETUP") bytes), icon ok, version in feed"
