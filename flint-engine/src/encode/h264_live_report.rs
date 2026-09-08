use super::tests::moving_frame;
use super::*;

/// Prints what the encoder actually did on this machine.
///
/// The live tests above skip when no encoder exists, which is correct but indistinguishable
/// from passing. Run this deliberately to see the real numbers.
#[test]
#[ignore = "reports rather than asserts; run deliberately"]
fn report_live_encoding() {
    let config = EncoderConfig {
        width: 320,
        height: 240,
        frame_rate: 30,
        bitrate_bits_per_second: 1_000_000,
        codec: VideoCodec::H264,
    };

    match H264Encoder::new(config) {
        Ok(mut encoder) => {
            println!("encoder created: {encoder:?}");
            println!(
                "codec specific data blocks: {}",
                encoder.codec_specific_data().len()
            );
            let mut units = 0;
            let mut key_frames = 0;
            let mut bytes = 0usize;
            for index in 0..30 {
                match encoder.submit(&moving_frame(index, 320, 240), false) {
                    Ok(Some(frame)) => {
                        units += 1;
                        bytes += frame.data.len();
                        if frame.key_frame {
                            key_frames += 1;
                        }
                        if units == 1 {
                            println!(
                                "first access unit: {} bytes, key={}, pts={}us, head={:02x?}",
                                frame.data.len(),
                                frame.key_frame,
                                frame.presentation_time_us,
                                &frame.data[..frame.data.len().min(8)]
                            );
                        }
                    }
                    Ok(None) => {}
                    Err(error) => println!("frame {index} failed: {error}"),
                }
            }
            println!("ENCODED {units} access units, {key_frames} key frames, {bytes} bytes total");
        }
        Err(error) => println!("NO ENCODER: {error}"),
    }
}
