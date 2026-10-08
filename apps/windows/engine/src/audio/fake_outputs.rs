//! Outputs that tests plug in and out, for the follower and the session above it.

use std::sync::{Arc, Mutex, MutexGuard, PoisonError};

use super::follow::{Capture, Outputs};
use super::AudioError;

/// The outputs on the desk, shared by every capture opened from them.
#[derive(Default)]
pub(crate) struct Desk {
    /// Connected outputs, each with the sample its capture reads.
    pub connected: Vec<(String, i16)>,
    pub default: Option<String>,
    /// How many samples each read delivers.
    pub samples_per_read: usize,
    pub opens: usize,
    pub refuse_opens: bool,
    /// Opening panics, as a broken driver might take the capture thread down.
    pub panic_on_open: bool,
    /// Returned by the next read, once.
    pub failure: Option<AudioError>,
    pub meter: f32,
}

/// The fake outputs. Clones share one desk.
#[derive(Clone, Default)]
pub(crate) struct FakeOutputs(Arc<Mutex<Desk>>);

impl FakeOutputs {
    /// `outputs` connected, each reading one sample at a time.
    pub(crate) fn with(outputs: &[(&str, i16)], default: Option<&str>) -> Self {
        let fake = Self::default();
        fake.desk().samples_per_read = 1;
        fake.plug(outputs, default);
        fake
    }

    /// Replaces what is connected, and which output is the default.
    pub(crate) fn plug(&self, outputs: &[(&str, i16)], default: Option<&str>) {
        let mut desk = self.desk();
        desk.connected = outputs
            .iter()
            .map(|&(id, sample)| (id.to_owned(), sample))
            .collect();
        desk.default = default.map(str::to_owned);
    }

    pub(crate) fn desk(&self) -> MutexGuard<'_, Desk> {
        self.0.lock().unwrap_or_else(PoisonError::into_inner)
    }
}

pub(crate) struct FakeCapture {
    desk: Arc<Mutex<Desk>>,
    id: String,
}

impl Capture for FakeCapture {
    fn read(&mut self, out: &mut Vec<i16>) -> Result<(), AudioError> {
        let mut desk = self.desk.lock().unwrap_or_else(PoisonError::into_inner);
        if let Some(error) = desk.failure.take() {
            return Err(error);
        }
        if let Some(&(_, sample)) = desk.connected.iter().find(|(id, _)| *id == self.id) {
            out.resize(out.len() + desk.samples_per_read, sample);
            Ok(())
        } else {
            // Some of a read can arrive before Windows notices the output is gone.
            out.push(-1);
            Err(AudioError::DeviceLost)
        }
    }

    fn meter(&self) -> f32 {
        self.desk
            .lock()
            .unwrap_or_else(PoisonError::into_inner)
            .meter
    }
}

impl Outputs for FakeOutputs {
    type Capture = FakeCapture;

    fn open(&self, id: Option<&str>) -> Result<(FakeCapture, String), AudioError> {
        let mut desk = self.desk();
        desk.opens += 1;
        assert!(!desk.panic_on_open, "the driver fell over");
        if desk.refuse_opens {
            return Err(AudioError::Platform("refused".into()));
        }
        let opened = match id {
            Some(id) if desk.connected.iter().any(|(connected, _)| connected == id) => {
                id.to_owned()
            }
            Some(_) => return Err(AudioError::DeviceMissing),
            None => desk.default.clone().ok_or(AudioError::NoOutput)?,
        };
        Ok((
            FakeCapture {
                desk: Arc::clone(&self.0),
                id: opened.clone(),
            },
            opened,
        ))
    }

    fn default_id(&self) -> Option<String> {
        self.desk().default.clone()
    }
}
