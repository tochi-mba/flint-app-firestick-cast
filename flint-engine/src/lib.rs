//! Flint engine — the hot path.
//!
//! A REX Technologies product.
//!
//! This crate owns everything that must happen between two frames: capture, colour conversion,
//! encode, packetization and transmission. It is a separate language from the shell for one
//! reason — a garbage-collector pause landing inside a 16.6 ms frame budget is a stutter the user
//! sees, and this side of the boundary has no collector to pause.
//!
//! # Invariants
//!
//! * Nothing on the frame path allocates. Buffers are pooled and reused.
//! * No panic crosses the FFI boundary. The release profile aborts rather than unwinding.
//! * Capability is proven by probing, never inferred from a device name.

#![warn(missing_docs)]
#![warn(clippy::all)]

pub mod capture;
pub mod convert;
pub mod decode;
pub mod encode;
pub mod session;
pub mod wire;

#[cfg(windows)]
pub mod ffi;

/// The engine version, reported across the FFI boundary so the shell can refuse a mismatch.
pub const VERSION: &str = env!("CARGO_PKG_VERSION");

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn version_is_reported() {
        assert_eq!(VERSION, "0.1.0");
    }
}
