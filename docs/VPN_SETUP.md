# Bringing your own VPN endpoint

Flint runs the tunnel **on the television** (ADR-0022). Windows is only where a WireGuard
config is pasted, because a D-pad cannot reasonably enter one. Nothing about the tunnel is
relayed through a Flint service, and the config text is opaque to Flint — stored per profile
in the Android Keystore on the TV, never logged, never sent anywhere else.

This document is for the person standing up the *other* end of that tunnel.

## What Flint will and will not accept

Flint admits a config, then rewrites it before the tunnel goes up.

| Rule | Why |
|---|---|
| `[Interface]` and `[Peer]` sections must both be present | Structural gate before any tunnel work |
| Config ≤ 64 KiB | Bounded storage per profile |
| Every peer needs an `Endpoint` and at least one `AllowedIPs` | A peer with neither cannot carry traffic; failing here is far easier to read than failing inside the Go backend |
| No `ExcludedApplications`; `IncludedApplications`, if present, must be Flint alone | Flint cannot honestly claim what a tunnel does for other apps on the TV |
| Full tunnel (`0.0.0.0/0`) is fine | It is the normal case |
| The TV's own LAN is carved out of `AllowedIPs` before the tunnel starts | Cast, pairing and the browser control session all run over Wi-Fi. A tunnel that swallowed the LAN would drop the session that asked for it |
| A route covering **only** the TV's LAN is rejected | That is a LAN-only tunnel, and after carve-out it routes nothing |

After the backend reports the tunnel up, Flint does **not** call it connected. It waits for
Android to report a validated VPN network, and tears the tunnel down if that never arrives.
This is not a packet kill switch and is not described as one.

## Standing up a server

`scripts/provision-wireguard-server.ps1` builds an endpoint on a host you already have SSH
access to, and writes a Flint-ready client config locally.

~~~powershell
.\scripts\provision-wireguard-server.ps1 `
    -HostAddress 141.147.109.67 `
    -User ubuntu `
    -ClientName firetv
~~~

It installs `wireguard-tools`, enables IPv4 forwarding, generates a server key (once) and a
fresh peer key, opens the host firewall for the listen port, and starts `wg-quick@wg0`.
Peers are kept one per file under `/etc/wireguard/peers/`, so re-running for a second
television cannot corrupt the first one's entry. Re-running for the *same* `-ClientName`
reissues that peer's keys and keeps its tunnel address.

The tunnel range defaults to `10.77.0.0/24`, deliberately not `10.0.0.0/24` — the latter is
the default OCI VCN subnet, and a tunnel address inside the server's own subnet does not
route. `BrowserVpnProvisionedConfigTest` pins that choice.

### Memory on Always Free shapes

The OCI Always Free instances have 1 GB of RAM, and the Oracle Linux images ship with no swap.
Resolving EPEL metadata is enough to exhaust that, and it does not fail cleanly — the box
thrashes until `sshd` can no longer complete a banner exchange, and the only way back is a
reboot from the cloud console. The script now adds a 1 GB swapfile before installing anything
whenever it finds under 2 GB of RAM and no swap.

If you hit this before that guard existed: *Compute → Instances → your instance → Reboot*
(force reboot if the graceful one does not take), then re-run the script.

### Two things the script cannot do

1. **The cloud provider's firewall.** The host firewall is not the only one. On OCI the VCN
   security list also has to allow the traffic:
   *Networking → Virtual Cloud Networks → your VCN → Security Lists → Default → Add Ingress
   Rule*, source `0.0.0.0/0`, protocol **UDP**, destination port **51820**.
   Without this the handshake never arrives and it looks like a client fault.
2. **Android's VPN consent.** The system prompt on the television requires a person. Flint
   surfaces `NeedsConsent` and waits; it never bypasses `VpnService.prepare`.

## Turning it on

1. Windows app → **Web** → **Network**: paste the config, tick **Enable VPN**, **Save**.
2. On the TV, grant the VPN consent prompt.
3. Confirm a real handshake, not just a UI state:

~~~bash
ssh ubuntu@<host> 'sudo wg show wg0 latest-handshakes'
~~~

A non-zero timestamp is the proof. Until then the tunnel is not carrying traffic, whatever
any screen says.

## This is not a product feature

The script is an operator tool for a host you own. It is not shipped with the Windows
installer and must not be: it would mean distributing an SSH private key, and pointing every
user's traffic at one machine that is then, in practice, their ISP. What ships is the paste
box — users bring a config from their own provider or their own server, the same way
WireGuard's own Android app works.
