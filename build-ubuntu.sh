#!/usr/bin/env bash
# ============================================================
# encryptTools — Ubuntu / Linux x64 打包
# - 主程序: EncryptTools.Desktop（Avalonia 图形界面），输出 encryptTools 可执行文件
# - 附加: EncryptTools.GcmCli（命令行，与 Windows 版格式一致）
# - 输出:
#     dist/encryptTools_desktop_ubuntu/   ← 运行此目录下的 ./encryptTools 打开主界面
#     dist/encryptTools_gcm_ubuntu/         ← 仅命令行加解密
# - 需本机已安装 .NET 8 运行时，或使用 SELF_CONTAINED=1 自包含（体积大）
# - 可选: RID=linux-arm64 等
# ============================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

CONFIG="${CONFIG:-Release}"
RID="${RID:-linux-x64}"
DIST="${SCRIPT_DIR}/dist"
OUT_DESKTOP="${OUT_DESKTOP:-${DIST}/encryptTools_desktop_ubuntu}"
OUT_GCM="${OUT_GCM:-${DIST}/encryptTools_gcm_ubuntu}"
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

echo
echo "[encryptTools] Cleaning output directories..."
rm -rf "${OUT_DESKTOP}" "${OUT_GCM}"
mkdir -p "${OUT_DESKTOP}" "${OUT_GCM}"

echo
echo "[encryptTools] Restoring Desktop + GcmCli..."
dotnet restore "${DESKTOP_PROJ}"
dotnet restore "${GCM_PROJ}"

echo
echo "[encryptTools] Publishing Desktop — 图形界面 (${RID}, self-contained=${SELF_CONTAINED})..."
dotnet publish "${DESKTOP_PROJ}" \
  -c "${CONFIG}" \
  -r "${RID}" \
  -f net8.0 \
  --self-contained "${SELF_CONTAINED}" \
  -o "${OUT_DESKTOP}" \
  /p:DebugType=None \
  /p:DebugSymbols=false

echo
echo "[encryptTools] Publishing GcmCli — 命令行 (${RID}, self-contained=${SELF_CONTAINED})..."
dotnet publish "${GCM_PROJ}" \
  -c "${CONFIG}" \
  -r "${RID}" \
  -f net8.0 \
  --self-contained "${SELF_CONTAINED}" \
  -o "${OUT_GCM}" \
  /p:DebugType=None \
  /p:DebugSymbols=false

echo
echo "[encryptTools] Desktop launcher + PNG icon (Linux taskbar / menu)..."
DESKTOP_FILE="${OUT_DESKTOP}/encryptTools.desktop"
PNG_SRC="${SCRIPT_DIR}/app2.png"
PNG_DST="${OUT_DESKTOP}/encryptTools.png"
if [[ -f "${PNG_SRC}" ]]; then
  cp -f "${PNG_SRC}" "${PNG_DST}"
  EXEC_LINE="${OUT_DESKTOP}/encryptTools"
  ICON_LINE="${PNG_DST}"
  cat > "${DESKTOP_FILE}" << EOF
[Desktop Entry]
Name=encryptTools
Comment=encryptTools 工作区（文件 / 字符串 / 图片）
Exec=${EXEC_LINE}
Path=${OUT_DESKTOP}
Icon=${ICON_LINE}
Type=Application
Terminal=false
Categories=Utility;Security;
EOF
  chmod 0644 "${DESKTOP_FILE}" 2>/dev/null || true
  echo "  已写入: ${DESKTOP_FILE}"
  echo "  图标: ${PNG_DST}（任务栏需桌面环境从 .desktop 启动或手动指定 Icon）"
else
  echo "  (跳过) 未找到 ${PNG_SRC}"
fi

echo
echo "[encryptTools] Done."
echo "主界面（推荐）: ${OUT_DESKTOP}/"
echo "  运行: \"${OUT_DESKTOP}/encryptTools\""
if [[ "${SELF_CONTAINED}" == "false" ]]; then
  echo "  若直接运行失败，请确认已安装 .NET 8: dotnet --list-runtimes"
fi
echo "命令行工具: ${OUT_GCM}/"
echo "  示例: dotnet \"${OUT_GCM}/EncryptTools.GcmCli.dll\" --encrypt --input <in> --output <out> --password-file <pwd.txt>"
