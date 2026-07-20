#!/bin/bash
set -e

# -----------------------------------------------------------------------
# One-time production provisioning for MusterHub Command.
#
# Adds NOTHING billable: the app rides the existing plan-musterhub App
# Service plan and the database is another database on the existing
# musterhub-pg flexible server -- same shape as Rota/Skills.
#
# Run order for first go-live:
#   1. ./provision-prod.sh            (this script -- infra + settings)
#   2. Create the two Key Vault secrets it references (see the notes it
#      prints at the end).
#   3. ./migrate-prod.sh              (applies EF migrations)
#   4. ./deploy.sh --deploy           (ships the API + control-room web app)
#   5. Admin portal: enable the Command bolt-on for the service, and mint
#      the NotificationSend key used in step 2.
#   6. Publish the tablet app (src/MusterHubCommandTablet) to the App
#      Store / Play Store separately -- it's its own listing, not part of
#      this deploy at all.
# -----------------------------------------------------------------------

RG=rg-musterhub
PLAN=plan-musterhub
APP=musterhub-command
PG=musterhub-pg
VAULT=musterhubapp
CORE=https://musterhub-api.azurewebsites.net

echo "==> Database (musterhubcommand on $PG)..."
az postgres flexible-server db create --resource-group $RG --server-name $PG --name musterhubcommand -o none
echo "    ready"

echo "==> App Service ($APP on $PLAN)..."
az webapp create -g $RG -p $PLAN -n $APP --runtime "DOTNETCORE:10.0" -o none
echo "    ready"

echo "==> Managed identity + Key Vault access..."
az webapp identity assign -g $RG -n $APP -o none
PRINCIPAL=$(az webapp identity show -g $RG -n $APP --query principalId -o tsv)
VAULT_ID=$(az keyvault show -n $VAULT --query id -o tsv)
# The object-id + principal-type form skips the Graph lookup that fails
# when a freshly created identity hasn't propagated yet -- and never
# swallow this error: without the role, Key Vault references silently
# fail to resolve and the app crash-loops at startup.
az role assignment create --assignee-object-id "$PRINCIPAL" --assignee-principal-type ServicePrincipal \
  --role "Key Vault Secrets User" --scope "$VAULT_ID" -o none \
  || echo "    WARNING: role assignment failed -- if it doesn't already exist, KV references WILL NOT resolve"
echo "    ready"

echo "==> Hardening + health check..."
az webapp update -g $RG -n $APP --https-only true -o none
az webapp config set -g $RG -n $APP --always-on true --generic-configurations '{"healthCheckPath": "/health"}' -o none
echo "    ready"

echo "==> App settings (Key Vault references for every secret)..."
az webapp config appsettings set -g $RG -n $APP -o none --settings \
  ASPNETCORE_ENVIRONMENT=Production \
  Core__JwksUri="$CORE/.well-known/jwks.json" \
  Core__Issuer=MusterHub \
  Core__Audience=MusterHubApp \
  Core__NotificationsUri="$CORE/api/integrations/notifications" \
  "ConnectionStrings__Default=@Microsoft.KeyVault(SecretUri=https://$VAULT.vault.azure.net/secrets/command-db-connection/)" \
  "Core__NotificationApiKey=@Microsoft.KeyVault(SecretUri=https://$VAULT.vault.azure.net/secrets/command-notification-api-key/)"
echo "    ready"

cat <<'NOTES'

Provisioned. Before migrating and deploying, create the two secrets
(neither of these commands print a secret):

  # 1. DB connection: Skills' own, pointed at the new database
  az keyvault secret set --vault-name musterhubapp --name command-db-connection -o none \
    --value "$(az keyvault secret show --vault-name musterhubapp --name skills-db-connection --query value -o tsv | sed 's/Database=[^;]*/Database=musterhubcommand/')"

  # 2. Push notifications: mint a NotificationSend-scoped key for the
  #    service in the admin portal (Integrations page), then:
  #    az keyvault secret set --vault-name musterhubapp --name command-notification-api-key --value "<paste>" -o none
  #    (Until this exists the app still runs -- notifications no-op.)
  #    Placeholder so the KV reference resolves in the meantime:
  az keyvault secret set --vault-name musterhubapp --name command-notification-api-key --value "not-configured-yet" -o none

Then: ./migrate-prod.sh && ./deploy.sh --deploy

Note: unlike Skills, Command's own Vision-facing integration keys are
issued per-organisation from Setup > Integration keys (database-backed,
see IntegrationApiKeysController) -- there is no shared appsettings
integration secret to provision here.
NOTES
