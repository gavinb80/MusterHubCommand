#!/bin/bash

set -e

# -----------------------------
# Configuration
# -----------------------------
PROJECT="MusterHubCommandTablet.csproj"

CLEAN=false
RUN=false
RESTORE=false
DEVICE=""

# -----------------------------
# Parse Arguments
# -----------------------------
while [[ $# -gt 0 ]]; do
    case "$1" in
        --clean)
            CLEAN=true
            shift
            ;;

        --restore)
            RESTORE=true
            shift
            ;;

        --run)
            RUN=true
            shift
            ;;

        -d|--device)
            DEVICE="$2"
            shift 2
            ;;

        -h|--help)
            echo "Usage:"
            echo "  ./dev.sh [options]"
            echo ""
            echo "Options:"
            echo "  --clean           Clean project"
            echo "  --restore         Restore packages"
            echo "  --run             Run project"
            echo "  -d DEVICE         android | ios | maccatalyst | windows"
            exit 0
            ;;

        *)
            echo "Unknown argument: $1"
            exit 1
            ;;
    esac
done

# -----------------------------
# Determine Framework
# -----------------------------
FRAMEWORK=""

case "$DEVICE" in
    android)
        FRAMEWORK="net10.0-android"
        ;;
    ios)
        FRAMEWORK="net10.0-ios"
        ;;
    maccatalyst)
        FRAMEWORK="net10.0-maccatalyst"
        ;;
    windows)
        FRAMEWORK="net10.0-windows10.0.19041.0"
        ;;
    "")
        ;;
    *)
        echo "Unknown device '$DEVICE'"
        exit 1
        ;;
esac

# -----------------------------
# Clean
# -----------------------------
if $CLEAN; then
    echo "Cleaning..."

    dotnet clean "$PROJECT"

    find . -name bin -type d -exec rm -rf {} +
    find . -name obj -type d -exec rm -rf {} +

    dotnet restore "$PROJECT"
fi

# -----------------------------
# Restore
# -----------------------------
if $RESTORE; then
    echo "Restoring..."
    dotnet restore "$PROJECT"
fi

# -----------------------------
# Run
# -----------------------------
if $RUN; then
    CMD="dotnet build"

    if [[ -n "$FRAMEWORK" ]]; then
        CMD="$CMD -t:run -f $FRAMEWORK"
    fi

    echo "Running:"
    echo "$CMD"

    eval "$CMD"
fi
