#!/usr/bin/env bash
# Baut das Aion 2 G13 Plugin als eine einzelne .exe und packt sie als .zip.
#   ./build.sh          kleine .exe (braucht die .NET-10-Desktop-Laufzeit)
#   ./build.sh full     grosse .exe, laeuft ohne installierte Laufzeit
set -euo pipefail
cd "$(dirname "$0")"
DOTNET="/mnt/c/Program Files/dotnet/dotnet.exe"
VERSION=$(grep -oP '(?<=<Version>)[^<]+' Aion2G13Plugin.csproj)
OUT_WIN='C:\Users\Public\aion2g13plugin-release'
OUT_LIN=/mnt/c/Users/Public/aion2g13plugin-release
if [ "${1:-}" = "full" ]; then
  EXTRA="--self-contained true -p:EnableCompressionInSingleFile=true"; NAME="Aion2G13Plugin-$VERSION-full"
else
  EXTRA="--self-contained false"; NAME="Aion2G13Plugin-$VERSION"
fi
rm -rf "$OUT_LIN"
"$DOTNET" publish Aion2G13Plugin.csproj -c Release -r win-x64 $EXTRA -p:PublishSingleFile=true -o "$OUT_WIN"
cd "$OUT_LIN" && rm -f ./*.pdb && python3 -I -c "import zipfile,sys; z=zipfile.ZipFile(sys.argv[1],'w',zipfile.ZIP_DEFLATED); z.write('Aion2G13Plugin.exe'); z.close()" "../$NAME.zip"
echo "Fertig: C:\\Users\\Public\\$NAME.zip"
