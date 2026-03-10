#!/bin/sh
set -eu

PACKAGES_DIR="$BUILD_WORKSPACE_DIRECTORY/packages/dotnet"

cd "$PACKAGES_DIR"

# install packages
paket install

# generate Bazel targets for each package
bazel run @rules_dotnet//tools/paket2bazel -- \
    --dependencies-file "$PACKAGES_DIR/paket.dependencies" \
    --output-folder "$PACKAGES_DIR/extension"
