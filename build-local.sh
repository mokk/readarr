#!/usr/bin/env bash
# Package the host-published build into the runtime-only image the compose
# file runs (readarr-fork:local). Publish first with
#   dotnet msbuild -restore src/Readarr.sln -p:Configuration=Release -p:Platform=Posix \
#     -p:RuntimeIdentifiers=$RID -p:AssemblyVersion=$VERSION -p:AssemblyConfiguration=develop -t:PublishAllRids
# and build the UI once with `yarn install && yarn run build --env production`.
# Compiling inside an emulated container segfaults under Rosetta, hence host-side.
set -euo pipefail
RID=${RID:-linux-musl-arm64}
FRAMEWORK=net6.0
VERSION=${VERSION:-0.10.0.2}
cd "$(dirname "$0")"

folder=_artifacts/$RID/$FRAMEWORK/Readarr
rm -rf "$folder"
mkdir -p "$folder"
cp -r "_output/$FRAMEWORK/$RID/publish/"* "$folder"
cp -r "_output/Readarr.Update/$FRAMEWORK/$RID/publish" "$folder/Readarr.Update"
cp -r _output/UI "$folder"
cp LICENSE.md "$folder"
# mirrors build.sh PackageLinux
rm -f "$folder"/ServiceUninstall.* "$folder"/ServiceInstall.* "$folder"/Readarr.Windows.*
cp "$folder"/Readarr.Mono.* "$folder/Readarr.Update"
cp "$folder"/Mono.Posix.NETStandard.* "$folder/Readarr.Update" 2>/dev/null || true
cp "$folder"/libMonoPosixHelper.* "$folder/Readarr.Update" 2>/dev/null || true

# .dockerignore excludes _artifacts, so build from a throwaway context
ctx=$(mktemp -d)
mkdir -p "$ctx/_artifacts/$RID/$FRAMEWORK"
cp -r "$folder" "$ctx/_artifacts/$RID/$FRAMEWORK/"
cp Dockerfile.prebuilt entrypoint.sh "$ctx/"
platform=linux/amd64
[[ $RID == *arm64* ]] && platform=linux/arm64
docker build --platform "$platform" --build-arg "RID=$RID" --build-arg "VERSION=$VERSION" \
  -f "$ctx/Dockerfile.prebuilt" -t readarr-fork:local "$ctx"
rm -rf "$ctx"
echo "built readarr-fork:local ($RID, $VERSION)"
