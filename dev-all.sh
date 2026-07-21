#!/bin/bash
set -euo pipefail

# Starts/stops the whole MusterHub Command local test stack in one go: core
# (MusterHub_Rebuilt, for OTP login + JWKS + the CommandEnabled toggle), the
# Command API, and the control-room web app. Each runs in the background
# with its own log file. Start/stop/status all key off "what's listening on
# the expected port", not tracked PIDs -- same reasoning as Rota/Skills'
# own dev-all.sh, which this mirrors.
#
# Usage:
#   ./dev-all.sh start [core-repo-path]   # default: ../MusterHub_Rebuilt
#   ./dev-all.sh stop
#   ./dev-all.sh status
#   ./dev-all.sh restart [core-repo-path]
#   ./dev-all.sh logs [core|command-api|command-web]
#   ./dev-all.sh token <email> <service-code>   # logs in via OTP, opens the
#                                                # control-room web app with
#                                                # a token
#
# Ports are deliberately different from Rota's (5168/5173) and Skills'
# (5178/5183): Command API 5188, Command web 5193 -- so all three stacks
# can run against the same core at once.
#
# Postgres itself is NOT managed here -- it's assumed to be a persistent
# service shared across whatever else you're working on.
#
# The tablet app (src/MusterHubCommandTablet) isn't part of this script --
# it's a MAUI project, run/debugged from its own build/simulator tooling,
# not `npm run dev`. Point its AppConfig.ApiBaseUrl (DEBUG default:
# http://localhost:5188, or http://10.0.2.2:5188 on the Android emulator)
# at the Command API this script starts.

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
COMMAND_API_DIR="$SCRIPT_DIR/src/MusterHubCommand.Api"
COMMAND_WEB_DIR="$SCRIPT_DIR/src/musterhub-command-web"
DEV_DIR="$SCRIPT_DIR/.dev"
mkdir -p "$DEV_DIR"

CORE_PORT=5005
COMMAND_API_PORT=5188
COMMAND_WEB_PORT=5193

CORE_LOG="$DEV_DIR/core.log"
COMMAND_API_LOG="$DEV_DIR/command-api.log"
COMMAND_WEB_LOG="$DEV_DIR/command-web.log"
CORE_REPO_FILE="$DEV_DIR/core-repo-path"

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

port_pids() {
    lsof -nP -iTCP:"$1" -sTCP:LISTEN -t 2>/dev/null || true
}

is_up() {
    [[ -n "$(port_pids "$1")" ]]
}

wait_for_http() {
    local url="$1" label="$2" timeout="${3:-40}"
    for _ in $(seq 1 "$timeout"); do
        if curl -s -o /dev/null "$url"; then
            echo "    $label is up ($url)"
            return 0
        fi
        sleep 1
    done
    echo "    ERROR: $label did not respond at $url within ${timeout}s -- see its log"
    return 1
}

resolve_core_repo() {
    local candidate="${1:-}"
    if [[ -n "$candidate" ]]; then
        echo "$(cd "$candidate" && pwd)"
        return
    fi
    if [[ -f "$CORE_REPO_FILE" ]]; then
        cat "$CORE_REPO_FILE"
        return
    fi
    local default="$SCRIPT_DIR/../MusterHub_Rebuilt"
    if [[ -d "$default" ]]; then
        echo "$(cd "$default" && pwd)"
        return
    fi
    echo ""
}

# ---------------------------------------------------------------------------
# start
# ---------------------------------------------------------------------------

cmd_start() {
    local core_repo
    core_repo="$(resolve_core_repo "${1:-}")"
    if [[ -z "$core_repo" || ! -f "$core_repo/dev-api.sh" ]]; then
        echo "ERROR: couldn't find the MusterHub_Rebuilt repo (looked for dev-api.sh)."
        echo "  Pass its path explicitly: ./dev-all.sh start /path/to/MusterHub_Rebuilt"
        exit 1
    fi
    echo "$core_repo" > "$CORE_REPO_FILE"

    if ! command -v pg_isready >/dev/null 2>&1 || ! pg_isready -h localhost -q 2>/dev/null; then
        echo "WARNING: Postgres doesn't look reachable on localhost. Both APIs need it."
        echo "         e.g. brew services start postgresql@17"
    fi

    echo "==> Core API ($core_repo)"
    if is_up "$CORE_PORT"; then
        echo "    already running (port $CORE_PORT)"
    else
        ( cd "$core_repo" && nohup ./dev-api.sh > "$CORE_LOG" 2>&1 & )
        wait_for_http "http://localhost:$CORE_PORT/.well-known/jwks.json" "Core API" 40
    fi

    echo "==> Command API ($COMMAND_API_DIR)"
    if is_up "$COMMAND_API_PORT"; then
        echo "    already running (port $COMMAND_API_PORT)"
    else
        echo "    applying migrations..."
        ( cd "$COMMAND_API_DIR" && dotnet ef database update ) >> "$COMMAND_API_LOG" 2>&1
        (
            cd "$COMMAND_API_DIR"
            Core__JwksUri="http://localhost:$CORE_PORT/.well-known/jwks.json" \
            Core__Issuer="MusterHub" \
            Core__Audience="MusterHubApp" \
            ASPNETCORE_ENVIRONMENT=Development \
            nohup dotnet run --urls "http://localhost:$COMMAND_API_PORT" > "$COMMAND_API_LOG" 2>&1 &
        )
        wait_for_http "http://localhost:$COMMAND_API_PORT/api/me" "Command API" 60
    fi

    echo "==> Command web ($COMMAND_WEB_DIR)"
    if is_up "$COMMAND_WEB_PORT"; then
        echo "    already running (port $COMMAND_WEB_PORT)"
    else
        if [[ ! -d "$COMMAND_WEB_DIR/node_modules" ]]; then
            echo "    installing dependencies (first run)..."
            ( cd "$SCRIPT_DIR" && npm install ) >> "$COMMAND_WEB_LOG" 2>&1
        fi
        ( cd "$COMMAND_WEB_DIR" && nohup npm run dev > "$COMMAND_WEB_LOG" 2>&1 & )
        wait_for_http "http://localhost:$COMMAND_WEB_PORT/" "Command web" 30
    fi

    cat <<EOF

Stack is up:
  Core admin portal   http://localhost:$CORE_PORT/Admin
  Core JWKS           http://localhost:$CORE_PORT/.well-known/jwks.json
  Command API         http://localhost:$COMMAND_API_PORT
  Command web         http://localhost:$COMMAND_WEB_PORT

If this org doesn't have Command enabled yet: Core admin portal -> Services ->
Config -> Bolt-on products -> MusterHub Command.

To get into the control-room web app: ./dev-all.sh token <email> <service-code>
(there's no "Command" link inside core's own UI in dev, so this does the OTP
login for you and opens the app with a token).

Push notifications (new-incident alerts) no-op in dev unless you also export
Core__NotificationsUri / Core__NotificationApiKey before running this script
-- see CoreNotificationService.IsConfigured.

The tablet app isn't started by this script -- run it from Xcode/Android
Studio or 'dotnet build -t:Run' against src/MusterHubCommandTablet, pointed
at http://localhost:$COMMAND_API_PORT.

Logs: $DEV_DIR/{core,command-api,command-web}.log
Stop: ./dev-all.sh stop
EOF
}

# ---------------------------------------------------------------------------
# stop
# ---------------------------------------------------------------------------

stop_port() {
    local port="$1" label="$2"
    local pids
    pids="$(port_pids "$port")"

    if [[ -z "$pids" ]]; then
        echo "==> $label was not running"
        return
    fi

    kill $pids 2>/dev/null || true
    for _ in $(seq 1 10); do
        is_up "$port" || break
        sleep 0.5
    done

    pids="$(port_pids "$port")"
    if [[ -n "$pids" ]]; then
        kill -9 $pids 2>/dev/null || true
    fi
    echo "==> Stopped $label"
}

cmd_stop() {
    stop_port "$COMMAND_WEB_PORT" "Command web"
    stop_port "$COMMAND_API_PORT" "Command API"
    stop_port "$CORE_PORT" "Core API"
}

# ---------------------------------------------------------------------------
# status / logs
# ---------------------------------------------------------------------------

cmd_status() {
    for entry in "Core API:$CORE_PORT" "Command API:$COMMAND_API_PORT" "Command web:$COMMAND_WEB_PORT"; do
        IFS=":" read -r label port <<< "$entry"
        local pids
        pids="$(port_pids "$port")"
        if [[ -n "$pids" ]]; then
            echo "$label: running (port $port, pid $pids)"
        else
            echo "$label: stopped"
        fi
    done
}

cmd_logs() {
    case "${1:-}" in
        core) tail -f "$CORE_LOG" ;;
        command-api) tail -f "$COMMAND_API_LOG" ;;
        command-web) tail -f "$COMMAND_WEB_LOG" ;;
        *) tail -f "$CORE_LOG" "$COMMAND_API_LOG" "$COMMAND_WEB_LOG" ;;
    esac
}

# ---------------------------------------------------------------------------
# token -- OTP login against core, then open the Command web app with it
# ---------------------------------------------------------------------------

cmd_token() {
    local email="${1:-}" service_code="${2:-}"
    if [[ -z "$email" || -z "$service_code" ]]; then
        echo "Usage: $0 token <email> <service-code>"
        exit 1
    fi

    local core_repo
    core_repo="$(resolve_core_repo "")"
    if [[ -z "$core_repo" ]]; then
        echo "ERROR: don't know where the core repo is -- run './dev-all.sh start' at least once first."
        exit 1
    fi
    local settings="$core_repo/MusterHub.Api/appsettings.Development.json"
    if [[ ! -f "$settings" ]]; then
        echo "ERROR: $settings not found."
        exit 1
    fi

    if ! is_up "$CORE_PORT"; then
        echo "ERROR: Core API isn't running on port $CORE_PORT. Run './dev-all.sh start' first."
        exit 1
    fi

    read -r DB_HOST DB_NAME DB_USER <<< "$(python3 - "$settings" <<'PYEOF'
import json, sys
cs = json.load(open(sys.argv[1]))["ConnectionStrings"]["Default"]
parts = dict(p.split("=", 1) for p in cs.split(";") if "=" in p)
print(parts.get("Host", "localhost"), parts.get("Database", ""), parts.get("Username", ""))
PYEOF
)"

    echo "==> Requesting OTP for $email ($service_code)..."
    local otp_response
    otp_response="$(curl -s -X POST "http://localhost:$CORE_PORT/api/auth/request-otp" \
        -H "Content-Type: application/json" \
        -d "{\"serviceCode\":\"$service_code\",\"email\":\"$email\"}")"
    if ! echo "$otp_response" | grep -q '"success":true'; then
        echo "ERROR: request-otp failed: $otp_response"
        exit 1
    fi

    local otp=""
    for _ in $(seq 1 10); do
        otp="$(psql -h "$DB_HOST" -U "$DB_USER" -d "$DB_NAME" -t -A \
            -c "SELECT \"Code\" FROM \"OtpCodes\" WHERE \"Email\"='$email' ORDER BY \"CreatedAtUtc\" DESC LIMIT 1;" 2>/dev/null || true)"
        [[ -n "$otp" ]] && break
        sleep 1
    done
    if [[ -z "$otp" ]]; then
        echo "ERROR: couldn't read the OTP code from the database (table OtpCodes, email $email)."
        exit 1
    fi

    echo "==> Verifying OTP..."
    local login_response access_token
    login_response="$(curl -s -X POST "http://localhost:$CORE_PORT/api/auth/verify-otp" \
        -H "Content-Type: application/json" \
        -d "{\"serviceCode\":\"$service_code\",\"email\":\"$email\",\"otp\":\"$otp\",\"devicePlatform\":\"Dev\",\"deviceName\":\"dev-all.sh\"}")"
    access_token="$(echo "$login_response" | python3 -c "import json,sys; print(json.load(sys.stdin).get('accessToken',''))" 2>/dev/null || true)"
    if [[ -z "$access_token" ]]; then
        echo "ERROR: verify-otp failed: $login_response"
        exit 1
    fi

    local has_command
    has_command="$(python3 - "$access_token" <<'PYEOF'
import sys, json, base64
token = sys.argv[1]
payload = token.split(".")[1]
payload += "=" * (-len(payload) % 4)
claims = json.loads(base64.urlsafe_b64decode(payload))
print("yes" if "command" in str(claims.get("entitlements", "")) else "no")
PYEOF
)"
    if [[ "$has_command" != "yes" ]]; then
        echo ""
        echo "WARNING: this token has no 'command' entitlement, so the web app will still"
        echo "         show Session expired. Enable it first:"
        echo "         Core admin portal -> Services -> Config -> Bolt-on products -> MusterHub Command"
        echo "         ($core_repo/... -> http://localhost:$CORE_PORT/Admin)"
        echo ""
    fi

    local url="http://localhost:$COMMAND_WEB_PORT/#token=$access_token"
    echo ""
    echo "==> $url"
    command -v open >/dev/null 2>&1 && open "$url"
}

# ---------------------------------------------------------------------------
# main
# ---------------------------------------------------------------------------

case "${1:-start}" in
    start) cmd_start "${2:-}" ;;
    stop) cmd_stop ;;
    restart) cmd_stop; cmd_start "${2:-}" ;;
    status) cmd_status ;;
    logs) cmd_logs "${2:-}" ;;
    token) cmd_token "${2:-}" "${3:-}" ;;
    *)
        echo "Usage: $0 {start|stop|restart|status|logs|token} [args]"
        exit 1
        ;;
esac
