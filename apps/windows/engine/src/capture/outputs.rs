//! The displays this host can share, numbered the one way capture numbers them.
//!
//! A display is chosen in the app by its index, and capture opens it by the same index. Two
//! enumerations that each counted adapters and outputs could disagree - one skipping an adapter
//! the other kept - and the person would pick one screen and share another. So there is one walk,
//! [`walk`], and both the list and capture go through it.

use std::ops::ControlFlow;

/// How a display is turned, as Windows reports it.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u32)]
pub enum Rotation {
    /// Upright, or not reported.
    Identity = 1,
    /// Turned a quarter clockwise.
    Rotate90 = 2,
    /// Upside down.
    Rotate180 = 3,
    /// Turned a quarter anticlockwise.
    Rotate270 = 4,
}

impl Rotation {
    /// Reads DXGI's rotation value; unspecified and unknown values read as upright.
    #[must_use]
    pub fn from_dxgi(value: i32) -> Self {
        match value {
            2 => Self::Rotate90,
            3 => Self::Rotate180,
            4 => Self::Rotate270,
            _ => Self::Identity,
        }
    }
}

/// One display, as the app is told about it.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DisplayOutput {
    /// The index capture opens this display by.
    pub index: u32,
    /// The adapter that drives it.
    pub adapter_luid: i64,
    /// Its place on the Windows desktop: left, top, right, bottom.
    pub desktop: (i32, i32, i32, i32),
    /// How it is turned.
    pub rotation: Rotation,
    /// Whether it is part of the desktop. A detached output cannot be captured.
    pub attached: bool,
    /// Windows' device name for it, such as `\\.\DISPLAY1`, which names the monitor elsewhere.
    pub device_name: String,
}

impl DisplayOutput {
    /// Whether this is the display Windows calls the main one: the one at the desktop's origin.
    #[must_use]
    pub fn is_main(&self) -> bool {
        self.attached && self.desktop.0 == 0 && self.desktop.1 == 0
    }
}

/// Visits every output of every adapter in enumeration order, giving each its capture index.
///
/// `adapter_at` and `output_at` return `Ok(None)` past the last item. `visit` may stop the walk
/// early with a result. Returns that result, or the number of outputs seen when the walk ran to
/// the end.
///
/// # Errors
/// The first error any of the three callbacks returns.
pub fn walk<A, O, E, R>(
    mut adapter_at: impl FnMut(u32) -> Result<Option<A>, E>,
    mut output_at: impl FnMut(&A, u32) -> Result<Option<O>, E>,
    mut visit: impl FnMut(u32, &A, O) -> Result<ControlFlow<R>, E>,
) -> Result<ControlFlow<R, u32>, E> {
    let mut seen = 0u32;
    for adapter_index in 0.. {
        let Some(adapter) = adapter_at(adapter_index)? else {
            break;
        };

        for output_index in 0.. {
            let Some(output) = output_at(&adapter, output_index)? else {
                break;
            };

            if let ControlFlow::Break(result) = visit(seen, &adapter, output)? {
                return Ok(ControlFlow::Break(result));
            }
            seen += 1;
        }
    }

    Ok(ControlFlow::Continue(seen))
}

#[cfg(windows)]
mod dxgi {
    use super::{walk, DisplayOutput, Rotation};
    use crate::capture::CaptureError;
    use std::ops::ControlFlow;
    use windows::Win32::Graphics::Dxgi::{
        CreateDXGIFactory1, IDXGIAdapter1, IDXGIFactory1, IDXGIOutput, DXGI_ERROR_NOT_FOUND,
    };

    /// Walks this host's adapters and outputs through DXGI.
    ///
    /// # Errors
    /// [`CaptureError::Platform`] when DXGI refuses, or whatever `visit` returns.
    pub(crate) fn walk_dxgi<R>(
        mut visit: impl FnMut(
            u32,
            &IDXGIAdapter1,
            i64,
            IDXGIOutput,
        ) -> Result<ControlFlow<R>, CaptureError>,
    ) -> Result<ControlFlow<R, u32>, CaptureError> {
        // SAFETY: CreateDXGIFactory1 is callable on any thread and returns a checked HRESULT.
        let factory: IDXGIFactory1 = unsafe { CreateDXGIFactory1() }.map_err(platform)?;

        walk(
            // SAFETY: the factory is live; past the last adapter DXGI answers NOT_FOUND.
            |index| match unsafe { factory.EnumAdapters1(index) } {
                Ok(adapter) => {
                    // SAFETY: the adapter is live; GetDesc1 returns its description.
                    let description = unsafe { adapter.GetDesc1() }.map_err(platform)?;
                    let luid = (i64::from(description.AdapterLuid.HighPart) << 32)
                        | i64::from(description.AdapterLuid.LowPart);
                    Ok(Some((adapter, luid)))
                }
                Err(error) if error.code() == DXGI_ERROR_NOT_FOUND => Ok(None),
                Err(error) => Err(platform(error)),
            },
            // SAFETY: the adapter is live; past the last output DXGI answers NOT_FOUND.
            |(adapter, _), index| match unsafe { adapter.EnumOutputs(index) } {
                Ok(output) => Ok(Some(output)),
                Err(error) if error.code() == DXGI_ERROR_NOT_FOUND => Ok(None),
                Err(error) => Err(platform(error)),
            },
            |index, (adapter, luid), output| visit(index, adapter, *luid, output),
        )
    }

    /// Lists every display capture can open, in capture's own order.
    ///
    /// # Errors
    /// [`CaptureError::Platform`] when DXGI refuses.
    pub fn list() -> Result<Vec<DisplayOutput>, CaptureError> {
        let mut displays = Vec::new();
        // Every display is visited; the count the walk returns is the list's own length.
        let _ = walk_dxgi(|index, _, adapter_luid, output| {
            // SAFETY: the output is live; GetDesc returns its description by value.
            let description = unsafe { output.GetDesc() }.map_err(platform)?;
            let name_len = description
                .DeviceName
                .iter()
                .position(|&unit| unit == 0)
                .unwrap_or(description.DeviceName.len());
            let rect = description.DesktopCoordinates;
            displays.push(DisplayOutput {
                index,
                adapter_luid,
                desktop: (rect.left, rect.top, rect.right, rect.bottom),
                rotation: Rotation::from_dxgi(description.Rotation.0),
                attached: description.AttachedToDesktop.as_bool(),
                device_name: String::from_utf16_lossy(&description.DeviceName[..name_len]),
            });
            Ok(ControlFlow::<()>::Continue(()))
        })?;
        Ok(displays)
    }

    fn platform(error: windows::core::Error) -> CaptureError {
        CaptureError::Platform(error.message())
    }
}

#[cfg(windows)]
pub use dxgi::list;
#[cfg(windows)]
pub(crate) use dxgi::walk_dxgi;

#[cfg(test)]
mod tests {
    use super::*;

    /// A host with three adapters: two displays, none, and one.
    fn host() -> Vec<Vec<&'static str>> {
        vec![vec!["a0", "a1"], vec![], vec!["c0"]]
    }

    fn walk_host<R>(
        visit: impl FnMut(u32, &usize, &'static str) -> Result<ControlFlow<R>, ()>,
    ) -> Result<ControlFlow<R, u32>, ()> {
        let host = host();
        walk(
            |index| Ok(host.get(index as usize).map(|_| index as usize)),
            |adapter, index| Ok(host[*adapter].get(index as usize).copied()),
            visit,
        )
    }

    #[test]
    fn indices_run_from_zero_without_gaps_across_adapters() {
        let mut seen = Vec::new();
        let walked = walk_host(|index, adapter, name| {
            seen.push((index, *adapter, name));
            Ok(ControlFlow::<()>::Continue(()))
        });

        assert_eq!(walked, Ok(ControlFlow::Continue(3)));
        assert_eq!(seen, [(0, 0, "a0"), (1, 0, "a1"), (2, 2, "c0")]);
    }

    #[test]
    fn stopping_at_an_index_finds_the_same_display_the_list_numbered_so() {
        // This is the promise capture relies on: the display listed at an index is the one a walk
        // stopping at that index opens.
        let mut listed = Vec::new();
        let _ = walk_host(|index, _, name| {
            listed.push((index, name));
            Ok(ControlFlow::<()>::Continue(()))
        })
        .unwrap();

        for (index, name) in listed {
            let found = walk_host(|seen, _, output| {
                Ok(if seen == index {
                    ControlFlow::Break(output)
                } else {
                    ControlFlow::Continue(())
                })
            });
            assert_eq!(found, Ok(ControlFlow::Break(name)));
        }
    }

    #[test]
    fn an_error_from_any_step_ends_the_walk() {
        let failing_adapter: Result<ControlFlow<(), u32>, &str> = walk(
            |_| Err("adapter"),
            |_: &(), _| Ok(Some(())),
            |_, _, ()| Ok(ControlFlow::Continue(())),
        );
        assert_eq!(failing_adapter, Err("adapter"));

        let failing_output: Result<ControlFlow<(), u32>, &str> = walk(
            |index| Ok((index == 0).then_some(())),
            |_, _| Err("output"),
            |_, _, ()| Ok(ControlFlow::Continue(())),
        );
        assert_eq!(failing_output, Err("output"));

        let failing_visit = walk_host(|_, _, _| Err::<ControlFlow<()>, ()>(()));
        assert_eq!(failing_visit, Err(()));
    }

    #[test]
    fn rotation_reads_dxgi_values_and_treats_the_unknown_as_upright() {
        assert_eq!(Rotation::from_dxgi(0), Rotation::Identity);
        assert_eq!(Rotation::from_dxgi(1), Rotation::Identity);
        assert_eq!(Rotation::from_dxgi(2), Rotation::Rotate90);
        assert_eq!(Rotation::from_dxgi(3), Rotation::Rotate180);
        assert_eq!(Rotation::from_dxgi(4), Rotation::Rotate270);
        assert_eq!(Rotation::from_dxgi(9), Rotation::Identity);
    }

    #[test]
    fn the_main_display_is_the_attached_one_at_the_origin() {
        let display = DisplayOutput {
            index: 0,
            adapter_luid: 1,
            desktop: (0, 0, 1920, 1080),
            rotation: Rotation::Identity,
            attached: true,
            device_name: r"\\.\DISPLAY1".into(),
        };
        assert!(display.is_main());
        assert!(!DisplayOutput {
            desktop: (1920, 0, 3840, 1080),
            ..display.clone()
        }
        .is_main());
        assert!(!DisplayOutput {
            desktop: (0, 1080, 1920, 2160),
            ..display.clone()
        }
        .is_main());
        assert!(!DisplayOutput {
            attached: false,
            ..display
        }
        .is_main());
    }

    #[cfg(windows)]
    #[test]
    fn listing_this_hosts_displays_never_panics_and_numbers_them_in_order() {
        // A build agent with no display lists none, which is an answer rather than a failure.
        if let Ok(displays) = list() {
            for (position, display) in displays.iter().enumerate() {
                assert_eq!(display.index as usize, position);
            }
        }
    }
}
