# flint-engine

The Flint hot path: capture, colour conversion, encode, packetization and transmission.

A REX Technologies product.

## Status

The native capability slice is compiled and tested: it initialises Media Foundation, enumerates
DXGI adapters and their display roles, asks Media Foundation for hardware encoders on each adapter,
and exposes that inventory through a fixed-layout C ABI. The Windows app uses this result today.

The frame path named above is not available in this crate. This crate does not capture, convert, encode,
packetize, or transmit frames, and its README does not treat those designed modules as shipped.

## Prerequisite

Rust on Windows links with MSVC, which needs the **Visual Studio Build Tools** with the
"Desktop development with C++" workload. Without it `cargo build` fails at the link step, and on a
Git Bash path it fails confusingly: GNU coreutils ships a `link.exe` that shadows the MSVC linker,
so the error reads `extra operand ... Try 'link --help'` rather than "no linker found".

Install the build tools, then:

```powershell
cd apps/windows/engine
cargo test
cargo clippy --all-targets -- -D warnings
cargo build
```

## Why Rust

The frame path has 16.6 ms to work with at 60 Hz. A garbage-collector pause landing inside that
budget is a stutter the user sees. This side of the FFI boundary has no collector to pause, and the
release profile aborts rather than unwinding so a panic can never cross into the managed host.

## Layout

| Module | Role |
|---|---|
| `encode` | Encoder model, and the vendor-neutral capability probe |
| `encode::mediafoundation` | Windows encoder and adapter discovery |

Capture, convert, transport, wire and session modules require the receiver and capture implementations.
