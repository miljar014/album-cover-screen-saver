#!/bin/bash
# Convenience wrapper: builds and writes all output to build.log so it can be
# read back easily. Usage:  ./build.sh   or   ./build.sh install
cd "$(dirname "$0")"
{ make "${1:-all}" 2>&1; echo "EXIT_CODE=$?"; } | tee build.log
