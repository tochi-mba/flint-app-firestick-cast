//! Pixel format conversion.
//!
//! Capture produces BGRA; every encoder wants NV12. This module is the bridge, and it is kept
//! platform-neutral on purpose so the colour maths can be tested anywhere rather than only on a
//! machine with a GPU.

#[cfg(windows)]
pub mod gpu_nv12;
pub mod nv12;
pub mod scale;
