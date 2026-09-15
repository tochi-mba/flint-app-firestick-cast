#Requires -Version 5.1
<#
.SYNOPSIS
    Provisions a WireGuard endpoint on a cloud host and emits a Flint-ready client config.

.DESCRIPTION
    Flint runs the tunnel on the television (ADR-0022): the Windows app is only where a
    WireGuard config is pasted, because a D-pad cannot reasonably enter one. This script
    builds the other end of that tunnel — a server the TV can dial — and prints the exact
    config text to paste into Web -> Network.

    Idempotent. Re-running with the same -ClientName reissues that peer's keys; a different
    -ClientName adds a peer and leaves existing ones alone.

    Two things this script cannot do for you, both outside the host:
      * The cloud provider's own firewall. On OCI that is the VCN security list; the
        ingress rule is UDP <ListenPort> from 0.0.0.0/0. The script says so again at the end.
      * Granting the VPN consent prompt on the television. Android requires a person.

.PARAMETER HostAddress
    Public IPv4 (or DNS name) of the server. This becomes the peer Endpoint on the TV.

.PARAMETER User
    SSH user. Oracle Linux images default to 'opc'; Ubuntu images to 'ubuntu'.

.PARAMETER SshKey
    Optional path to the private key. Omit to use the ssh agent / default identity.

.PARAMETER ListenPort
    UDP port WireGuard listens on. Must match the cloud firewall rule.

.PARAMETER TunnelNetwork
    /24 the tunnel hands out. Deliberately not 10.0.0.0/24: that is the default OCI VCN
    subnet, and a tunnel address colliding with the server's own subnet does not route.

.PARAMETER ClientName
    Peer name. One config per television.

.PARAMETER OutFile
    Where to write the client config locally. Defaults to artifacts/wireguard/<ClientName>.conf.

.NOTES
    The client config contains a private key. It is written with restrictive ACLs and is
    never echoed to the console in full unless -ShowConfig is passed.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$HostAddress,

    [string]$User = 'ubuntu',

    [string]$SshKey,

    [ValidateRange(1, 65535)]
    [int]$ListenPort = 51820,

    [string]$TunnelNetwork = '10.77.0',

    [ValidatePattern('^[a-zA-Z0-9_-]{1,32}$')]
    [string]$ClientName = 'firetv',

    [string]$OutFile,

    [switch]$ShowConfig
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $OutFile) {
    $OutFile = Join-Path $repoRoot "artifacts/wireguard/$ClientName.conf"
}

$sshArgs = @('-o', 'StrictHostKeyChecking=accept-new', '-o', 'ConnectTimeout=15')
if ($SshKey) {
    if (-not (Test-Path -LiteralPath $SshKey)) { throw "SSH key not found: $SshKey" }
    $sshArgs += @('-i', $SshKey)
}
$target = "$User@$HostAddress"

Write-Host "Provisioning WireGuard on $target (udp/$ListenPort)" -ForegroundColor Cyan

# The remote script is deliberately one heredoc: a half-applied firewall or a wg0 that is
# up without forwarding is worse than a clean failure, so every step runs under `set -e`.
$remote = @'
set -euo pipefail
umask 077

PORT="__PORT__"
NET="__NET__"
PEER="__PEER__"
SERVER_ADDR="${NET}.1"
WG=/etc/wireguard

if [ "$(id -u)" -ne 0 ]; then SUDO="sudo"; else SUDO=""; fi

# --- swap -----------------------------------------------------------------
# OCI Always Free shapes have 1 GB of RAM and, on the Oracle Linux images, no swap at all.
# Resolving EPEL metadata is enough to exhaust that, and the failure mode is not an error: the
# box thrashes until sshd can no longer complete a banner exchange and the instance has to be
# rebooted from the cloud console. A small swapfile costs nothing and avoids it entirely.
MEM_MB="$(awk '/MemTotal/ {print int($2/1024)}' /proc/meminfo)"
SWAP_MB="$(awk '/SwapTotal/ {print int($2/1024)}' /proc/meminfo)"
if [ "$MEM_MB" -lt 2048 ] && [ "$SWAP_MB" -lt 512 ] && [ ! -f /swapfile ]; then
    echo "Low memory (${MEM_MB} MB, no swap) — adding a 1 GB swapfile before installing." >&2
    $SUDO fallocate -l 1G /swapfile 2>/dev/null || $SUDO dd if=/dev/zero of=/swapfile bs=1M count=1024 status=none
    $SUDO chmod 600 /swapfile
    $SUDO mkswap /swapfile >/dev/null
    $SUDO swapon /swapfile
    if ! grep -q '^/swapfile' /etc/fstab 2>/dev/null; then
        echo '/swapfile none swap sw 0 0' | $SUDO tee -a /etc/fstab >/dev/null
    fi
fi

# --- packages -------------------------------------------------------------
if command -v apt-get >/dev/null 2>&1; then
    export DEBIAN_FRONTEND=noninteractive
    $SUDO apt-get update -qq
    $SUDO apt-get install -y -qq wireguard wireguard-tools iptables-persistent >/dev/null
    PKG=apt
elif command -v dnf >/dev/null 2>&1; then
    # Oracle Linux / RHEL keep wireguard-tools in EPEL. The kernel module itself is already
    # in UEK and RHEL 9 kernels, so only the userspace tools are missing.
    if ! command -v wg >/dev/null 2>&1; then
        EL="$(. /etc/os-release && echo "${VERSION_ID%%.*}")"
        $SUDO dnf install -y -q "oracle-epel-release-el${EL}" >/dev/null 2>&1 \
            || $SUDO dnf install -y -q epel-release >/dev/null 2>&1 \
            || true
        $SUDO dnf install -y -q wireguard-tools >/dev/null
    fi
    PKG=dnf
else
    echo "FLINT_ERROR: no supported package manager (need apt-get or dnf)" >&2
    exit 1
fi

# --- forwarding -----------------------------------------------------------
echo 'net.ipv4.ip_forward=1' | $SUDO tee /etc/sysctl.d/99-flint-wireguard.conf >/dev/null
$SUDO sysctl -q -w net.ipv4.ip_forward=1

WAN="$(ip -4 route show default | awk '{print $5; exit}')"
if [ -z "$WAN" ]; then
    echo "FLINT_ERROR: could not determine the default route interface" >&2
    exit 1
fi

# --- firewall -------------------------------------------------------------
# Decided before wg0.conf is written, because the two are the same decision: whoever owns
# the firewall owns forwarding and NAT, and wg0.conf's PostUp must not duplicate it.
#
# The host firewall matters as much as the cloud one. Oracle Linux ships firewalld active
# with only ssh open; without this the handshake never arrives and it reads as a client fault.
if command -v firewall-cmd >/dev/null 2>&1 && $SUDO firewall-cmd --state >/dev/null 2>&1; then
    # firewalld owns it. Raw iptables rules on top would need the iptables binary, which
    # Oracle Linux 9 does not install by default, and wg-quick would fail at PostUp.
    $SUDO firewall-cmd --permanent --add-port="${PORT}/udp" >/dev/null
    $SUDO firewall-cmd --permanent --add-masquerade >/dev/null
    $SUDO firewall-cmd --permanent --zone=trusted --add-interface=wg0 >/dev/null 2>&1 || true
    $SUDO firewall-cmd --reload >/dev/null
    FORWARD_UP=""
    FORWARD_DOWN=""
else
    # Return traffic for tunnel clients has to be forwarded and source-natted onto the public
    # interface, and torn down again with the tunnel.
    FORWARD_UP="iptables -A FORWARD -i wg0 -j ACCEPT; iptables -A FORWARD -o wg0 -j ACCEPT; iptables -t nat -A POSTROUTING -s ${NET}.0/24 -o ${WAN} -j MASQUERADE"
    FORWARD_DOWN="$(printf '%s' "$FORWARD_UP" | sed 's/ -A / -D /g')"
    if ! $SUDO iptables -C INPUT -p udp --dport "$PORT" -j ACCEPT 2>/dev/null; then
        $SUDO iptables -I INPUT 1 -p udp --dport "$PORT" -j ACCEPT
    fi
    if [ "$PKG" = "apt" ]; then
        $SUDO mkdir -p /etc/iptables
        $SUDO sh -c 'iptables-save > /etc/iptables/rules.v4'
    fi
fi

# --- keys -----------------------------------------------------------------
$SUDO mkdir -p "$WG"
if [ ! -f "$WG/server.key" ]; then
    wg genkey | $SUDO tee "$WG/server.key" >/dev/null
    $SUDO chmod 600 "$WG/server.key"
fi
SERVER_PRIV="$($SUDO cat "$WG/server.key")"
SERVER_PUB="$(printf '%s' "$SERVER_PRIV" | wg pubkey)"

CLIENT_PRIV="$(wg genkey)"
CLIENT_PUB="$(printf '%s' "$CLIENT_PRIV" | wg pubkey)"
PSK="$(wg genpsk)"

# --- tunnel address -------------------------------------------------------
# Reuse this peer's existing address if it has one, so reissuing keys for a television does
# not move it; otherwise take the lowest free host in the range. Two televisions sharing an
# address is the kind of fault that looks like a flaky tunnel rather than a config error.
# Reads go through `sudo sh -c` rather than `sudo sed`: /etc/wireguard is mode 700, so a glob
# expanded by this (unprivileged) shell would silently match nothing.
$SUDO mkdir -p "$WG/peers"
SLOT_OF='s#^AllowedIPs = .*\.\([0-9]\+\)/32.*#\1#p'
CLIENT_HOST="$($SUDO sh -c "cat '$WG/peers/${PEER}.peer' 2>/dev/null" | sed -n "$SLOT_OF" | head -n1)"
if [ -z "$CLIENT_HOST" ]; then
    TAKEN="$($SUDO sh -c "cat $WG/peers/*.peer 2>/dev/null" | sed -n "$SLOT_OF" | sort -n | uniq)"
    CLIENT_HOST=""
    for candidate in $(seq 2 254); do
        if ! printf '%s\n' "$TAKEN" | grep -qx "$candidate"; then
            CLIENT_HOST="$candidate"
            break
        fi
    done
fi
if [ -z "$CLIENT_HOST" ]; then
    echo "FLINT_ERROR: no free address left in ${NET}.0/24" >&2
    exit 1
fi
CLIENT_ADDR="${NET}.${CLIENT_HOST}"

# --- server config --------------------------------------------------------
# One file per peer, and wg0.conf assembled from the interface plus everything in peers/.
# Re-running for a second television then cannot corrupt the first one's entry, which a
# single rewritten file makes very easy to do.
$SUDO tee "$WG/peers/${PEER}.peer" >/dev/null <<PEERCONF
[Peer]
PublicKey = ${CLIENT_PUB}
PresharedKey = ${PSK}
AllowedIPs = ${CLIENT_ADDR}/32
PEERCONF
$SUDO chmod 600 "$WG/peers/${PEER}.peer"

$SUDO tee "$WG/wg0.conf" >/dev/null <<CONF
[Interface]
Address = ${SERVER_ADDR}/24
ListenPort = ${PORT}
PrivateKey = ${SERVER_PRIV}
CONF

# Only where this script owns forwarding. Under firewalld these lines would be a second,
# conflicting NAT path — and an empty PostUp is a command wg-quick would still try to run.
if [ -n "$FORWARD_UP" ]; then
    $SUDO sh -c "printf 'PostUp = %s\nPostDown = %s\n' '$FORWARD_UP' '$FORWARD_DOWN' >> $WG/wg0.conf"
fi

# The whole append runs as root for the same reason the reads do: the glob has to be expanded
# by a shell that can list a mode-700 directory, or every peer is silently dropped.
$SUDO sh -c "for f in $WG/peers/*.peer; do [ -e \"\$f\" ] || continue; printf '\n'; cat \"\$f\"; done >> $WG/wg0.conf"
$SUDO chmod 600 "$WG/wg0.conf"

# --- bring it up ----------------------------------------------------------
$SUDO systemctl enable wg-quick@wg0 >/dev/null 2>&1 || true
if $SUDO systemctl is-active --quiet wg-quick@wg0; then
    $SUDO systemctl restart wg-quick@wg0
else
    $SUDO systemctl start wg-quick@wg0
fi

$SUDO wg show wg0 >/dev/null

# --- emit the client config ----------------------------------------------
# AllowedIPs is a full tunnel. Flint admits it and carves the television's own LAN back
# out before the tunnel goes up, so cast and browser control stay reachable on Wi-Fi.
cat <<CLIENTCONF
FLINT_CONFIG_BEGIN
[Interface]
PrivateKey = ${CLIENT_PRIV}
Address = ${CLIENT_ADDR}/32
DNS = 9.9.9.9

[Peer]
PublicKey = ${SERVER_PUB}
PresharedKey = ${PSK}
AllowedIPs = 0.0.0.0/0
Endpoint = __ENDPOINT__:${PORT}
PersistentKeepalive = 25
FLINT_CONFIG_END
CLIENTCONF
'@

$remote = $remote.Replace('__PORT__', "$ListenPort").
    Replace('__NET__', $TunnelNetwork).
    Replace('__PEER__', $ClientName).
    Replace('__ENDPOINT__', $HostAddress)

$output = $remote | & ssh @sshArgs $target 'bash -s' 2>&1
$exit = $LASTEXITCODE

$text = ($output | Out-String)
if ($exit -ne 0 -or $text -notmatch 'FLINT_CONFIG_BEGIN') {
    Write-Host $text
    throw "Provisioning failed (ssh exit $exit). Nothing was written locally."
}

$config = ($text -split 'FLINT_CONFIG_BEGIN')[1]
$config = ($config -split 'FLINT_CONFIG_END')[0].Trim()

$outDir = Split-Path -Parent $OutFile
if (-not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
Set-Content -LiteralPath $OutFile -Value $config -Encoding ASCII

# The file holds a private key. Strip inheritance and leave only this user on it.
try {
    & icacls $OutFile /inheritance:r /grant:r "$($env:USERNAME):(R,W)" | Out-Null
} catch {
    Write-Warning "Could not tighten ACLs on $OutFile - review its permissions yourself."
}

Write-Host ""
Write-Host "Server is up. Client config written to:" -ForegroundColor Green
Write-Host "  $OutFile"
if ($ShowConfig) {
    Write-Host ""
    Write-Host $config
}

Write-Host ""
Write-Host "Still to do, in order:" -ForegroundColor Yellow
Write-Host "  1. Cloud firewall: allow ingress UDP $ListenPort from 0.0.0.0/0."
Write-Host "     OCI: Networking > VCN > Security Lists > Default > Add Ingress Rule."
Write-Host "     Source 0.0.0.0/0, IP Protocol UDP, Destination Port Range $ListenPort."
Write-Host "  2. Windows app > Web > Network: paste the config, tick Enable VPN, Save."
Write-Host "  3. On the TV, grant Android's VPN consent prompt. Nobody can grant it for you."
Write-Host "  4. Confirm the handshake:"
Write-Host "     ssh $target 'sudo wg show wg0 latest-handshakes'"
Write-Host "     A non-zero timestamp is the proof. Until then the tunnel is not carrying traffic."
