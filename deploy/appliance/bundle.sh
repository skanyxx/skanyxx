#!/usr/bin/env bash
# Builds the appliance bundle on a build machine (Docker + Helm): everything install.sh needs next to it.
#   deploy/appliance/bundle.sh                         → dist/skanyxx-appliance/ + dist/skanyxx-appliance-<tag>-<arch>.tar.gz
#   PLATFORM=linux/amd64 TAG=1.2.3 deploy/appliance/bundle.sh
# Contents: install.sh, values.yaml, traefik.yaml, the packaged chart (skanyxx-<version>.tgz, sub-charts inside) and
# skanyxx-image.tar (`docker save skanyxx:<tag>`). The tag defaults to <UTC time>-<commit>: every bundle gets a new
# one, because a node never re-reads a tag it already holds (D140), so a reused tag would never roll the pod.
# The machine still pulls kagent, Postgres, MinIO, Gitea, Dragonfly and k3s/Helm from the internet (D199).
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
PLATFORM=${PLATFORM:-linux/$(docker version --format '{{.Server.Arch}}')}
TAG=${TAG:-$(date -u +%Y%m%d%H%M)-$(git -C "$ROOT" rev-parse --short HEAD)}
OUT=${OUT:-$ROOT/dist/skanyxx-appliance}

rm -rf "$OUT" && mkdir -p "$OUT"
IMAGE=skanyxx TAG=$TAG PLATFORM=$PLATFORM "$ROOT/scripts/helm/build-image.sh"
docker save "skanyxx:$TAG" -o "$OUT/skanyxx-image.tar"
helm dependency build "$ROOT/deploy/helm/skanyxx" >/dev/null
helm package "$ROOT/deploy/helm/skanyxx" -d "$OUT" >/dev/null
cp "$ROOT"/deploy/appliance/{install.sh,values.yaml,traefik.yaml} "$OUT/"
COPYFILE_DISABLE=1 tar --no-xattrs -czf "$OUT-$TAG-${PLATFORM#linux/}.tar.gz" -C "$(dirname "$OUT")" "$(basename "$OUT")"
echo "bundle: $OUT-$TAG-${PLATFORM#linux/}.tar.gz (image skanyxx:$TAG, $PLATFORM)"
echo "on the machine: tar -xzf <bundle> && sudo ./skanyxx-appliance/install.sh --host <dns name>"
