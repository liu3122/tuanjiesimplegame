#!/usr/bin/env bash
# 多平台一键构建入口（Git Bash / macOS 通用；iOS 目标仅 macOS 可用）
#
# 用法：
#   Tools/ci/build_all.sh                  # 默认本机全打：win steam android webgl
#   Tools/ci/build_all.sh steam android    # 只打指定目标
#   UNITY_BIN=/path/to/Unity Tools/ci/build_all.sh   # 覆盖 Unity 路径
#
# 目标：win | steam | android | webgl | ios | all
# 产物：Build/<target>_<version>/，日志：Build/logs/ci_<target>.log
# 前置：本机已装对应平台模块（Android SDK/NDK、WebGL）；Android 正式包需在
#       Player Settings 配置 keystore；微信小游戏需再用官方转换工具处理 WebGL 产物。
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

UNITY_BIN="${UNITY_BIN:-D:/Program Files/Unity 2019.4.40f1/Editor/Unity.exe}"
LOG_DIR="Build/logs"
mkdir -p "$LOG_DIR"

if [ ! -e "$UNITY_BIN" ]; then
  echo "!! Unity 不存在：$UNITY_BIN（用 UNITY_BIN=... 覆盖）"
  exit 2
fi

if [ $# -eq 0 ]; then
  set -- win steam android webgl
fi

fail=0
for t in "$@"; do
  case "$t" in
    win)    method=CIBuild.BuildWindows ;;
    steam)  method=CIBuild.BuildSteam ;;
    android) method=CIBuild.BuildAndroid ;;
    webgl)  method=CIBuild.BuildWebgl ;;
    ios)    method=CIBuild.BuildIos ;;
    all)    set -- win steam android webgl; continue ;;
    *) echo "未知目标：$t（可选 win/steam/android/webgl/ios/all）"; exit 2 ;;
  esac

  log="$LOG_DIR/ci_${t}.log"
  echo "==> [$t] 构建中（日志：$log）"
  "$UNITY_BIN" -batchmode -quit -projectPath "$ROOT" -executeMethod "$method" -logFile "$log"
  code=$?
  if [ $code -ne 0 ]; then
    echo "!! [$t] 失败（exit $code），错误摘要："
    grep -aE "error CS|Exception|FAIL|\[CIBuild\].*失败" "$log" | head -20
    fail=1
  else
    echo "OK [$t]"
  fi
done

echo "---- 产物清单 ----"
ls -1 Build/ 2>/dev/null | grep -v logs
exit $fail
