//! The mirror session: what turns a screen into a stream.
//!
//! Capture, convert, encode and hand out access units, one tick at a time. The session owns the
//! policy decisions that sit between those stages — what to do when the desktop has not changed,
//! when duplication is lost to a lock screen, when the receiver asks for a key frame — and none of
//! those need a GPU to be tested, so the pieces it drives are traits rather than concrete types.

use crate::capture::{CaptureError, FrameOutcome};
use crate::convert::scale::scale_bgra;
use crate::encode::video::{EncodeError, EncodedFrame, FrameData, SourceFrame, VideoEncoder};

/// A source of desktop frames.
///
/// [`crate::capture::duplication::DesktopDuplication`] is the real one; tests use their own.
pub trait FrameSource {
    /// Waits up to `timeout_ms` for a new frame.
    ///
    /// # Errors
    /// [`CaptureError::Interrupted`] asks the caller to re-create the source and carry on; every
    /// other error ends the session.
    fn next_frame(
        &mut self,
        timeout_ms: u32,
    ) -> Result<(FrameOutcome, Option<SourceFrame>), CaptureError>;

    /// Returns the storage from a frame previously produced by [`Self::next_frame`].
    ///
    /// The session calls this exactly once after the encoder no longer borrows the frame, including
    /// when scaling or encoding fails. A capture source can therefore keep its full-size pixel
    /// allocation and fill it again on the next capture instead of allocating on every tick.
    fn recycle_frame(&mut self, frame: SourceFrame);
}

/// Why a session tick could not complete.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum SessionError {
    /// Capture failed and could not be recovered.
    Capture(CaptureError),
    /// The encoder failed.
    Encode(EncodeError),
}

impl std::fmt::Display for SessionError {
    fn fmt(&self, formatter: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            Self::Capture(error) => write!(formatter, "capture failed: {error}"),
            Self::Encode(error) => write!(formatter, "encode failed: {error}"),
        }
    }
}

impl std::error::Error for SessionError {}

/// What one tick of the session produced.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum Tick {
    /// An access unit is ready for the wire.
    Encoded(EncodedFrame),
    /// The desktop did not change, so there was nothing new to encode.
    ///
    /// Not an error and not rare: a still desktop produces these continuously, and the session
    /// deliberately does not re-encode an unchanged frame just to keep a frame rate up. Bandwidth
    /// spent re-sending a picture the receiver is already showing buys nothing.
    Unchanged,
    /// Capture was interrupted and recovered; the next tick resumes normally.
    ///
    /// A lock screen, a UAC prompt or a resolution change all arrive here. The session reports it
    /// rather than swallowing it so the host can say why the picture paused, and forces a key
    /// frame on the next real frame because the receiver's reference frames are now stale.
    Recovered,
}

/// Counters a session keeps for the diagnostics view and for adaptive bitrate.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct SessionStats {
    /// Frames the encoder produced an access unit for.
    pub frames_encoded: u64,
    /// Ticks where the desktop had not changed.
    pub frames_unchanged: u64,
    /// Times capture was lost and re-created.
    pub recoveries: u64,
    /// Total encoded bytes handed to the transport.
    pub bytes_encoded: u64,
}

/// Drives capture into an encoder.
pub struct MirrorSession<S: FrameSource, E: VideoEncoder> {
    source: S,
    encoder: E,
    stats: SessionStats,
    force_key_frame: bool,
    started: bool,
    scaled: Vec<u8>,
}

impl<S: FrameSource, E: VideoEncoder> MirrorSession<S, E> {
    /// How long a tick waits for the desktop to change before reporting it unchanged.
    ///
    /// Short enough that a key-frame request or a stop is acted on promptly, long enough that a
    /// still desktop is not a busy loop.
    pub const FRAME_TIMEOUT_MS: u32 = 100;

    /// Builds a session over a capture source and an encoder.
    pub fn new(source: S, encoder: E) -> Self {
        let (width, height) = encoder.output_size();
        let scaled = packed_bgra_len(width, height)
            .map(Vec::with_capacity)
            .unwrap_or_default();

        Self {
            source,
            encoder,
            stats: SessionStats::default(),
            // The first real frame must be a key frame: a receiver has nothing to decode against.
            force_key_frame: true,
            started: false,
            // Allocated before capture starts so resizing does not hit the allocator on the first
            // frame and then recycled after every encoder submission.
            scaled,
        }
    }

    /// The pixel size the encoder was configured for, which is also the size of every access unit.
    #[must_use]
    pub fn output_size(&self) -> (u32, u32) {
        self.encoder.output_size()
    }

    /// The counters accumulated so far.
    #[must_use]
    pub fn stats(&self) -> SessionStats {
        self.stats
    }

    /// The codec setup data the receiver needs before the first access unit.
    #[must_use]
    pub fn codec_specific_data(&self) -> &[Vec<u8>] {
        self.encoder.codec_specific_data()
    }

    /// The codec this session's access units are actually in.
    #[must_use]
    pub fn codec(&self) -> crate::encode::VideoCodec {
        self.encoder.codec()
    }

    /// Asks for the next access unit to be a key frame.
    ///
    /// The receiver requests this after packet loss, and the session forces one itself after any
    /// capture recovery, because everything the receiver holds as a reference is stale by then.
    pub fn request_key_frame(&mut self) {
        self.force_key_frame = true;
    }

    /// Whether the next encoded frame will be forced to a key frame.
    #[must_use]
    pub fn key_frame_pending(&self) -> bool {
        self.force_key_frame
    }

    /// Runs one tick: capture, encode, and report what happened.
    ///
    /// # Errors
    /// [`SessionError`] when capture or encode failed in a way the session cannot recover from.
    pub fn tick(&mut self) -> Result<Tick, SessionError> {
        let outcome = self.source.next_frame(Self::FRAME_TIMEOUT_MS);

        let frame = match outcome {
            Ok((FrameOutcome::Captured, Some(frame))) => frame,
            Ok((FrameOutcome::Captured, None) | (FrameOutcome::Unchanged, _)) => {
                self.stats.frames_unchanged += 1;
                return Ok(Tick::Unchanged);
            }
            Err(CaptureError::Interrupted) => {
                self.stats.recoveries += 1;
                // Whatever the receiver was decoding against no longer describes the desktop.
                self.force_key_frame = true;
                return Ok(Tick::Recovered);
            }
            Err(error) => return Err(SessionError::Capture(error)),
        };

        let force_key_frame = self.force_key_frame || !self.started;
        // A texture goes straight to the encoder whatever its size. The GPU path converts colour
        // and scales in one hardware pass, so resizing it here would mean reading it back to do by
        // hand the thing the encoder is about to do for free.
        let frame_is_texture = matches!(frame.data, FrameData::Texture(_));
        let encoded =
            if frame_is_texture || (frame.width, frame.height) == self.encoder.output_size() {
                self.encoder.submit(&frame, force_key_frame)
            } else {
                match self.scale_frame(&frame) {
                    Ok(scaled) => {
                        let encoded = self.encoder.submit(&scaled, force_key_frame);
                        self.reclaim_scaled(scaled);
                        encoded
                    }
                    Err(error) => Err(error),
                }
            };

        // The encoder only borrows its input, so the capture allocation is available again on
        // every path from here, including scale and encoder failures.
        self.source.recycle_frame(frame);
        let encoded = encoded.map_err(SessionError::Encode)?;

        match encoded {
            Some(encoded) => {
                self.started = true;
                // Clear only when an IDR actually came out. Some encoders accept a force request
                // yet delay or ignore it; clearing on an ordinary access unit would strand a
                // receiver whose reference chain is already lost.
                if encoded.key_frame {
                    self.force_key_frame = false;
                }
                self.stats.frames_encoded += 1;
                self.stats.bytes_encoded += encoded.data.len() as u64;
                Ok(Tick::Encoded(encoded))
            }
            // The encoder took the frame but has not produced anything yet, which is normal for
            // the first frames of a stream.
            None => Ok(Tick::Unchanged),
        }
    }

    /// Resizes a captured frame to what the encoder accepts using the session's pooled buffer.
    fn scale_frame(&mut self, frame: &SourceFrame) -> Result<SourceFrame, EncodeError> {
        let (width, height) = self.encoder.output_size();
        // Only system-memory frames reach here: textures are handed to the encoder unscaled.
        let FrameData::Bgra { pixels, stride } = &frame.data else {
            return Err(EncodeError::UnsupportedFrameData);
        };
        scale_bgra(
            pixels,
            frame.width,
            frame.height,
            *stride,
            width,
            height,
            &mut self.scaled,
        )
        .map_err(|error| EncodeError::Platform(error.to_string()))?;

        Ok(SourceFrame {
            width,
            height,
            presentation_time_us: frame.presentation_time_us,
            data: FrameData::Bgra {
                pixels: std::mem::take(&mut self.scaled),
                stride: width * 4,
            },
        })
    }

    /// Takes the scaling buffer back off a frame so the next tick reuses its allocation.
    ///
    /// Without this the resize allocates a full frame every tick, which on a 4K desktop is the
    /// single largest allocation on the frame path — exactly what the engine exists to avoid.
    fn reclaim_scaled(&mut self, frame: SourceFrame) {
        // Only ever called with a frame `scale_frame` produced, which is always system memory.
        if let FrameData::Bgra { pixels, .. } = frame.data {
            self.scaled = pixels;
        }
    }
}

/// The packed BGRA byte count when it can be represented by a `Vec` on this target.
fn packed_bgra_len(width: u32, height: u32) -> Option<usize> {
    let len = u64::from(width)
        .checked_mul(u64::from(height))?
        .checked_mul(4)?;
    usize::try_from(len)
        .ok()
        .filter(|&len| isize::try_from(len).is_ok())
}

#[cfg(test)]
#[path = "session_tests.rs"]
mod tests;

#[cfg(all(test, windows))]
/// Live diagnostics that capture and encode from the real desktop.
///
/// Public so the decode round trips can reuse the bitmap writer rather than growing a second copy.
#[path = "session_live_pipeline.rs"]
pub mod live_pipeline;

#[cfg(all(test, windows))]
mod stage_timing {
    use crate::capture::duplication::DesktopDuplication;
    use crate::capture::readback::FrameReadback;
    use crate::capture::FrameOutcome;
    use crate::convert::nv12::bgra_to_nv12;
    use crate::encode::h264::H264Encoder;
    use crate::encode::video::{EncoderConfig, FrameData, VideoEncoder};
    use crate::encode::VideoCodec;
    use std::time::Instant;

    fn summarise(name: &str, mut samples: Vec<u64>) -> u64 {
        if samples.is_empty() {
            println!("{name:>20}: no samples");
            return 0;
        }
        samples.sort_unstable();
        let median = samples[samples.len() / 2];
        println!(
            "{name:>20}: median {:>7.2}ms  worst {:>7.2}ms  (n={})",
            median as f64 / 1000.0,
            *samples.last().unwrap() as f64 / 1000.0,
            samples.len()
        );
        median
    }

    /// Times the readback itself, never the wait for the desktop to change.
    ///
    /// `acquire` blocks until Windows has a new frame, so timing it measures how busy the screen
    /// happened to be rather than anything about this code — which made an earlier version of this
    /// harness report the same configuration at 25ms and at 80ms depending on nothing but whether
    /// the desktop was moving. Only the work between having a texture and having pixels is timed.
    fn measure(label: &str, gpu_scale: bool, wanted: usize) -> u64 {
        let Ok(mut duplication) = DesktopDuplication::open(0) else {
            println!("NO CAPTURE");
            return 0;
        };
        let format = duplication.format();
        let (width, height) = ((format.width / 2) & !1, (format.height / 2) & !1);
        let target = if gpu_scale {
            (width, height)
        } else {
            (format.width, format.height)
        };

        let mut readback = FrameReadback::new(
            duplication.device().clone(),
            duplication.context().clone(),
            format,
        );

        let Ok(mut encoder) = H264Encoder::new(EncoderConfig {
            width,
            height,
            frame_rate: 60,
            bitrate_bits_per_second: 12_000_000,
            codec: VideoCodec::H264,
        }) else {
            println!("NO ENCODER");
            return 0;
        };

        let mut readback_us = Vec::new();
        let mut convert_us = Vec::new();
        let mut encode_us = Vec::new();
        let mut nv12 = Vec::new();
        let mut scaled = Vec::new();

        let deadline = Instant::now() + std::time::Duration::from_secs(20);
        while readback_us.len() < wanted && Instant::now() < deadline {
            // Not timed: this is Windows waiting for the screen to change.
            let Ok((outcome, texture)) = duplication.acquire(200) else {
                continue;
            };
            let (FrameOutcome::Captured, Some(texture)) = (outcome, texture) else {
                duplication.release();
                continue;
            };

            let started = Instant::now();
            let frame = readback.read_scaled(&texture, format, target, 0);
            let elapsed = started.elapsed().as_micros() as u64;
            duplication.release();

            let Ok(Some(frame)) = frame else { continue };
            readback_us.push(elapsed);

            let started = Instant::now();
            let frame = if gpu_scale {
                frame
            } else {
                // System memory only here: the GPU path hands textures straight to the encoder.
                let FrameData::Bgra { pixels, stride } = &frame.data else {
                    continue;
                };
                if crate::convert::scale::scale_bgra(
                    pixels,
                    frame.width,
                    frame.height,
                    *stride,
                    width,
                    height,
                    &mut scaled,
                )
                .is_err()
                {
                    continue;
                }
                crate::encode::video::SourceFrame {
                    width,
                    height,
                    presentation_time_us: 0,
                    data: FrameData::Bgra {
                        pixels: std::mem::take(&mut scaled),
                        stride: width * 4,
                    },
                }
            };

            // System memory only here: the GPU path hands textures straight to the encoder.
            let FrameData::Bgra { pixels, stride } = &frame.data else {
                continue;
            };
            if bgra_to_nv12(pixels, frame.width, frame.height, *stride, &mut nv12).is_err() {
                continue;
            }
            convert_us.push(started.elapsed().as_micros() as u64);

            let started = Instant::now();
            let _ = encoder.submit(&frame, false);
            encode_us.push(started.elapsed().as_micros() as u64);

            if !gpu_scale {
                // The stage timings run the readback path deliberately, so this is always system
                // memory; reclaiming the buffer is what keeps the loop allocation-free.
                if let FrameData::Bgra { pixels, .. } = frame.data {
                    scaled = pixels;
                    scaled.clear();
                }
            }
        }

        println!("--- {label} ---");
        let a = summarise("readback", readback_us);
        let b = summarise("scale+nv12", convert_us);
        let c = summarise("encode", encode_us);
        let total = a + b + c;
        println!("{:>20}: {:>7.2}ms", "TOTAL median", total as f64 / 1000.0);
        total
    }

    /// Compares CPU-side and GPU-side downscaling on equal terms.
    ///
    /// Move a window around while this runs: it needs the desktop to actually change to gather
    /// samples, and a completely still screen produces none at all.
    #[test]
    #[ignore = "measures this machine; run deliberately"]
    fn report_stage_timings() {
        measure("warmup", true, 10);

        let gpu = measure("GPU downscale before readback", true, 60);
        let cpu = measure("CPU downscale after readback", false, 60);

        println!();
        if gpu == 0 || cpu == 0 {
            println!("VERDICT: not measurable (was the desktop changing?)");
        } else if gpu < cpu {
            println!(
                "VERDICT: GPU downscale wins by {:.2}ms per frame ({:.0}%)",
                (cpu - gpu) as f64 / 1000.0,
                (cpu - gpu) as f64 / cpu as f64 * 100.0
            );
        } else {
            println!(
                "VERDICT: CPU downscale wins by {:.2}ms per frame ({:.0}%)",
                (gpu - cpu) as f64 / 1000.0,
                (gpu - cpu) as f64 / gpu as f64 * 100.0
            );
        }
    }
}
