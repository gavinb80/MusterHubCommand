#!/bin/bash
set -e

# Applies EF migrations to the production database, reading the
# connection string from Key Vault without ever printing it. Run before
# every deploy that includes a new migration (same sequence as Rota/Skills:
# migrate first, then deploy).

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
CONN=$(az keyvault secret show --vault-name musterhubapp --name command-db-connection --query value -o tsv)
cd "$SCRIPT_DIR/src/MusterHubCommand.Api"
dotnet ef database update --connection "$CONN"
echo "Migrations applied."
