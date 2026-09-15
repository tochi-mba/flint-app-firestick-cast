//! Canonical wire vectors, shared across languages.
//!
//! The corpus below is the contract. Each case is encoded once, committed to `testdata/golden/`,
//! and asserted byte for byte by Rust here and by C# in `Flint.Protocol.Tests`. Kotlin joins when
//! the receiver lands.
//!
//! Three implementations agreeing with committed bytes is what stops them drifting apart.
//! # Regenerating
//!
//! ```text
//! cargo test --test golden -- --ignored regenerate
//! ```
//!
//! Regenerating is a deliberate act. A wire change that alters these bytes is a protocol change,
//! and the diff on these files is the review surface for it.

use std::fs;
use std::path::{Path, PathBuf};

use flint_engine::wire::codec::{self, WireError};
use flint_engine::wire::{Frame, Message};

#[path = "support/corpus.rs"]
mod corpus_data;
use corpus_data::corpus;

fn golden_dir() -> PathBuf {
    // Integration tests run with the crate root as the working directory.
    Path::new(env!("CARGO_MANIFEST_DIR"))
        .parent()
        .expect("the crate has a parent directory")
        .join("testdata")
        .join("golden")
}

#[test]
fn every_case_encodes_to_its_committed_bytes() {
    let directory = golden_dir();

    for vector in corpus() {
        let path = directory.join(format!("{}.bin", vector.name));
        let expected = fs::read(&path).unwrap_or_else(|error| {
            panic!(
                "missing golden vector {}: {error}. Regenerate with \
                 `cargo test --test golden -- --ignored regenerate`.",
                path.display()
            )
        });

        let actual = codec::encode(&vector.frame)
            .unwrap_or_else(|error| panic!("{} failed to encode: {error}", vector.name));

        assert_eq!(
            actual, expected,
            "{} does not match its committed bytes. If this change is intentional, regenerate the \
             vectors and review the diff as a protocol change.",
            vector.name
        );
    }
}

#[test]
fn every_committed_vector_decodes_to_its_case() {
    let directory = golden_dir();

    for vector in corpus() {
        let path = directory.join(format!("{}.bin", vector.name));
        let bytes = fs::read(&path).expect("golden vector is present");

        let decoded = codec::decode(&bytes)
            .unwrap_or_else(|error| panic!("{} failed to decode: {error}", vector.name));

        assert_eq!(
            decoded.message,
            normalise(vector.frame.message),
            "{}",
            vector.name
        );
        assert_eq!(decoded.flags, vector.frame.flags, "{} flags", vector.name);
        assert_eq!(
            decoded.protocol_version, vector.frame.protocol_version,
            "{} version",
            vector.name
        );
    }
}

/// Applies encoder normalisation so a decoded case can be compared to its source.
fn normalise(message: Message) -> Message {
    match message {
        Message::Hello {
            minimum_version,
            maximum_version,
            device_name,
            mut codec_capabilities,
            screen_width,
            screen_height,
            density_dpi,
        } => {
            codec_capabilities.sort_unstable();
            codec_capabilities.dedup();
            Message::Hello {
                minimum_version,
                maximum_version,
                device_name,
                codec_capabilities,
                screen_width,
                screen_height,
                density_dpi,
            }
        }
        other => other,
    }
}

#[test]
fn the_corpus_has_no_duplicate_names() {
    // Two cases sharing a name would silently overwrite one another on regeneration.
    let mut names: Vec<&str> = corpus().iter().map(|vector| vector.name).collect();
    let total = names.len();
    names.sort_unstable();
    names.dedup();
    assert_eq!(names.len(), total, "corpus names must be unique");
}

#[test]
fn no_committed_vector_is_orphaned() {
    // A file left behind after a case is renamed would never be asserted again.
    let directory = golden_dir();
    let expected: Vec<String> = corpus()
        .iter()
        .map(|vector| format!("{}.bin", vector.name))
        .collect();

    for entry in fs::read_dir(&directory).expect("golden directory exists") {
        let entry = entry.expect("directory entry is readable");
        let name = entry.file_name().to_string_lossy().into_owned();
        if !name.ends_with(".bin") {
            continue;
        }
        assert!(
            expected.contains(&name),
            "{name} is committed but no corpus case produces it. Delete it or restore the case."
        );
    }
}

#[test]
fn a_truncated_vector_is_reported_as_truncated_not_corrupt() {
    // A stream reader relies on this distinction to decide between "read more" and "give up".
    let bytes = codec::encode(&Frame::new(Message::Stats {
        receiver_queue_depth: 1,
        decode_latency_us: 2,
        round_trip_time_us: 3,
        dropped_video_frames: 4,
    }))
    .expect("stats encodes");

    for cut in 1..bytes.len() {
        let error = codec::decode_prefix(&bytes[..cut]).unwrap_err();
        assert!(
            matches!(error, WireError::Truncated(_)),
            "cutting at {cut} gave {error:?}, expected a truncation"
        );
    }
}

#[test]
#[ignore = "writes to testdata; run deliberately when the wire format changes"]
fn regenerate() {
    let directory = golden_dir();
    fs::create_dir_all(&directory).expect("golden directory can be created");

    for vector in corpus() {
        let bytes = codec::encode(&vector.frame)
            .unwrap_or_else(|error| panic!("{} failed to encode: {error}", vector.name));
        let path = directory.join(format!("{}.bin", vector.name));
        fs::write(&path, &bytes).expect("golden vector can be written");
        println!("wrote {} ({} bytes)", path.display(), bytes.len());
    }
}
