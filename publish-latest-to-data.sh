#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="${SCRIPT_DIR}"
SOLUTION_PATH="${REPO_ROOT}/LaserEnergyMonitor.sln"
APP_OUTPUT_DIR="${REPO_ROOT}/src/LaserEnergyMonitor.Wpf/bin/x86/Release/net48"
SHARED_ROOT="/mnt/data/Builds/LaserEnergyMonitor"
LATEST_DIR="${SHARED_ROOT}/latest"
BUILD_STAMP_FILE="${LATEST_DIR}/build-info.txt"
TIMESTAMP="$(date +%Y%m%d-%H%M%S)"

if ! findmnt /mnt/data > /dev/null 2>&1; then
  echo "Shared Data partition is not mounted at /mnt/data."
  echo "Mount it first, for example:"
  echo "  sudo mount -t ntfs3 -o force /dev/nvme0n1p4 /mnt/data"
  exit 1
fi

if [[ ! -f "${SOLUTION_PATH}" ]]; then
  echo "Solution not found at ${SOLUTION_PATH}"
  exit 1
fi

echo "Building Release configuration..."
env DOTNET_CLI_HOME=/tmp dotnet build "${SOLUTION_PATH}" -c Release -v m

if [[ ! -d "${APP_OUTPUT_DIR}" ]]; then
  echo "Build output folder not found: ${APP_OUTPUT_DIR}"
  exit 1
fi

echo "Refreshing latest artifact folder: ${LATEST_DIR}"
mkdir -p "${LATEST_DIR}"
find "${LATEST_DIR}" -mindepth 1 -maxdepth 1 ! -name '.' -exec rm -rf -- {} +

echo "Copying build output to latest..."
cp -a "${APP_OUTPUT_DIR}/." "${LATEST_DIR}/"

cat > "${BUILD_STAMP_FILE}" <<EOF
Build timestamp: ${TIMESTAMP}
Source output: ${APP_OUTPUT_DIR}
Shared folder: ${LATEST_DIR}
EOF

echo "Done."
echo "Latest build artifacts copied to:"
echo "  ${LATEST_DIR}"
