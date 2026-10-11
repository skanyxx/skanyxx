#!/usr/bin/env bash
# The Skanyxx appliance (D057, D195-D199): k3s + Helm + the umbrella chart on this Linux machine (amd64 or arm64,
# systemd). Run as root. The person running it never needs kubectl: everything Kubernetes happens in here.
#
#   install.sh [install] [--host NAME] [--image REF] [--tls-cert FILE --tls-key FILE]
#       First run: installs k3s and Helm (pinned), loads the Skanyxx image, installs the chart, waits until
#       https://NAME/health answers 200, prints the URL and the owner's one-time bootstrap token.
#       Again (same or newer bundle): an upgrade with the same settings. Data (PVCs) and generated Secrets are kept.
#   install.sh status              k3s, the release, the pods, /health. Prints no secret.
#   install.sh token               the bootstrap token again (if the first print was lost before /Setup).
#   install.sh uninstall           removes the release; data and Secrets stay, `install` brings it back.
#   install.sh uninstall --purge [--yes]   removes k3s and every byte of data (no undo).
#
# NAME is the DNS name people type (default: `hostname -f` when it has a dot). --image REF pulls from a registry;
# without it the image comes from skanyxx-image.tar next to this script (bundle.sh), else the one used last time.
# Without --tls-cert/--tls-key a self-signed certificate for NAME is generated once.
set -euo pipefail

K3S_VERSION=v1.36.5+k3s1
# sha256 of https://raw.githubusercontent.com/k3s-io/k3s/$K3S_VERSION/install.sh: the script is checked before it runs
# (it then checks the k3s binary against the release's own list). Bump both together.
K3S_INSTALL_SHA256=46177d4c99440b4c0311b67233823a8e8a2fc09693f6c89af1a7161e152fbfad
HELM_VERSION=v4.3.0
declare -A HELM_SHA256=(
  [amd64]=86584a54def73570558f66f5111cc53dfed56689637ae32c1201205d494f54fb
  [arm64]=31c5794dd55c66a51e6b7d2e2ac7a114ae8b1de41ff1d9ba51748ac973b06a08
)
RELEASE=skanyxx
NAMESPACE=skanyxx
DIR=$(cd "$(dirname "$0")" && pwd)
STATE=/etc/skanyxx
BIN=/opt/skanyxx/bin
HELM=$BIN/helm
FIREWALL=$BIN/skanyxx-firewall
K3S_IMAGES=/var/lib/rancher/k3s/agent/images
export KUBECONFIG=/etc/rancher/k3s/k3s.yaml

log() { printf '==> %s\n' "$*" >&2; }
TMP=''
trap '[[ -z $TMP ]] || rm -rf "$TMP"' EXIT
# One scratch directory per run, removed on any exit (a failed download included).
scratch() { [[ -n $TMP ]] || TMP=$(mktemp -d); }
die() { printf 'ERROR: %s\n' "$*" >&2; exit 1; }
kc() { k3s kubectl "$@"; }
# The system's iptables/ip6tables, else the copy k3s ships (the firewall uses whichever k3s itself would find first).
ipt() { command -v "$1" || echo "/var/lib/rancher/k3s/data/current/bin/aux/$1"; }

[[ $(id -u) == 0 ]] || die "run as root (sudo $0 $*)"
case $(uname -m) in
  x86_64) ARCH=amd64 ;;
  aarch64 | arm64) ARCH=arm64 ;;
  *) die "unsupported CPU $(uname -m): amd64 or arm64 only" ;;
esac

load_state() {
  HOST='' IMAGE='' TLS_SELF_SIGNED=''
  # shellcheck source=/dev/null
  [[ -f $STATE/appliance.env ]] && . "$STATE/appliance.env"
  return 0
}

save_state() {
  install -d -m 0700 "$STATE"
  (umask 077 && printf 'HOST=%q\nIMAGE=%q\nTLS_SELF_SIGNED=%q\n' "$HOST" "$IMAGE" "$TLS_SELF_SIGNED" >"$STATE/appliance.env")
}

# The chart: a packaged bundle (skanyxx-*.tgz next to this script) or the repo checkout (deploy/helm/skanyxx).
find_chart() {
  local tgz
  tgz=$(find "$DIR" -maxdepth 1 -name 'skanyxx-*.tgz' | sort | tail -1)
  if [[ -n $tgz ]]; then CHART=$tgz; return; fi
  CHART=$(cd "$DIR/../helm/skanyxx" 2>/dev/null && pwd) || die "no chart: put skanyxx-<version>.tgz next to $0 (bundle.sh)"
  [[ -d $CHART/charts ]] || "$HELM" dependency build "$CHART" >/dev/null
}

# The image named inside a `docker save` tarball (its first RepoTag).
tar_image() {
  tar -xOf "$1" manifest.json | grep -o '"RepoTags":\["[^"]*"' | head -1 | sed 's/.*\["//; s/"$//'
}

install_firewall() {
  install -d -m 0755 "$BIN"
  # D198: the k3s API (6443), the kubelet (10250) and flannel's VXLAN (8472/udp) answer this machine and its pods only.
  cat >"$FIREWALL" <<'EOF'
#!/bin/sh
# Written by the Skanyxx appliance installer (D198). Runs before k3s on every start.
set -eu
for name in iptables ip6tables; do
  bin=$(command -v "$name" || echo /var/lib/rancher/k3s/data/current/bin/aux/$name)
  if [ ! -x "$bin" ]; then
    [ "$name" = ip6tables ] && continue
    # The one tolerated failure: k3s's very first start, before it has unpacked its own iptables; install.sh applies
    # the rules right after that start. Any other failure exits non-zero, so systemd reports it and k3s stays down.
    echo "skanyxx-firewall: no $name yet (k3s not unpacked); install.sh applies the rules after this first start" >&2
    exit 0
  fi
  "$bin" -w -N SKANYXX-FW 2>/dev/null || "$bin" -w -F SKANYXX-FW
  "$bin" -w -A SKANYXX-FW -i lo -j RETURN
  "$bin" -w -A SKANYXX-FW -i cni0 -j RETURN
  "$bin" -w -A SKANYXX-FW -p tcp -m multiport --dports 6443,10250 -j DROP
  "$bin" -w -A SKANYXX-FW -p udp --dport 8472 -j DROP
  "$bin" -w -C INPUT -j SKANYXX-FW 2>/dev/null || "$bin" -w -I INPUT 1 -j SKANYXX-FW
done
EOF
  chmod 0755 "$FIREWALL"
  install -d -m 0755 /etc/systemd/system/k3s.service.d
  # No `-`: a firewall that cannot be applied fails the start (fail closed, visible in systemctl status k3s); the
  # script itself tolerates only k3s's first start, before its iptables exist.
  printf '[Service]\nExecStartPre=%s\n' "$FIREWALL" >/etc/systemd/system/k3s.service.d/skanyxx-firewall.conf
  systemctl daemon-reload
}

install_k3s() {
  install -d -m 0755 /etc/rancher/k3s
  local config
  config=$(printf 'write-kubeconfig-mode: "0600"\nsecrets-encryption: true\n')
  local changed=0
  [[ $(cat /etc/rancher/k3s/config.yaml 2>/dev/null) == "$config" ]] || { printf '%s\n' "$config" >/etc/rancher/k3s/config.yaml; changed=1; }
  if [[ $(k3s --version 2>/dev/null | head -1) == *"$K3S_VERSION"* ]] && systemctl is-active --quiet k3s; then
    if ((changed)); then log "k3s config changed: restarting k3s"; systemctl restart k3s; else log "k3s $K3S_VERSION already running"; fi
  else
    log "installing k3s $K3S_VERSION"
    # The install script of that exact release, pinned by hash and run only once it is whole and verified.
    scratch
    curl -sfL -o "$TMP/k3s-install.sh" "https://raw.githubusercontent.com/k3s-io/k3s/$K3S_VERSION/install.sh" \
      || die "k3s install script: download failed"
    echo "$K3S_INSTALL_SHA256  $TMP/k3s-install.sh" | sha256sum -c --quiet - || die "k3s install script: sha256 mismatch"
    INSTALL_K3S_VERSION=$K3S_VERSION INSTALL_K3S_SKIP_SELINUX_RPM=true sh "$TMP/k3s-install.sh" server >&2
  fi
  "$FIREWALL"
  log "waiting for the node"
  local i
  for i in $(seq 120); do
    [[ $(kc get nodes 2>/dev/null) == *' Ready '* ]] && break
    ((i < 120)) || die "k3s node not Ready after 10 minutes (journalctl -u k3s)"
    sleep 5
  done
}

install_helm() {
  if [[ -x $HELM && $("$HELM" version --template '{{.Version}}' 2>/dev/null) == "$HELM_VERSION" ]]; then return; fi
  log "installing helm $HELM_VERSION"
  scratch
  curl -sfL -o "$TMP/helm.tgz" "https://get.helm.sh/helm-$HELM_VERSION-linux-$ARCH.tar.gz" || die "helm download failed"
  echo "${HELM_SHA256[$ARCH]}  $TMP/helm.tgz" | sha256sum -c --quiet - || die "helm download: sha256 mismatch"
  tar -xzf "$TMP/helm.tgz" -C "$TMP"
  install -d -m 0755 "$BIN"
  install -m 0755 "$TMP/linux-$ARCH/helm" "$HELM"
}

load_image() {
  local tar=$DIR/skanyxx-image.tar
  [[ -f $tar ]] || return 0
  [[ $(tar_image "$tar") == "$IMAGE" ]] || return 0
  log "loading $IMAGE from $(basename "$tar")"
  # Also into k3s's images dir: re-imported (and pinned) on every k3s start, so image GC never strands the pod.
  install -d -m 0755 "$K3S_IMAGES"
  install -m 0600 "$tar" "$K3S_IMAGES/skanyxx-image.tar"
  k3s ctr -n k8s.io images import "$K3S_IMAGES/skanyxx-image.tar" >/dev/null
}

# Our own certificate still names HOST and is good for 30 more days (else install/upgrade generates a new one).
self_signed_current() {
  [[ $(openssl x509 -in "$STATE/tls.crt" -noout -subject) == *"CN = $HOST"* ]] \
    && openssl x509 -in "$STATE/tls.crt" -noout -checkend 2592000 >/dev/null
}

ensure_tls() {
  if [[ -n $TLS_CERT ]]; then
    install -m 0600 "$TLS_CERT" "$STATE/tls.crt"
    install -m 0600 "$TLS_KEY" "$STATE/tls.key"
    TLS_SELF_SIGNED=0
  elif [[ ! -f $STATE/tls.crt ]] || { [[ $TLS_SELF_SIGNED == 1 ]] && ! self_signed_current; }; then
    log "generating a self-signed certificate for $HOST (replace it: --tls-cert/--tls-key)"
    (umask 077 && openssl req -x509 -newkey ec -pkeyopt ec_paramgen_curve:P-256 -nodes -days 825 \
      -subj "/CN=$HOST" -addext "subjectAltName=DNS:$HOST" -keyout "$STATE/tls.key" -out "$STATE/tls.crt" 2>/dev/null)
    TLS_SELF_SIGNED=1
  fi
  # Server-side apply: a client-side apply would copy the key into the last-applied-configuration annotation.
  kc -n "$NAMESPACE" create secret tls skanyxx-tls --cert="$STATE/tls.crt" --key="$STATE/tls.key" --dry-run=client -o yaml \
    | kc apply --server-side --force-conflicts --field-manager=skanyxx-appliance -f - >/dev/null
}

wait_health() {
  local i code redirect
  for i in $(seq 60); do
    code=$(curl -sk -o /dev/null -w '%{http_code}' --resolve "$HOST:443:127.0.0.1" "https://$HOST/health" || true)
    [[ $code == 200 ]] && break
    ((i < 60)) || die "https://$HOST/health answered $code after 5 minutes ($0 status)"
    sleep 5
  done
  # D196: plain http must send people to https (a permanent redirect to the same host), never serve or 404.
  redirect=$(curl -s -o /dev/null -w '%{http_code} %{redirect_url}' --resolve "$HOST:80:127.0.0.1" "http://$HOST/Setup" || true)
  [[ $redirect == "301 https://$HOST/Setup" || $redirect == "308 https://$HOST/Setup" ]] \
    || die "http://$HOST/Setup answered '$redirect', not a redirect to https://$HOST/Setup ($0 status)"
}

cmd_install() {
  local host='' image=''
  TLS_CERT='' TLS_KEY=''
  while (($#)); do
    case $1 in
      --host) host=${2:?--host NAME}; shift 2 ;;
      --image) image=${2:?--image REF}; shift 2 ;;
      --tls-cert) TLS_CERT=${2:?--tls-cert FILE}; shift 2 ;;
      --tls-key) TLS_KEY=${2:?--tls-key FILE}; shift 2 ;;
      *) die "unknown option $1" ;;
    esac
  done
  [[ -n $TLS_CERT && -n $TLS_KEY || -z $TLS_CERT && -z $TLS_KEY ]] || die "--tls-cert and --tls-key go together"
  [[ -z $TLS_CERT || ( -r $TLS_CERT && -r $TLS_KEY ) ]] || die "cannot read $TLS_CERT / $TLS_KEY"
  command -v systemctl >/dev/null || die "systemd is required"
  command -v openssl >/dev/null || [[ -n $TLS_CERT ]] || die "openssl is required for the self-signed certificate (or pass --tls-cert/--tls-key)"

  load_state
  HOST=${host:-$HOST}
  if [[ -z $HOST ]]; then
    HOST=$(hostname -f 2>/dev/null || hostname)
    [[ $HOST == *.* ]] || die "this machine's name ($HOST) is not a DNS name people can type: pass --host NAME"
  fi
  HOST=${HOST,,}
  [[ $HOST =~ ^([a-z0-9]([a-z0-9-]*[a-z0-9])?\.)+[a-z0-9]([a-z0-9-]*[a-z0-9])?$ ]] || die "--host $HOST is not a DNS name"
  if [[ -n $image ]]; then
    IMAGE=$image
  elif [[ -f $DIR/skanyxx-image.tar ]]; then
    IMAGE=$(tar_image "$DIR/skanyxx-image.tar")
    [[ -n $IMAGE ]] || die "$DIR/skanyxx-image.tar names no image (docker save <repo>:<tag>)"
  fi
  [[ -n $IMAGE ]] || die "no image: pass --image REF or put skanyxx-image.tar next to $0"
  [[ ${IMAGE##*/} == *:* && $IMAGE != *@* ]] || die "--image $IMAGE needs a tag (repository:tag)"
  # A reference, nothing else: it goes into --set-string, where a comma or '=' would become another value.
  [[ $IMAGE =~ ^[a-z0-9]([a-z0-9._/:-]*[a-z0-9])?:[A-Za-z0-9_][A-Za-z0-9._-]{0,127}$ ]] || die "--image $IMAGE is not an image reference"

  install_firewall
  install_k3s
  install_helm
  find_chart
  save_state
  load_image

  kc create namespace "$NAMESPACE" --dry-run=client -o yaml | kc apply -f - >/dev/null
  log "waiting for Traefik (k3s installs it)"
  local i
  for i in $(seq 120); do
    kc get crd middlewares.traefik.io >/dev/null 2>&1 && break
    ((i < 120)) || die "Traefik's CRDs did not appear in 10 minutes (k3s kubectl -n kube-system get pods)"
    sleep 5
  done
  kc apply -f "$DIR/traefik.yaml" >/dev/null
  ensure_tls
  save_state

  log "helm upgrade --install $RELEASE ($(basename "$CHART"), image $IMAGE, https://$HOST)"
  "$HELM" upgrade --install "$RELEASE" "$CHART" -n "$NAMESPACE" -f "$DIR/values.yaml" \
    --set-string "skanyxx.image.repository=${IMAGE%:*}" --set-string "skanyxx.image.tag=${IMAGE##*:}" \
    --set-string "ingress.hosts[0]=$HOST" \
    --set-string "ingress.tls[0].secretName=skanyxx-tls" --set-string "ingress.tls[0].hosts[0]=$HOST" \
    --wait=watcher --timeout 20m >/dev/null
  wait_health

  printf '\nSkanyxx is up: https://%s\n' "$HOST"
  ((TLS_SELF_SIGNED)) && printf '(self-signed certificate: browsers warn until you install a real one with --tls-cert/--tls-key)\n'
  if [[ ! -f $STATE/token-shown ]]; then
    # Read first: inside printf's arguments a failed read would print nothing and still mark the token as shown.
    local token=''
    token=$(bootstrap_token) || true
    if [[ -n $token ]]; then
      printf '\nCreate the owner at https://%s/Setup with this one-time bootstrap token (shown once; keep it safe):\n  %s\n' \
        "$HOST" "$token"
      (umask 077 && : >"$STATE/token-shown")
    else
      log "WARNING: could not read the owner bootstrap token; '$0 token' prints it (the next install run shows it too)"
    fi
  else
    printf '(the owner bootstrap token was shown on the first run; %s token shows it again)\n' "$0"
  fi
}

bootstrap_token() {
  kc -n "$NAMESPACE" get secret "$RELEASE" -o jsonpath='{.data.bootstrap-token}' | base64 -d
}

cmd_status() {
  load_state
  printf 'k3s:     %s (%s)\n' "$(k3s --version 2>/dev/null | head -1 | awk '{print $3}')" "$(systemctl is-active k3s 2>/dev/null || true)"
  printf 'helm:    %s\n' "$("$HELM" version --template '{{.Version}}' 2>/dev/null || echo missing)"
  printf 'url:     https://%s\n' "${HOST:-?}"
  printf 'image:   %s\n' "${IMAGE:-?}"
  printf 'health:  %s\n' "$(curl -sk -o /dev/null -w '%{http_code}' --resolve "$HOST:443:127.0.0.1" "https://$HOST/health" || true)"
  firewall_status
  echo
  kc get nodes -o wide 2>/dev/null || true
  echo
  "$HELM" list -n "$NAMESPACE" 2>/dev/null || true
  echo
  kc -n "$NAMESPACE" get pods,pvc 2>/dev/null || true
}

# D198 as it stands now, per family: where the SKANYXX-FW jump sits in INPUT and its 2 DROP rules (6443+10250/tcp,
# 8472/udp). k3s inserts its own KUBE-* jumps above it on every start, which is fine; anything else above it (ufw,
# firewalld, a hand-made ACCEPT) may accept those ports before SKANYXX-FW sees them, so it is a warning.
firewall_status() {
  local name bin rules pos drops above
  for name in iptables ip6tables; do
    bin=$(ipt "$name")
    [[ -x $bin ]] || { printf 'firewall: %-9s not present\n' "$name"; continue; }
    rules=$("$bin" -w -S INPUT 2>/dev/null | grep -- '^-A INPUT' || true)
    pos=$(grep -n -x -- '-A INPUT -j SKANYXX-FW' <<<"$rules" | head -1 | cut -d: -f1 || true)
    drops=$("$bin" -w -S SKANYXX-FW 2>/dev/null | grep -c -- '-j DROP' || true)
    if [[ -z $pos ]]; then
      printf 'firewall: %-9s WARNING: no SKANYXX-FW jump in INPUT (%s DROP rules); 6443/10250/8472 are open (re-run %s)\n' "$name" "$drops" "$0"
      continue
    fi
    above=$(head -n $((pos - 1)) <<<"$rules" | grep -v -- '-j KUBE-' || true)
    if [[ $drops != 2 ]]; then
      printf 'firewall: %-9s WARNING: SKANYXX-FW holds %s DROP rules, not 2 (re-run %s)\n' "$name" "$drops" "$0"
    elif [[ -n $above ]]; then
      printf 'firewall: %-9s WARNING: jump is INPUT rule %s, below rules that may accept first (ufw/firewalld?):\n%s\n' \
        "$name" "$pos" "            ${above//$'\n'/$'\n'            }"
    else
      printf 'firewall: %-9s ok: jump is INPUT rule %s (only k3s rules above), 2 DROP rules (6443+10250/tcp, 8472/udp)\n' "$name" "$pos"
    fi
  done
}

cmd_uninstall() {
  local purge=0 yes=0
  while (($#)); do
    case $1 in
      --purge) purge=1; shift ;;
      --yes) yes=1; shift ;;
      *) die "unknown option $1" ;;
    esac
  done
  if ((!purge)); then
    "$HELM" uninstall "$RELEASE" -n "$NAMESPACE" --wait >/dev/null
    log "release removed. Data (volumes) and generated Secrets are kept: '$0 install' brings Skanyxx back with them."
    log "To erase everything, including k3s: $0 uninstall --purge"
    return
  fi
  if ((!yes)); then
    read -r -p "Erase Skanyxx, k3s and ALL data on this machine? Type 'erase' to confirm: " answer
    [[ $answer == erase ]] || die "not confirmed"
  fi
  # The firewall first: without a system iptables, k3s-uninstall.sh deletes the only binary that can remove it.
  local name bin
  for name in iptables ip6tables; do
    bin=$(ipt "$name")
    [[ -x $bin ]] || continue
    "$bin" -w -D INPUT -j SKANYXX-FW 2>/dev/null || true
    { "$bin" -w -F SKANYXX-FW && "$bin" -w -X SKANYXX-FW; } 2>/dev/null || true
  done
  rm -f /etc/systemd/system/k3s.service.d/skanyxx-firewall.conf
  if [[ -x /usr/local/bin/k3s-uninstall.sh ]]; then /usr/local/bin/k3s-uninstall.sh >&2; fi
  rmdir /etc/systemd/system/k3s.service.d 2>/dev/null || true
  systemctl daemon-reload
  rm -rf "$STATE" /opt/skanyxx
  log "erased: k3s, its data and volumes, $STATE, /opt/skanyxx"
}

cmd=${1:-install}
[[ $# -gt 0 ]] && shift
case $cmd in
  install) cmd_install "$@" ;;
  status) cmd_status ;;
  token) bootstrap_token; echo ;;
  uninstall) cmd_uninstall "$@" ;;
  --*) cmd_install "$cmd" "$@" ;;
  *) die "usage: $0 [install|status|token|uninstall] (see the header of this script)" ;;
esac
