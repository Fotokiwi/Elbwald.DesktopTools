#!/usr/bin/env bash
set -euo pipefail

VERSION="0.1.0-alpha.1"
CONFIGURATION="Release"

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "${SCRIPT_DIR}/.." && pwd)"

SOLUTION="${REPO_ROOT}/Elbwald.DesktopTools.sln"
APP_PROJECT="${REPO_ROOT}/src/Elbwald.DesktopTools.App/Elbwald.DesktopTools.App.csproj"

ARTIFACT_ROOT="${REPO_ROOT}/artifacts/alpha/${VERSION}"
PUBLISH_ROOT="${ARTIFACT_ROOT}/publish"

LINUX_DIR="${PUBLISH_ROOT}/linux-x64"
WINDOWS_DIR="${PUBLISH_ROOT}/win-x64"

echo "== Elbwald Digital – Desktop Tools ${VERSION} =="
echo "Repository: ${REPO_ROOT}"
echo

rm -rf "${ARTIFACT_ROOT}"
mkdir -p "${PUBLISH_ROOT}"

echo "== Clean =="
dotnet clean "${SOLUTION}" -c "${CONFIGURATION}"

echo
echo "== Restore =="
dotnet restore "${SOLUTION}"

echo
echo "== Tests =="
dotnet test "${SOLUTION}" \
    -c "${CONFIGURATION}" \
    --no-restore

publish_runtime() {
    local rid="$1"
    local output="$2"

    echo
    echo "== Publish ${rid} =="
    dotnet publish "${APP_PROJECT}" \
        -c "${CONFIGURATION}" \
        -r "${rid}" \
        --self-contained true \
        -o "${output}"

    test -f "${output}/Modules/PhotoSort/module.json"
    test -f "${output}/Modules/PhotoSort/Elbwald.DesktopTools.PhotoSort.dll"
    test -f "${output}/Modules/MediaAnalyzer/module.json"
    test -f "${output}/Modules/MediaAnalyzer/Elbwald.DesktopTools.MediaAnalyzer.dll"

    cat > "${output}/BUILD-INFO.txt" <<EOF
Elbwald Digital – Desktop Tools
Version: ${VERSION}
Configuration: ${CONFIGURATION}
Runtime: ${rid}
Self-contained: yes
Single-file: no
Trimmed: no

Original media files are not part of this build directory.
Application caches and recovery data remain in the operating system's local app-data location.
EOF
}

publish_runtime "linux-x64" "${LINUX_DIR}"
publish_runtime "win-x64" "${WINDOWS_DIR}"

test -f "${LINUX_DIR}/Elbwald.DesktopTools.App"
test -f "${WINDOWS_DIR}/Elbwald.DesktopTools.App.exe"

echo
echo "== Archive Linux =="
tar -czf \
    "${ARTIFACT_ROOT}/Elbwald.DesktopTools-${VERSION}-linux-x64.tar.gz" \
    -C "${LINUX_DIR}" .

echo
if command -v zip >/dev/null 2>&1; then
    echo "== Archive Windows ZIP =="
    (
        cd "${WINDOWS_DIR}"
        zip -qr \
            "${ARTIFACT_ROOT}/Elbwald.DesktopTools-${VERSION}-win-x64.zip" \
            .
    )
else
    echo "Hinweis: 'zip' wurde nicht gefunden."
    echo "Windows-Publish liegt fertig als Verzeichnis vor; zusätzlich wird ein tar.gz erzeugt."
    tar -czf \
        "${ARTIFACT_ROOT}/Elbwald.DesktopTools-${VERSION}-win-x64.tar.gz" \
        -C "${WINDOWS_DIR}" .
fi

echo
echo "Alpha-Build fertig:"
echo "  ${ARTIFACT_ROOT}"
