use super::*;
use crate::capture::readback::DesktopFrameSource;
use crate::encode::h264::H264Encoder;
use crate::encode::video::EncoderConfig;
use crate::encode::VideoCodec;

/// Captures one desktop frame and writes it out as a bitmap for a person to look at.
///
/// The diagnostic that separates "the receiver renders our frames wrongly" from "we are
/// sending it a blank picture". Both look identical from the host's counters, which happily
/// report thousands of frames either way.
#[test]
#[ignore = "needs an interactive desktop; run deliberately"]
fn report_captured_frame_as_a_bitmap() {
    let mut source = match DesktopFrameSource::open(0) {
        Ok(source) => source,
        Err(error) => {
            println!("NO CAPTURE: {error}");
            return;
        }
    };

    let format = source.format();
    println!("capture: {}x{}", format.width, format.height);

    // The first acquire routinely reports nothing: duplication only hands over a frame once
    // something on the desktop has changed since it started.
    let mut captured = None;
    for attempt in 0..120 {
        match source.next_frame(16) {
            Ok((_, Some(frame))) => {
                captured = Some(frame);
                println!("got a frame on attempt {attempt}");
                break;
            }
            Ok((outcome, None)) => {
                if attempt % 30 == 0 {
                    println!("attempt {attempt}: {outcome:?}, no frame");
                }
            }
            Err(error) => {
                println!("CAPTURE FAILED: {error}");
                return;
            }
        }
        std::thread::sleep(std::time::Duration::from_millis(16));
    }

    let Some(frame) = captured else {
        println!("NO FRAME: the desktop never reported a change");
        return;
    };

    // The readback path is what this diagnostic measures, so a texture would be off topic.
    let FrameData::Bgra { pixels, stride } = &frame.data else {
        println!("NOT A READBACK FRAME");
        return;
    };
    println!(
        "frame {}x{} stride {} bytes {}",
        frame.width,
        frame.height,
        stride,
        pixels.len()
    );

    // A uniform frame is the whole question, so say so numerically as well as visually.
    let mut lowest = u8::MAX;
    let mut highest = u8::MIN;
    let mut total = 0u64;
    for &byte in pixels.iter() {
        lowest = lowest.min(byte);
        highest = highest.max(byte);
        total += u64::from(byte);
    }
    println!(
        "byte range {lowest}..{highest}, mean {:.1}",
        total as f64 / pixels.len() as f64
    );
    if lowest == highest {
        println!("UNIFORM: every byte is {lowest} — the capture is blank");
    }

    let path = std::env::temp_dir().join("flint-captured-frame.bmp");
    write_bmp(&path, pixels, frame.width, frame.height, *stride);
    println!("wrote {}", path.display());

    // The encoder is fed the scaled frame, not this one. A capture that looks right and a
    // scale that does not produce exactly the same host counters, so dump both.
    let (target_width, target_height) =
        crate::convert::scale::encoded_frame_size(frame.width, frame.height, 1920);
    println!("scaling to {target_width}x{target_height}");

    let mut scaled = Vec::new();
    match crate::convert::scale::scale_bgra(
        pixels,
        frame.width,
        frame.height,
        *stride,
        target_width,
        target_height,
        &mut scaled,
    ) {
        Ok(()) => {
            let mut lowest = u8::MAX;
            let mut highest = u8::MIN;
            let mut total = 0u64;
            for &byte in scaled.iter() {
                lowest = lowest.min(byte);
                highest = highest.max(byte);
                total += u64::from(byte);
            }
            println!(
                "scaled bytes {}, range {lowest}..{highest}, mean {:.1}",
                scaled.len(),
                total as f64 / scaled.len() as f64
            );
            if lowest == highest {
                println!("SCALED UNIFORM: every byte is {lowest} — the scale is blank");
            }

            let scaled_path = std::env::temp_dir().join("flint-scaled-frame.bmp");
            write_bmp(
                &scaled_path,
                &scaled,
                target_width,
                target_height,
                target_width * 4,
            );
            println!("wrote {}", scaled_path.display());

            // The last host-side transform before the encoder. Rebuilding a picture from the
            // NV12 the encoder is actually handed is the only way to see a colour mistake:
            // wrong chroma still encodes, still decodes, and still reports healthy counters.
            let mut nv12 = Vec::new();
            match crate::convert::nv12::bgra_to_nv12(
                &scaled,
                target_width,
                target_height,
                target_width * 4,
                &mut nv12,
            ) {
                Ok(()) => {
                    let luma_bytes = (target_width * target_height) as usize;
                    let (luma, chroma) = nv12.split_at(luma_bytes);
                    println!(
                        "nv12 bytes {}, luma {}..{}, chroma {}..{}",
                        nv12.len(),
                        luma.iter().min().copied().unwrap_or(0),
                        luma.iter().max().copied().unwrap_or(0),
                        chroma.iter().min().copied().unwrap_or(0),
                        chroma.iter().max().copied().unwrap_or(0)
                    );

                    let rebuilt = nv12_to_bgra(&nv12, target_width, target_height);
                    let nv12_path = std::env::temp_dir().join("flint-nv12-frame.bmp");
                    write_bmp(
                        &nv12_path,
                        &rebuilt,
                        target_width,
                        target_height,
                        target_width * 4,
                    );
                    println!("wrote {}", nv12_path.display());
                }
                Err(error) => println!("NV12 FAILED: {error}"),
            }
        }
        Err(error) => println!("SCALE FAILED: {error}"),
    }
}

/// Rebuilds a BGRA picture from NV12, so a chroma mistake becomes something a person can see.
pub fn nv12_to_bgra(nv12: &[u8], width: u32, height: u32) -> Vec<u8> {
    let luma_bytes = (width * height) as usize;
    let (luma, chroma) = nv12.split_at(luma_bytes);
    let mut out = vec![0u8; luma_bytes * 4];

    for y in 0..height {
        for x in 0..width {
            let luma_value = f32::from(luma[(y * width + x) as usize]);
            let chroma_index = (((y / 2) * (width / 2)) + (x / 2)) as usize * 2;
            let blue_diff = f32::from(chroma[chroma_index]) - 128.0;
            let red_diff = f32::from(chroma[chroma_index + 1]) - 128.0;

            // BT.601 inverse, matching the forward weights the converter uses.
            let scaled_luma = 1.164 * (luma_value - 16.0);
            let red = scaled_luma + 1.596 * red_diff;
            let green = scaled_luma - 0.392 * blue_diff - 0.813 * red_diff;
            let blue = scaled_luma + 2.017 * blue_diff;

            let at = ((y * width + x) * 4) as usize;
            out[at] = blue.clamp(0.0, 255.0) as u8;
            out[at + 1] = green.clamp(0.0, 255.0) as u8;
            out[at + 2] = red.clamp(0.0, 255.0) as u8;
            out[at + 3] = 255;
        }
    }

    out
}

/// Writes a top-down 32-bit bitmap, so the dump needs no image crate to be viewable.
pub fn write_bmp(path: &std::path::Path, pixels: &[u8], width: u32, height: u32, stride: u32) {
    const HEADER_BYTES: u32 = 54;
    let image_bytes = width * height * 4;
    let mut out = Vec::with_capacity((HEADER_BYTES + image_bytes) as usize);

    out.extend_from_slice(b"BM");
    out.extend_from_slice(&(HEADER_BYTES + image_bytes).to_le_bytes());
    out.extend_from_slice(&0u32.to_le_bytes());
    out.extend_from_slice(&HEADER_BYTES.to_le_bytes());
    out.extend_from_slice(&40u32.to_le_bytes());
    out.extend_from_slice(&(width as i32).to_le_bytes());
    // Negative height means the rows are stored top-down, matching the capture's own order.
    out.extend_from_slice(&(-(height as i32)).to_le_bytes());
    out.extend_from_slice(&1u16.to_le_bytes());
    out.extend_from_slice(&32u16.to_le_bytes());
    out.extend_from_slice(&0u32.to_le_bytes());
    out.extend_from_slice(&image_bytes.to_le_bytes());
    out.extend_from_slice(&0u32.to_le_bytes());
    out.extend_from_slice(&0u32.to_le_bytes());
    out.extend_from_slice(&0u32.to_le_bytes());
    out.extend_from_slice(&0u32.to_le_bytes());

    for row in 0..height {
        let start = (row * stride) as usize;
        let end = start + (width * 4) as usize;
        out.extend_from_slice(&pixels[start..end]);
    }

    std::fs::write(path, out).expect("the bitmap should be writable");
}

/// Runs the real pipeline end to end: this desktop, a real encoder, real access units.
///
/// Ignored by default because it depends on an interactive session rather than on the code;
/// run it deliberately to prove the host half of a mirror actually works on a given machine.
#[test]
#[ignore = "needs an interactive desktop; run deliberately"]
fn report_live_mirror_pipeline() {
    let source = match DesktopFrameSource::open(0) {
        Ok(source) => source,
        Err(error) => {
            println!("NO CAPTURE: {error}");
            return;
        }
    };

    let format = source.format();
    println!(
        "capture: {}x{} on adapter {}",
        format.width, format.height, format.adapter_luid
    );

    // Encoders need even dimensions; a desktop can legitimately be odd-sized.
    let config = EncoderConfig {
        width: format.width & !1,
        height: format.height & !1,
        frame_rate: 30,
        bitrate_bits_per_second: 12_000_000,
        codec: VideoCodec::H264,
    };

    let encoder = match H264Encoder::new(config) {
        Ok(encoder) => encoder,
        Err(error) => {
            println!("NO ENCODER: {error}");
            return;
        }
    };
    println!(
        "encoder ready, codec data blocks: {}",
        encoder.codec_specific_data().len()
    );

    let mut session = MirrorSession::new(source, encoder);
    let started = std::time::Instant::now();
    let mut encoded = 0;
    let mut key_frames = 0;
    let mut ticks = 0u64;
    let mut unchanged = 0u64;

    // Poll for a couple of seconds; a completely still desktop legitimately yields nothing, so
    // this reports rather than asserts.
    while started.elapsed() < std::time::Duration::from_secs(3) && encoded < 30 {
        ticks += 1;
        match session.tick() {
            Ok(Tick::Encoded(frame)) => {
                if encoded == 0 {
                    println!(
                        "first access unit: {} bytes, key={}, head={:02x?}",
                        frame.data.len(),
                        frame.key_frame,
                        &frame.data[..frame.data.len().min(8)]
                    );
                }
                encoded += 1;
                if frame.key_frame {
                    key_frames += 1;
                }
            }
            Ok(Tick::Unchanged) => {
                unchanged += 1;
            }
            Ok(Tick::Recovered) => println!("capture recovered"),
            Err(error) => {
                println!("PIPELINE FAILED: {error}");
                return;
            }
        }
    }

    let stats = session.stats();
    println!("ticks attempted: {ticks}, unchanged returned: {unchanged}");
    println!(
        "PIPELINE OK: {encoded} access units ({key_frames} key), {} bytes, {} unchanged ticks, {} recoveries",
        stats.bytes_encoded, stats.frames_unchanged, stats.recoveries
    );
}

/// The exact path the FFI takes, including the GPU downscale, dumped for a person to look at.
///
/// Distinct from [`report_captured_frame_as_a_bitmap`], which scales on the CPU. The live session
/// asks the capture source to reduce the frame on the GPU before reading it back, and that is a
/// different code path with its own stride and its own opportunities to produce a picture that is
/// technically a picture and visually wrong. A green television is exactly what an all-zero or
/// half-written NV12 buffer looks like after decoding, so this says numerically whether the bytes
/// leaving this machine carry an image at all.
#[test]
#[ignore = "needs an interactive desktop; run deliberately"]
fn report_gpu_scaled_frame_as_the_encoder_receives_it() {
    let Ok(mut source) = DesktopFrameSource::open(0) else {
        println!("NO CAPTURE");
        return;
    };

    let format = source.format();
    let (target_width, target_height) =
        crate::convert::scale::encoded_frame_size(format.width, format.height, 1920);
    println!(
        "capture {}x{} -> target {target_width}x{target_height}",
        format.width, format.height
    );

    // The line under test: the session asks for a reduced frame rather than scaling afterwards.
    source.set_target_size((target_width, target_height));
    println!("source reports target {:?}", source.target_size());

    let mut captured = None;
    for _ in 0..120 {
        if let Ok((_, Some(frame))) = source.next_frame(16) {
            captured = Some(frame);
            break;
        }
        std::thread::sleep(std::time::Duration::from_millis(16));
    }

    let Some(frame) = captured else {
        println!("NO FRAME");
        return;
    };

    // System memory only here: this diagnostic measures the readback path by construction.
    let FrameData::Bgra { pixels, stride } = &frame.data else {
        println!("NOT A READBACK FRAME");
        return;
    };
    println!(
        "frame {}x{} stride {} bytes {}",
        frame.width,
        frame.height,
        stride,
        pixels.len()
    );

    let expected = (*stride as usize) * frame.height as usize;
    if pixels.len() != expected {
        println!(
            "SIZE MISMATCH: {} bytes for {}x{} stride {stride} (expected {expected})",
            pixels.len(),
            frame.width,
            frame.height
        );
    }

    summarise("bgra", pixels);

    let mut nv12 = Vec::new();
    match crate::convert::nv12::bgra_to_nv12(pixels, frame.width, frame.height, *stride, &mut nv12)
    {
        Ok(()) => {
            let luma_len = (frame.width * frame.height) as usize;
            let (luma, chroma) = nv12.split_at(luma_len);
            summarise("nv12 luma", luma);
            summarise("nv12 chroma", chroma);

            let path = std::env::temp_dir().join("flint-gpu-nv12-frame.bmp");
            let rgb = nv12_to_bgra(&nv12, frame.width, frame.height);
            write_bmp(&path, &rgb, frame.width, frame.height, frame.width * 4);
            println!("wrote {}", path.display());
        }
        Err(error) => println!("NV12 FAILED: {error}"),
    }
}

/// Prints the range and mean of a byte slice, and says plainly when it is uniform.
fn summarise(label: &str, bytes: &[u8]) {
    let mut lowest = u8::MAX;
    let mut highest = u8::MIN;
    let mut total = 0u64;
    for &byte in bytes {
        lowest = lowest.min(byte);
        highest = highest.max(byte);
        total += u64::from(byte);
    }
    println!(
        "{label}: {} bytes, range {lowest}..{highest}, mean {:.1}",
        bytes.len(),
        total as f64 / bytes.len() as f64
    );
    if lowest == highest {
        println!("  UNIFORM — {label} carries no picture");
    }
}

/// Per-frame processing cost of the GPU path against the readback path.
///
/// The honest comparison, and the one that answers whether keeping frames on the GPU was worth
/// doing. Only the work is timed: acquiring a frame blocks until the desktop changes, and including
/// that would measure how busy the screen is rather than how fast the pipeline is — a mistake this
/// project has already made once and corrected.
#[test]
#[ignore = "needs an interactive desktop; run deliberately"]
fn report_gpu_path_against_readback_path() {
    use crate::encode::selected::SelectedEncoder;

    let Ok(probe) = crate::capture::duplication::DesktopDuplication::open_primary() else {
        println!("NO CAPTURE");
        return;
    };
    let adapter = probe.format().adapter_luid;
    let full = probe.format();
    drop(probe);

    let (width, height) = crate::convert::scale::encoded_frame_size(full.width, full.height, 1920);
    let encoder_config = EncoderConfig {
        width,
        height,
        frame_rate: 60,
        bitrate_bits_per_second: 12_000_000,
        codec: VideoCodec::H264,
    };
    println!(
        "desktop {}x{} -> encode {width}x{height}",
        full.width, full.height
    );

    for gpu in [true, false] {
        let Ok(mut source) = DesktopFrameSource::open(0) else {
            println!("NO CAPTURE");
            return;
        };
        source.set_target_size((width, height));

        let encoder = if gpu {
            match SelectedEncoder::open_on_device(
                encoder_config,
                adapter,
                source.device().clone(),
                source.context().clone(),
            ) {
                Ok(encoder) if source.enable_gpu_frames().is_ok() => encoder,
                _ => {
                    println!("gpu path unavailable");
                    continue;
                }
            }
        } else {
            match SelectedEncoder::open(encoder_config, adapter) {
                Ok(encoder) => encoder,
                Err(_) => {
                    println!("no encoder");
                    continue;
                }
            }
        };

        let label = if gpu { "gpu texture" } else { "readback   " };
        let kind = encoder.kind();
        let mut session = MirrorSession::new(source, encoder);

        // Warm up past allocation and the encoder priming.
        for _ in 0..40 {
            let _ = session.tick();
        }

        let mut samples = Vec::new();
        let mut first_error = None;
        let mut unchanged = 0usize;
        let deadline = std::time::Instant::now() + std::time::Duration::from_secs(6);
        while std::time::Instant::now() < deadline && samples.len() < 200 {
            let started = std::time::Instant::now();
            match session.tick() {
                Ok(Tick::Encoded(_)) => samples.push(started.elapsed().as_secs_f64() * 1000.0),
                Ok(Tick::Unchanged) => unchanged += 1,
                Ok(Tick::Recovered) => {}
                Err(error) => {
                    if first_error.is_none() {
                        first_error = Some(format!("{error}"));
                    }
                }
            }
        }
        if let Some(error) = first_error {
            println!("{label}: first error: {error}");
        }
        println!("{label}: unchanged ticks {unchanged}");

        if samples.is_empty() {
            println!("{label} ({kind:?}): no encoded frames; move a window and re-run");
            continue;
        }
        samples.sort_by(f64::total_cmp);
        println!(
            "{label} ({kind:?}): n={:3} median {:5.2}ms  p95 {:5.2}ms  worst {:6.2}ms",
            samples.len(),
            samples[samples.len() / 2],
            samples[samples.len() * 95 / 100],
            samples[samples.len() - 1]
        );
    }
}

/// End-to-end tick time for the pipeline the FFI actually runs.
///
/// The stage-timing report above measures the software encoder, which hides a cost the hardware
/// path pays: frames are read back from the GPU, converted on the CPU, and then uploaded to a GPU
/// texture again. That round trip is the honest subject of this measurement, because a faster
/// encoder that needs its input copied back across the bus is not necessarily a faster pipeline.
#[test]
#[ignore = "needs an interactive desktop; run deliberately"]
fn report_end_to_end_tick_time_for_each_encoder() {
    use crate::encode::selected::SelectedEncoder;

    let Ok(duplication) = crate::capture::duplication::DesktopDuplication::open_primary() else {
        println!("NO CAPTURE");
        return;
    };
    let adapter = duplication.format().adapter_luid;
    drop(duplication);

    for forced in ["hardware", "software"] {
        // SAFETY: single-threaded test, and the variable is read only when an encoder is built.
        unsafe { std::env::set_var("FLINT_ENCODER", forced) };

        let Ok(mut source) = DesktopFrameSource::open(0) else {
            println!("NO CAPTURE");
            return;
        };
        let format = source.format();
        let (width, height) =
            crate::convert::scale::encoded_frame_size(format.width, format.height, 1920);
        source.set_target_size((width, height));

        let Ok(encoder) = SelectedEncoder::open(
            EncoderConfig {
                width,
                height,
                frame_rate: 60,
                bitrate_bits_per_second: 12_000_000,
                codec: VideoCodec::H264,
            },
            adapter,
        ) else {
            println!("{forced}: NO ENCODER");
            continue;
        };
        let kind = encoder.kind();
        let mut session = MirrorSession::new(source, encoder);

        // Warm up, so the first allocations and the encoder's own priming are not measured.
        for _ in 0..30 {
            let _ = session.tick();
        }

        let mut samples = Vec::new();
        for _ in 0..200 {
            let started = std::time::Instant::now();
            let outcome = session.tick();
            // Only ticks that actually encoded a frame: an unchanged desktop returns in nanoseconds
            // and would drag the median down to something the pipeline never achieves in use.
            if matches!(outcome, Ok(Tick::Encoded(_))) {
                samples.push(started.elapsed().as_secs_f64() * 1000.0);
            }
        }

        if samples.is_empty() {
            println!("{forced} ({kind:?}): no encoded ticks; the desktop never changed");
            continue;
        }
        samples.sort_by(f64::total_cmp);
        let median = samples[samples.len() / 2];
        let worst = samples[samples.len() - 1];
        let p95 = samples[samples.len() * 95 / 100];
        println!(
            "{forced:8} ({kind:?}): n={:3} median {median:5.2}ms  p95 {p95:5.2}ms  worst {worst:6.2}ms",
            samples.len()
        );
    }

    // SAFETY: single-threaded test; restores the ordinary preference for anything after this.
    unsafe { std::env::remove_var("FLINT_ENCODER") };
}

/// Per-frame *processing* cost of the GPU path against the readback path.
///
/// Both passes start from an acquired desktop texture, and the acquire itself is not timed. That
/// distinction is the whole measurement: `AcquireNextFrame` blocks until the desktop changes, so
/// timing a whole tick reports how busy the screen is rather than how fast the pipeline is. This
/// project made exactly that mistake once already and drew the wrong conclusion from it.
///
/// * Readback: copy the frame to system memory, convert BGRA to NV12 on the processor, encode.
/// * GPU: convert and scale with the video processor, encode. The frame never leaves the GPU.
///
/// The two run as separate passes rather than side by side, because a driver will not always give
/// out two hardware encoder sessions at once and the second one then silently produces nothing.
#[test]
#[ignore = "needs an interactive desktop; run deliberately"]
fn report_processing_cost_of_the_gpu_path_against_readback() {
    println!("{}", measure_processing(true));
    println!("{}", measure_processing(false));
}

/// Times one pipeline variant and describes it in a line.
#[cfg(test)]
fn measure_processing(use_gpu: bool) -> String {
    use crate::capture::duplication::DesktopDuplication;
    use crate::capture::readback::FrameReadback;
    use crate::encode::h264_hardware::HardwareH264Encoder;
    use crate::encode::video::FrameData;

    let label = if use_gpu {
        "gpu texture"
    } else {
        "readback   "
    };

    let Ok(mut duplication) = DesktopDuplication::open_primary() else {
        return format!("{label}: NO CAPTURE");
    };
    let format = duplication.format();
    let adapter = format.adapter_luid;
    let (width, height) =
        crate::convert::scale::encoded_frame_size(format.width, format.height, 1920);
    let config = EncoderConfig {
        width,
        height,
        frame_rate: 60,
        bitrate_bits_per_second: 12_000_000,
        codec: VideoCodec::H264,
    };

    let device = duplication.device().clone();
    let context = duplication.context().clone();

    // The GPU encoder shares the capture device, which is what lets it read the captured texture at
    // all. The readback encoder deliberately does not, so it is fed system memory exactly as the
    // pipeline did before this path existed.
    let encoder = if use_gpu {
        HardwareH264Encoder::open_on_device(config, adapter, device.clone(), context.clone())
    } else {
        HardwareH264Encoder::new(config, adapter)
    };
    let Ok(mut encoder) = encoder else {
        return format!("{label}: NO ENCODER");
    };
    let mut readback = FrameReadback::new(device, context, format);

    let mut samples = Vec::new();
    let mut scaled: Vec<u8> = Vec::new();
    let mut first_error = None;
    let mut warmed = 0usize;
    let deadline = std::time::Instant::now() + std::time::Duration::from_secs(12);

    while std::time::Instant::now() < deadline && samples.len() < 120 {
        let Ok((_, Some(texture))) = duplication.acquire(16) else {
            duplication.release();
            continue;
        };

        let started = std::time::Instant::now();
        let outcome = if use_gpu {
            let frame = SourceFrame {
                width: format.width,
                height: format.height,
                presentation_time_us: 0,
                data: FrameData::Texture(texture.clone()),
            };
            let result = encoder.submit(&frame, false).map(|_| ());
            duplication.release();
            result
        } else {
            let frame = readback.read_scaled(&texture, format, (width, height), 0);
            duplication.release();
            match frame {
                Ok(Some(frame)) => {
                    // The same resize the session performs. The capture source accepts a target
                    // size and then hands back the full desktop anyway, so this cost is part of the
                    // readback path in practice whatever the intent was.
                    if (frame.width, frame.height) == (width, height) {
                        encoder.submit(&frame, false).map(|_| ())
                    } else {
                        let FrameData::Bgra { pixels, stride } = &frame.data else {
                            continue;
                        };
                        match crate::convert::scale::scale_bgra(
                            pixels,
                            frame.width,
                            frame.height,
                            *stride,
                            width,
                            height,
                            &mut scaled,
                        ) {
                            Ok(()) => {
                                let resized = SourceFrame {
                                    width,
                                    height,
                                    presentation_time_us: 0,
                                    data: FrameData::Bgra {
                                        pixels: std::mem::take(&mut scaled),
                                        stride: width * 4,
                                    },
                                };
                                let result = encoder.submit(&resized, false).map(|_| ());
                                if let FrameData::Bgra { pixels, .. } = resized.data {
                                    scaled = pixels;
                                }
                                result
                            }
                            Err(error) => Err(crate::encode::video::EncodeError::Platform(
                                error.to_string(),
                            )),
                        }
                    }
                }
                // The readback runs double-buffered, so the first calls legitimately produce
                // nothing while the pipeline fills.
                Ok(None) => continue,
                Err(error) => Err(crate::encode::video::EncodeError::Platform(
                    error.to_string(),
                )),
            }
        };
        let elapsed = started.elapsed().as_secs_f64() * 1000.0;

        match outcome {
            Ok(()) => {
                // The first frames pay for converter and texture construction, which a session
                // pays once and this measurement should not attribute to every frame.
                warmed += 1;
                if warmed > 20 {
                    samples.push(elapsed);
                }
            }
            Err(error) => {
                if first_error.is_none() {
                    first_error = Some(format!("{error}"));
                }
            }
        }
    }

    if let Some(error) = first_error {
        return format!("{label}: {error}");
    }
    if samples.is_empty() {
        return format!("{label}: no samples; move a window and re-run");
    }

    samples.sort_by(f64::total_cmp);
    format!(
        "{label}: n={:3} median {:5.2}ms  p95 {:5.2}ms  worst {:6.2}ms",
        samples.len(),
        samples[samples.len() / 2],
        samples[samples.len() * 95 / 100],
        samples[samples.len() - 1]
    )
}
