#!/usr/bin/env bash
set -e

IMAGE="impitrack/api:latest"
VPS_USER="ubuntu"
VPS_HOST="18.217.233.84"
VPS_KEY="$HOME/.ssh/my_key_pair.pem"
COMPOSE_FILE="~/ImpiTraker/ImpiTrack/docker-compose.prod.yml"

echo "==> Building $IMAGE"
docker build -t "$IMAGE" -f "$(dirname "$0")/../Dockerfile" "$(dirname "$0")/.."

echo "==> Pushing $IMAGE to Docker Hub"
docker push "$IMAGE"

echo "==> Deploying on VPS"
ssh -i "$VPS_KEY" -o StrictHostKeyChecking=no "$VPS_USER@$VPS_HOST" \
  "cd ~/ImpiTraker/ImpiTrack && git pull origin dev && docker compose -f docker-compose.prod.yml pull && docker compose -f docker-compose.prod.yml up -d"

echo "==> Done"
