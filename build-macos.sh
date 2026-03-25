#!/usr/bin/env bash
# ============================================================
# encryptTools — macOS 打包
# - 主程序: EncryptTools.Desktop（Avalonia 图形界面）
# - 附加: EncryptTools.GcmCli（命令行）
# - 默认按当前 CPU 选择 osx-arm64 / osx-x64
# - 输出（单架构）:
#     dist/encryptTools_desktop_macos/
#     dist/encryptTools_gcm_macos/
# - BUILD_ALL=1 时同时打 arm64 与 x64，输出到 *_osx-arm64 / *_osx-x64
# - 需本机已安装 .NET 8 运行时，或使用 SELF_CONTAINED=1 自包含
#
# 可选环境变量:
#   RID=osx-arm64|osx-x64
#   BUILD_ALL=1
#   SELF_CONTAINED=1
# ============================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

CONFIG="${CONFIG:-Release}"
DIST="${SCRIPT_DIR}/dist"
DESKTOP_PROJ="${SCRIPT_DIR}/EncryptTools.Desktop/EncryptTools.Desktop.csproj"
GCM_PROJ="${SCRIPT_DIR}/EncryptTools.GcmCli/EncryptTools.GcmCli.csproj"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "[encryptTools] ERROR: dotnet SDK not found. Install .NET 8 SDK: https://dotnet.microsoft.com/download"
  exit 1
fi

DOTNET_MAJOR="$(dotnet --version 2>/dev/null | cut -d. -f1 || true)"
echo "[encryptTools] Using .NET SDK ${DOTNET_MAJOR:-?}"

SELF_CONTAINED="${SELF_CONTAINED:-false}"
if [[ "${SELF_CONTAINED:-}" == "1" ]] || [[ "${SELF_CONTAINED:-}" == "true" ]]; then
  SELF_CONTAINED="true"
else
  SELF_CONTAINED="false"
fi

detect_rid() {
  local m
  m="$(uname -m 2>/dev/null || true)"
  case "${m}" in
    arm64) echo "osx-arm64" ;;
    x86_64) echo "osx-x64" ;;
    *) echo "[encryptTools] ERROR: unsupported machine: ${m}" >&2; exit 1 ;;
  esac
}

publish_desktop() {
  local rid="$1"
  local out_dir="$2"
  echo
  echo "[encryptTools] Publishing Desktop (${rid}) -> ${out_dir}"
  rm -rf "${out_dir}"
  mkdir -p "${out_dir}"
  dotnet publish "${DESKTOP_PROJ}" \
    -c "${CONFIG}" \
    -r "${rid}" \
    -f net8.0 \
    --self-contained "${SELF_CONTAINED}" \
    -o "${out_dir}" \
    /p:DebugType=None \
    /p:DebugSymbols=false
}

publish_gcm() {
  local rid="$1"
  local out_dir="$2"
  echo
  echo "[encryptTools] Publishing GcmCli (${rid}) -> ${out_dir}"
  rm -rf "${out_dir}"
  mkdir -p "${out_dir}"
  dotnet publish "${GCM_PROJ}" \
    -c "${CONFIG}" \
    -r "${rid}" \
    -f net8.0 \
    --self-contained "${SELF_CONTAINED}" \
    -o "${out_dir}" \
    /p:DebugType=None \
    /p:DebugSymbols=false
}

echo
echo "[encryptTools] Restoring Desktop + GcmCli..."
dotnet restore "${DESKTOP_PROJ}"
dotnet restore "${GCM_PROJ}"

if [[ "${BUILD_ALL:-}" == "1" ]]; then
  echo "[encryptTools] BUILD_ALL=1: osx-arm64 + osx-x64"
  publish_desktop "osx-arm64" "${DIST}/encryptTools_desktop_macos_osx-arm64"
  publish_gcm "osx-arm64" "${DIST}/encryptTools_gcm_macos_osx-arm64"
  publish_desktop "osx-x64" "${DIST}/encryptTools_desktop_macos_osx-x64"
  publish_gcm "osx-x64" "${DIST}/encryptTools_gcm_macos_osx-x64"
  echo
  echo "[encryptTools] Done."
  echo "图形界面:"
  echo "  ${DIST}/encryptTools_desktop_macos_osx-arm64/encryptTools"
  echo "  ${DIST}/encryptTools_desktop_macos_osx-x64/encryptTools"
else
  RID="${RID:-$(detect_rid)}"
  OUT_DESKTOP="${OUT_DESKTOP:-${DIST}/encryptTools_desktop_macos}"
  OUT_GCM="${OUT_GCM:-${DIST}/encryptTools_gcm_macos}"
  echo "[encryptTools] Target RID: ${RID}"
  publish_desktop "${RID}" "${OUT_DESKTOP}"
  publish_gcm "${RID}" "${OUT_GCM}"
  echo
  echo "[encryptTools] Done."
  echo "主界面: ${OUT_DESKTOP}/encryptTools"
  if [[ "${SELF_CONTAINED}" == "false" ]]; then
    echo "若无法运行，请确认已安装 .NET 8: dotnet --list-runtimes"
  fi
  echo "命令行: ${OUT_GCM}/EncryptTools.GcmCli.dll"
fi
