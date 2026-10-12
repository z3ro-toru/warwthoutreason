#!/usr/bin/env bash
set -euo pipefail

# Default to Release if no argument given.
CONFIGURATION="${1:-Release}"

# Validate the argument.
if [[ "$CONFIGURATION" != "Debug" && "$CONFIGURATION" != "Release" ]]; then
    echo "Error: configuration must be 'Debug' or 'Release', got '$CONFIGURATION'." >&2
    exit 1
fi

# Move to the script's own directory, so it works from anywhere.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "Building War Without Reason in $CONFIGURATION configuration..."
dotnet run --project ZZCakeBuild/CakeBuild.csproj -- --configuration="$CONFIGURATION"