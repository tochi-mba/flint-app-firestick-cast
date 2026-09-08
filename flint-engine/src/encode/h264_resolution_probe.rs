use super::tests::moving_frame;
use super::*;

/// Reports which frame sizes this machine's H.264 encoder actually produces output for.
#[test]
#[ignore = "reports rather than asserts; run deliberately"]
fn report_supported_resolutions() {
    for (width, height) in [(320u32, 240u32), (1280, 720), (1920, 1080), (2560, 1600)] {
        let config = EncoderConfig {
            width,
            height,
            frame_rate: 30,
            bitrate_bits_per_second: 8_000_000,
            codec: VideoCodec::H264,
        };

        match H264Encoder::new(config) {
            Ok(mut encoder) => {
                let mut units = 0;
                for index in 0..10 {
                    match encoder.submit(&moving_frame(index, width, height), false) {
                        Ok(Some(_)) => units += 1,
                        Ok(None) => {}
                        Err(error) => {
                            println!("{width}x{height}: submit failed: {error}");
                            break;
                        }
                    }
                }
                println!("{width}x{height}: created ok, {units} access units from 10 frames");
            }
            Err(error) => println!("{width}x{height}: NOT CREATED: {error}"),
        }
    }
}
