//! Decoding, for diagnostics only.
//!
//! Nothing here runs during a cast session. The host encodes and the television decodes; a decoder
//! on this side exists solely so the question "is the bitstream we send actually a picture?" can be
//! answered without a television, which is the only practical way to tell a host bug from a
//! receiver bug when both ends report success and the panel shows a flat green field.

#[cfg(windows)]
pub mod h264;

#[cfg(test)]
#[cfg(windows)]
#[path = "roundtrip_tests.rs"]
mod roundtrip_tests;
