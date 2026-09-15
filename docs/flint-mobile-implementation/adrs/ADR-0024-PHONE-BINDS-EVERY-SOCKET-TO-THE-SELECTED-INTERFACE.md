# ADR-0024 — the phone binds every socket to the selected tether interface

- **Status:** Accepted
- **Implementation:** Planned
- **Date:** 2026-09-11
- **Scope:** Flint Mobile
- **Deciders:** Flint engineering
- **Owners:** Slice 02 (transport and interface binding), Slice 03 (discovery ladder)
- **Related:** ADR-0025, ADR-0026, ADR-0027, ADR-0028, ADR-0029, ADR-0030, and Fire TV browser
  ADR-0004 on non-secret endpoint discovery
- **Supersedes:** None
- **Superseded by:** None

## Context

The phone-host app inverts the topology the desktop host was written for. The expected setup is a
phone running its own hotspot with the television joined to it, and the phone's upstream is mobile
data — that is the point, because it needs no existing Wi-Fi and no router the user controls. It
is also the configuration in which obvious socket code silently does the wrong thing.

While the phone tethers from mobile data, the process default network is cellular. Java takes an
unbound socket's source address from the default route, so that socket leaves over the WAN. A probe
aimed at `192.168.43.7` reaches the carrier rather than the television two metres away; it finds
neither the TV nor anything else useful, it is metered, and where the carrier uses private address
space it is aimed at somebody else's host. The failure is silent — sockets open, writes succeed,
and discovery finds nothing.

`ConnectivityManager.bindProcessToNetwork` cannot fix this. An app may only bind to a `Network` the
framework publishes, and a tether interface is not one: tethering is administered through
`TetheringManager`, a system API, and the local side is never exposed as a `Network`. There is
nothing to pass. It would also bind unrelated process traffic, which `AGENTS.md` forbids alongside
changing the default route.

This repository already recognised the trap. The KDoc on `protocol/dlna/Ssdp.kt` warns that the UPnP
group is fixed by specification but the socket carrying it must still be pinned by the caller,
because "on a phone that also has cellular up, an unbound SSDP socket reaches neither the TV nor
anything else useful". That was written for one rung of the discovery ladder and is true of all of
them. `InterfaceSocketBinder` implements the rule for TCP only, so the mDNS and UDP broadcast rungs
are precisely where an unbound socket slips back in. Listening is the mirror hazard: a wildcard
listener on a phone publishes the control port on the cellular interface as well.

## Decision

- Every listening and outgoing socket in `:mobile` and `:castcore` is bound to the address of the
  interface `HotspotInterfaceSelector` chose, surfaced as `LocalNetwork.boundAddress`. No socket is
  created before a `LocalNetwork` verdict exists; `NoLocalNetwork` opens nothing and reports the
  affected modes as Blocked with a remedy.
- Never `0.0.0.0`, never `InetSocketAddress(port)`, and never a hardcoded dotted quad, prefix length
  or interface name. Address, prefix and interface are derived, and everything else is bounded by
  what was derived.
- TCP goes through `InterfaceSocketBinder(localAddress)` and nothing else. The bare
  `ServerSocket(port)` and `Socket(host, port)` constructors are banned in phone sources.
- UDP uses `DatagramSocket(null)` and an explicit `bind(InetSocketAddress(localAddress, port))`.
  `DatagramSocket(port)` is banned: the binder does not cover datagrams, and that constructor is a
  wildcard bind wearing a short name.
- Multicast uses `MulticastSocket(null)`, an explicit bind, then
  `setNetworkInterface(NetworkInterface.getByInetAddress(localAddress))` and a `joinGroup` taking
  that same interface — the shape `ReceiverMdnsResponder` already uses on the television. Binding
  alone is not enough, because the kernel otherwise picks the multicast egress interface from the
  routing table, and that table points at cellular. The mDNS rung also holds a `WifiManager`
  `MulticastLock` for its lifetime.
- The TCP line-probe sweep enumerates `Ipv4Subnet(selected.address, selected.prefixLength).hosts()`
  and nothing else, keeping that helper's `maximumHosts` bound. A subnet too large to sweep is not
  swept; the ladder falls through to mDNS, broadcast, SSDP and manual entry.
- The app never calls `bindProcessToNetwork` or `setProcessDefaultNetwork` and never requests a
  default-route change. A connectivity change re-runs the assessment, closes every socket and
  re-derives the address, because a hotspot coming up after the app started is the ordinary case.
- Binding is addressing, not confidentiality. No string may call bound traffic private or encrypted;
  tokens remain authorisation only.

## Consequences

### Positive

- Casting works in the topology the product is built around, rather than only on shared Wi-Fi.
- No control port is ever published on the cellular interface.
- A wide subnet degrades to another rung instead of scanning addresses that cannot answer.
- The socket families the binder does not cover have a written rule and an executable guard.

### Trade-offs

- The selected address is threaded through every call site that opens a socket. There is no ambient
  default to fall back on, and that is deliberate.
- Sockets are rebuilt on a connectivity change rather than carried across one.
- `InterfaceSocketBinder` stays TCP-only here. UDP and multicast binding is enforced by policy and a
  source guard rather than by a type, because `:protocol` is consumed unchanged as the host SDK.
- The emulator and physical-device tests for tethered binding are written but have not been run.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Bind listeners to `0.0.0.0` and let the kernel pick an outbound source. | With cellular as the default network the kernel picks the WAN, and a wildcard listener publishes the control port on the cellular interface. `AGENTS.md` forbids a wildcard listen. |
| `ConnectivityManager.bindProcessToNetwork` for the local network. | A tether interface is not an exposed `Network`, so there is no object to bind to, and it would bind unrelated process traffic as well. |
| Hardcode the historical hotspot range `192.168.43.0/24`. | Vendors ship `ap0`, `swlan0`, `softap0` and `rndis0` with differing addresses and prefixes; `HotspotInterfaceSelector` derives both, and `AGENTS.md` bans a hardcoded subnet or prefix. |
| `requestNetwork` with `TRANSPORT_WIFI`, then that network's socket factory. | While the phone is the access point its Wi-Fi client link is normally down, and the tether is not published as a Wi-Fi transport, so the callback yields the wrong network or none. |
| Widen `InterfaceSocketBinder` with UDP and multicast factories now. | That changes `:protocol`, which this work consumes unchanged as the host SDK; it is separate cross-language work. |

## Invariants and validation

- A source-guard test in `:castcore` reads the `:mobile` and `:castcore` Kotlin sources from disk
  and fails on any wildcard bind — `ServerSocket(`, `DatagramSocket(` or `MulticastSocket(` taking
  a port, a single-argument `InetSocketAddress(port)`, or the literal `0.0.0.0` — and on any
  dotted-quad literal outside test sources. It must go red the moment somebody adds a convenience
  constructor, and it needs no device, emulator or network to run.
- A sweep test asserts the probe host list is exactly `Ipv4Subnet(selected).hosts()` across a range
  of prefixes, that the local address is excluded, and that a subnet over the `maximumHosts` bound
  refuses to sweep rather than truncating silently.
- An assessor test feeds `LocalNetworkAssessor` a cellular-shaped address alongside a tether-shaped
  one and asserts the bound address is the tether one, never the cellular one.
- A loopback test proves a bound listener accepts on its own address and that a socket bound to a
  different local address does not reach it. The multicast and broadcast rungs have emulator and
  physical-device tests written; those have not been run, and no result is claimed for them.

## Revisit criteria

A superseding record requires evidence rather than convenience. Either Android publishes a stable,
public way to obtain the local tether side as a `Network`, verified on the target phones; or
physical-device evidence shows a correctly bound multicast socket cannot answer on a vendor's tether
interface, which changes the ladder's rung order but not the binding rule; or cross-language
agreement extends `:protocol` with bound UDP and multicast factories, replacing the guard with a
type. Tidier call sites and unmeasured performance claims do not reopen this.

## References

- [Project constraints](../../../AGENTS.md)
- [Flint Mobile implementation slices](../README.md)
- [`HotspotInterfaceSelector`](../../../protocol/kotlin/src/main/kotlin/com/rextechnologies/flint/protocol/network/NetworkInterfaces.kt)
- [`InterfaceSocketBinder`](../../../protocol/kotlin/src/main/kotlin/com/rextechnologies/flint/protocol/network/InterfaceSocketBinder.kt)
- [`Ipv4Subnet.hosts()`](../../../protocol/kotlin/src/main/kotlin/com/rextechnologies/flint/protocol/network/Ipv4.kt)
- [`Ssdp` and its unbound-socket warning](../../../protocol/kotlin/src/main/kotlin/com/rextechnologies/flint/protocol/dlna/Ssdp.kt)
- [`LocalNetwork`](../../../apps/phone/core/src/main/kotlin/com/rextechnologies/flint/castcore/capability/LocalNetwork.kt)
- [`ReceiverMdnsResponder`](../../../apps/receiver/app/src/main/kotlin/com/rextechnologies/flint/receiver/net/ReceiverMdnsResponder.kt)
- [Fire TV browser ADR-0004](../../fire-tv-browser-implementation/adrs/ADR-0004-NONSECRET-ENDPOINT-DISCOVERY.md)
