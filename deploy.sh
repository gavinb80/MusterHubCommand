#!/bin/bash
set -e

# -----------------------------------------------------------------------
# MusterHub Command deploy script
#
# Publishes the API and bundles the control-room React web app's built
# assets into its wwwroot, then deploys to Azure App Service via ZIP
# deploy. One App Service serves both -- mirrors Rota/Skills' own
# deploy.sh. Does NOT touch the tablet app (src/MusterHubCommandTablet):
# that's a separate App Store / Play Store listing with its own release
# process entirely, decoupled deliberately from this deploy.
#
# Usage:
#   ./deploy.sh                          # publish only (no deploy)
#   ./deploy.sh --deploy                 # publish and deploy to Azure
#   ./deploy.sh --deploy --rg my-rg      # specify resource group
#   ./deploy.sh --deploy --app my-app    # specify app service name
#
# Before the first deploy: run ./provision-prod.sh and ./migrate-prod.sh.
# -----------------------------------------------------------------------

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
WEB_DIR="$SCRIPT_DIR/src/musterhub-command-web"
API_PROJECT="$SCRIPT_DIR/src/MusterHubCommand.Api/MusterHubCommand.Api.csproj"
PUBLISH_DIR="$SCRIPT_DIR/publish"
ZIP_FILE="$SCRIPT_DIR/deploy.zip"

RESOURCE_GROUP="rg-musterhub"
APP_NAME="musterhub-command"
DO_DEPLOY=false

while [[ $# -gt 0 ]]; do
    case "$1" in
        --deploy)  DO_DEPLOY=true; shift ;;
        --rg)      RESOURCE_GROUP="$2"; shift 2 ;;
        --app)     APP_NAME="$2"; shift 2 ;;
        --help|-h)
            echo "Usage: ./deploy.sh [--deploy] [--rg <resource-group>] [--app <app-name>]"
            echo ""
            echo "  --deploy   Deploy to Azure after publishing (default: publish only)"
            echo "  --rg       Azure resource group (default: $RESOURCE_GROUP)"
            echo "  --app      Azure App Service name (default: $APP_NAME)"
            exit 0 ;;
        *) echo "Unknown option: $1"; exit 1 ;;
    esac
done

echo "==> Cleaning previous publish output..."
rm -rf "$PUBLISH_DIR" "$ZIP_FILE" "$WEB_DIR/dist"

echo "==> Building web app..."
( cd "$WEB_DIR" && npm run build --silent )

echo "==> Publishing API..."
dotnet publish "$API_PROJECT" -c Release -o "$PUBLISH_DIR" --nologo -v quiet

echo "==> Bundling web app into API's wwwroot..."
mkdir -p "$PUBLISH_DIR/wwwroot"
cp -r "$WEB_DIR/dist/." "$PUBLISH_DIR/wwwroot/"

echo "==> Creating deploy.zip..."
(cd "$PUBLISH_DIR" && zip -r "$ZIP_FILE" . -q)

ZIPSIZE=$(du -sh "$ZIP_FILE" | cut -f1)
echo "    Package size: $ZIPSIZE"

if [ "$DO_DEPLOY" = true ]; then
    echo ""
    echo "==> Deploying to Azure..."
    echo "    Resource group: $RESOURCE_GROUP"
    echo "    App Service:    $APP_NAME"
    echo ""

    if ! command -v az &> /dev/null; then
        echo "ERROR: Azure CLI (az) is not installed."
        exit 1
    fi

    if ! az account show &> /dev/null; then
        echo "ERROR: Not logged in to Azure. Run 'az login' first."
        exit 1
    fi

    az webapp deploy \
        --resource-group "$RESOURCE_GROUP" \
        --name "$APP_NAME" \
        --src-path "$ZIP_FILE" \
        --type zip \
        --clean true

    echo ""
    echo "Deployment complete."
    echo "  Command app:  https://$APP_NAME.azurewebsites.net/"
    echo "  Command API:  https://$APP_NAME.azurewebsites.net/api/me"
else
    echo ""
    echo "Publish complete. Output in: $PUBLISH_DIR"
    echo "ZIP package: $ZIP_FILE"
    echo ""
    echo "Run with --deploy to push to Azure."
fi
