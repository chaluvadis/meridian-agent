#!/usr/bin/env bash
#
# deploy-local.sh - clean Docker state, rebuild, and deploy The Meridian Agent
# locally with docker-compose for testing.
#
# Usage:
#   ./deploy-local.sh [options]
#
# Options:
#   --keep-models    Keep the qdrant-data volume (skips re-embedding the rulebooks;
#                    faster iteration)
#   --no-cache       Build without Docker's layer cache (slowest, cleanest)
#   --no-build       Skip the image build, reuse whatever is cached
#   -l, --logs       Follow the web app logs after startup
#   -y, --yes        Do not ask for confirmation
#   --dry-run        Print every command without executing anything
#   -h, --help       Show this help

set -euo pipefail

# --- configuration -----------------------------------------------------------

COMPOSE_FILE="docker-compose.yml"
WEB_IMAGE="meridian-agent-meridian-web"
HEALTH_PATH="/health"
HEALTH_TIMEOUT_SECONDS=120


# --- output helpers ----------------------------------------------------------

if [ -t 1 ]; then
    C_RESET="$(printf '\033[0m')"
    C_BLUE="$(printf '\033[1;34m')"
    C_GREEN="$(printf '\033[1;32m')"
    C_YELLOW="$(printf '\033[1;33m')"
    C_RED="$(printf '\033[1;31m')"
else
    C_RESET="" C_BLUE="" C_GREEN="" C_YELLOW="" C_RED=""
fi

step() { printf '%s==>%s %s\n' "$C_BLUE" "$C_RESET" "$1"; }
ok()   { printf '%s==>%s %s\n' "$C_GREEN" "$C_RESET" "$1"; }
warn() { printf '%s==>%s %s\n' "$C_YELLOW" "$C_RESET" "$1"; }
die()  { printf '%sERROR:%s %s\n' "$C_RED" "$C_RESET" "$1" >&2; exit 1; }

# --- argument parsing --------------------------------------------------------

KEEP_MODELS=false
NO_CACHE=false
NO_BUILD=false
FOLLOW_LOGS=false
ASSUME_YES=false
DRY_RUN=false

while [ $# -gt 0 ]; do
    case "$1" in
        --keep-models) KEEP_MODELS=true ;;
        --no-cache)    NO_CACHE=true ;;
        --no-build)    NO_BUILD=true ;;
        -l|--logs)     FOLLOW_LOGS=true ;;
        -y|--yes)      ASSUME_YES=true ;;
        --dry-run)     DRY_RUN=true ;;
        -h|--help)
            sed -n '2,20p' "$0" | sed 's/^# \{0,1\}//'
            exit 0
            ;;
        *) die "Unknown option: $1 (try --help)" ;;
    esac
    shift
done

run() {
    printf '  %s$ %s%s\n' "$C_YELLOW" "$*" "$C_RESET"
    if [ "$DRY_RUN" = true ]; then
        return 0
    fi
    "$@"
}

run_shell() {
    printf '  %s$ %s%s\n' "$C_YELLOW" "$1" "$C_RESET"
    if [ "$DRY_RUN" = true ]; then
        return 0
    fi
    bash -c "$1"
}

# --- preflight ---------------------------------------------------------------

command -v docker >/dev/null 2>&1 || die "docker is not installed or not on PATH."

if [ "$DRY_RUN" = false ]; then
    docker info >/dev/null 2>&1 || die "Docker daemon is not running. Start Docker Desktop first."
fi

[ -f "$COMPOSE_FILE" ] || die "$COMPOSE_FILE not found. Run this script from the repository root."

if docker compose version >/dev/null 2>&1; then
    COMPOSE=(docker compose)
elif command -v docker-compose >/dev/null 2>&1; then
    COMPOSE=(docker-compose)
else
    die "Neither 'docker compose' nor 'docker-compose' is available."
fi

# Host port for the web app (docker-compose reads .env too; this is just for our probe).
WEB_PORT="$(grep -E '^WEB_PORT=' .env 2>/dev/null | tail -n1 | cut -d= -f2 || true)"
WEB_PORT="${WEB_PORT:-8080}"

if [ "$DRY_RUN" = false ]; then
    timeout 10 docker pull hello-world:latest >/dev/null 2>&1 || true
fi

if [ "$DRY_RUN" = false ] && [ -z "${KILO_API_KEY:-}" ] && ! grep -q '^[[:space:]]*KILO_API_KEY=[^[:space:]]' .env 2>/dev/null; then
    die "KILO_API_KEY is not set: the web container fails at startup with an 'AI:ApiKey is missing' error and crash-loops. Get your key at https://app.kilo.ai/profile and export it or add it to .env (see .env.example) — the app has no local model to fall back to."
fi

# --- confirmation ------------------------------------------------------------

if [ "$ASSUME_YES" = false ] && [ "$DRY_RUN" = false ]; then
    warn "This will stop the Meridian Agent stack, remove its containers and networks,"
    if [ "$KEEP_MODELS" = true ]; then
        warn "keep the qdrant-data volume — its vectors are kept UNLESS the configured"
        warn "vector size changed, in which case the collections are rebuilt on startup,"
        warn "rebuild the image, and redeploy."
    else
        warn "DELETE the qdrant-data volume (the rulebooks are re-embedded on first run),"
        warn "rebuild the image, and redeploy."
    fi
    printf 'Continue? [y/N] '
    read -r reply
    case "$reply" in
        [yY]|[yY][eE][sS]) ;;
        *) die "Aborted." ;;
    esac
fi

# --- 1. clean ----------------------------------------------------------------

step "Stopping and removing containers, networks, and orphans"
if [ "$KEEP_MODELS" = true ]; then
    run "${COMPOSE[@]}" -f "$COMPOSE_FILE" down --remove-orphans
else
    run "${COMPOSE[@]}" -f "$COMPOSE_FILE" down --volumes --remove-orphans
fi

step "Removing the previously built web image"
run_shell "docker image rm -f '$WEB_IMAGE' 2>/dev/null || true"

if [ "$NO_CACHE" = true ]; then
    step "Pruning the Docker build cache"
    run docker builder prune -f
fi

# --- 2. build ----------------------------------------------------------------

if [ "$NO_BUILD" = true ]; then
    warn "Skipping build (--no-build)"
else
    step "Building the Meridian Agent image"
    if [ "$NO_CACHE" = true ]; then
        run "${COMPOSE[@]}" -f "$COMPOSE_FILE" build --no-cache
    else
        run "${COMPOSE[@]}" -f "$COMPOSE_FILE" build
    fi
fi

# --- 3. deploy ---------------------------------------------------------------

step "Starting the stack (web + qdrant + osrm + overpass)"
run "${COMPOSE[@]}" -f "$COMPOSE_FILE" up -d --no-build

# --- 4. health ---------------------------------------------------------------

step "Waiting for http://localhost:${WEB_PORT}${HEALTH_PATH}"
if [ "$DRY_RUN" = false ]; then
    elapsed=0
    until curl -fsS "http://localhost:${WEB_PORT}${HEALTH_PATH}" >/dev/null 2>&1; do
        if [ "$elapsed" -ge "$HEALTH_TIMEOUT_SECONDS" ]; then
            echo
            warn "Health check did not pass within ${HEALTH_TIMEOUT_SECONDS}s. Recent logs:"
            if [ -z "${KILO_API_KEY:-}" ]; then
                warn "KILO_API_KEY is not set — the container logs that as 'AI:ApiKey is missing' and restarts."
            fi
            "${COMPOSE[@]}" -f "$COMPOSE_FILE" logs --tail=40 meridian-web || true
            die "Deployment is unhealthy. Inspect the logs above."
        fi
        sleep 3
        elapsed=$((elapsed + 3))
        printf '\r  waiting... %ss ' "$elapsed"
    done
    printf '\n'
else
    printf '  (dry run) would poll http://localhost:%s%s for %ss\n' "$WEB_PORT" "$HEALTH_PATH" "$HEALTH_TIMEOUT_SECONDS"
fi

# --- 5. report ---------------------------------------------------------------

step "Container status"
run "${COMPOSE[@]}" -f "$COMPOSE_FILE" ps

if [ "$DRY_RUN" = false ]; then
    ok "The Meridian Agent is up:"
    echo "  App:     http://localhost:${WEB_PORT}"
    echo "  Settings: http://localhost:${WEB_PORT}/profile/edit"
    echo "  Health:  http://localhost:${WEB_PORT}${HEALTH_PATH}"
    echo
    if [ "$KEEP_MODELS" = false ]; then
        warn "First run embeds the rulebooks against the OpenAI API. Watch progress with:"
        echo "  ${COMPOSE[*]} -f $COMPOSE_FILE logs -f meridian-web | grep -i indexing"
    fi
    echo "Useful:"
    echo "  ${COMPOSE[*]} -f $COMPOSE_FILE logs -f meridian-web   # follow app logs"
    echo "  ${COMPOSE[*]} -f $COMPOSE_FILE down --volumes         # full clean stop"
else
    ok "Dry run complete - nothing was changed."
fi

if [ "$FOLLOW_LOGS" = true ]; then
    step "Following meridian-web logs (Ctrl+C to stop)"
    run "${COMPOSE[@]}" -f "$COMPOSE_FILE" logs -f meridian-web
fi
