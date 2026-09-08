use super::*;
use crate::encode::selected::SelectedEncoder;
use crate::encode::video::{EncoderConfig, FrameData, SourceFrame, VideoEncoder};
use crate::encode::VideoCodec;

/// The check that would have caught a green television.
///
/// Flint tells the receiver a width and a height in `VIDEO_CONFIG`, and separately hands it the
/// encoder's parameter sets. The receiver builds one decoder from both. If they disagree the
/// decoder still configures, still accepts every access unit, still reports frames rendered, and
/// still shows a flat green field — because that is what a wrongly-sized YUV surface looks like.
/// Every counter on the host stays perfect. Nothing in the system notices except a person looking
/// at the television.
fn config(width: u32, height: u32) -> EncoderConfig {
    EncoderConfig {
        width,
        height,
        frame_rate: 60,
        bitrate_bits_per_second: 12_000_000,
        codec: VideoCodec::H264,
    }
}

/// A frame with real content, so the encoder produces a genuine key frame.
fn frame(index: u32, width: u32, height: u32) -> SourceFrame {
    let stride = width * 4;
    let mut pixels = vec![0u8; (stride * height) as usize];
    for y in 0..height {
        for x in 0..width {
            let offset = (y * stride + x * 4) as usize;
            pixels[offset] = ((x + index * 5) % 256) as u8;
            pixels[offset + 1] = ((y + index * 3) % 256) as u8;
            pixels[offset + 2] = ((x + y) % 256) as u8;
            pixels[offset + 3] = 255;
        }
    }
    SourceFrame {
        width,
        height,
        data: FrameData::Bgra { pixels, stride },
        presentation_time_us: i64::from(index) * 16_667,
    }
}

/// Encodes until a key frame appears, and returns it with the encoder's published parameter sets.
fn encode_until_key_frame(
    encoder: &mut SelectedEncoder,
    width: u32,
    height: u32,
) -> Option<(Vec<Vec<u8>>, Vec<u8>)> {
    for index in 0..120 {
        if let Some(unit) = encoder.submit(&frame(index, width, height), false).ok()? {
            if unit.key_frame {
                return Some((encoder.codec_specific_data().to_vec(), unit.data));
            }
        }
    }
    None
}

#[test]
#[ignore = "needs a real encoder; run deliberately"]
fn the_published_parameter_sets_describe_the_size_the_encoder_was_configured_for() {
    // The exact disagreement that puts a green picture on a television.
    for (width, height) in [(1920, 1080), (1920, 1200), (1280, 720)] {
        let Ok(mut encoder) = SelectedEncoder::open(config(width, height), i64::MAX) else {
            println!("no encoder for {width}x{height}");
            continue;
        };

        let Some((codec_data, _)) = encode_until_key_frame(&mut encoder, width, height) else {
            panic!("{width}x{height}: no key frame in 120 frames");
        };

        assert!(
            !codec_data.is_empty(),
            "{width}x{height}: no parameter sets, so the receiver cannot build a decoder at all"
        );

        let sps = codec_data
            .iter()
            .find_map(|block| dimensions_from_annex_b(block).ok())
            .unwrap_or_else(|| panic!("{width}x{height}: no readable SPS in the published sets"));

        println!("{width}x{height}: SPS says {}x{}", sps.width, sps.height);
        assert_eq!(
            (sps.width, sps.height),
            (width, height),
            "the parameter sets sent to the receiver describe {}x{} while VIDEO_CONFIG says \
             {width}x{height}; the decoder would be built for the wrong geometry",
            sps.width,
            sps.height
        );
    }
}

#[test]
#[ignore = "needs a real encoder; run deliberately"]
fn the_bitstream_agrees_with_the_parameter_sets_that_were_published() {
    // The other half: the published sets could be right while the stream carries different ones,
    // which is just as fatal and looks identical.
    let (width, height) = (1920, 1200);
    let Ok(mut encoder) = SelectedEncoder::open(config(width, height), i64::MAX) else {
        println!("no encoder");
        return;
    };

    let Some((codec_data, key_frame)) = encode_until_key_frame(&mut encoder, width, height) else {
        panic!("no key frame in 120 frames");
    };

    let published = codec_data
        .iter()
        .find_map(|block| dimensions_from_annex_b(block).ok())
        .expect("no readable SPS in the published sets");

    // A hardware encoder repeats its parameter sets in band; a software one may not, and an absent
    // in-band SPS is legitimate as long as the published one is correct.
    if let Ok(in_band) = dimensions_from_annex_b(&key_frame) {
        println!(
            "published {}x{}, in band {}x{}",
            published.width, published.height, in_band.width, in_band.height
        );
        assert_eq!(
            (published.width, published.height),
            (in_band.width, in_band.height),
            "the stream carries a different geometry than the parameter sets sent ahead of it"
        );
    } else {
        println!(
            "no in-band SPS; published {}x{}",
            published.width, published.height
        );
    }
}
