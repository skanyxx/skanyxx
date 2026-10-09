#!/usr/bin/env bash
# Builds the Skanyxx image the Helm chart deploys (Dockerfile at the repo root).
#   scripts/helm/build-image.sh                 → skanyxx:dev
#   IMAGE=registry.example.com/skanyxx TAG=1.2.3 scripts/helm/build-image.sh
#   KIND_CLUSTER=skanyxx-helm scripts/helm/build-image.sh   also loads it into that kind cluster
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
IMAGE=${IMAGE:-skanyxx}
TAG=${TAG:-dev}

docker build -t "$IMAGE:$TAG" "$ROOT"
if [[ -n ${KIND_CLUSTER:-} ]]; then
  kind load docker-image "$IMAGE:$TAG" --name "$KIND_CLUSTER"
fi
echo "built $IMAGE:$TAG"
