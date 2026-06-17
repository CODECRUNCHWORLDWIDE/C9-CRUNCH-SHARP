#!/usr/bin/env bash
# bootstrap.sh — stand up the Azure Container Apps environment, the app, and the
# secrets ONCE. After this, the pipeline (deploy.yml) only updates the image.
# See lecture-notes/02-github-actions-cd-to-azure-container-apps.md §4.
#
# Prereqs: `az login` works; the containerapp extension is installed
# (`az extension add --name containerapp`). Set the env vars below first.
set -euo pipefail

# ---- fill these in ----
RG="rg-workshop-capstone"
LOCATION="eastus"
ENV_NAME="workshop-env"
APP_NAME="workshop-api"
IMAGE="ghcr.io/<org>/polyglot-workshop:sha-<first-commit>"   # the first image to seed the app
: "${PG_CONN:?set PG_CONN to the PostgreSQL connection string}"
: "${KC_SECRET:?set KC_SECRET to the Keycloak OIDC client secret}"

echo "==> resource group + Container Apps environment"
az group create --name "$RG" --location "$LOCATION"
az containerapp env create \
  --name "$ENV_NAME" \
  --resource-group "$RG" \
  --location "$LOCATION"

echo "==> the app: external ingress on 8080, scale-to-zero, secrets referenced not inlined"
az containerapp create \
  --name "$APP_NAME" \
  --resource-group "$RG" \
  --environment "$ENV_NAME" \
  --image "$IMAGE" \
  --registry-server ghcr.io \
  --target-port 8080 \
  --ingress external \
  --min-replicas 0 \
  --max-replicas 3 \
  --secrets "db-conn=$PG_CONN" "keycloak-secret=$KC_SECRET" \
  --env-vars \
    "ConnectionStrings__Workshop=secretref:db-conn" \
    "Oidc__ClientSecret=secretref:keycloak-secret" \
    "ASPNETCORE_ENVIRONMENT=Production"

echo "==> done. Public URL:"
az containerapp show --name "$APP_NAME" --resource-group "$RG" \
  --query "properties.configuration.ingress.fqdn" -o tsv

echo "Configure the readiness probe (hits /health) in the portal or via 'az containerapp update'."
echo "Then push to main; the pipeline takes it from here."
