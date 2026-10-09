#![allow(
    clippy::undocumented_unsafe_blocks,
    reason = "test set-up: every block is a D3D11 call with the arguments it names"
)]

use windows::Win32::Foundation::HMODULE;
use windows::Win32::Graphics::Direct3D::{D3D_DRIVER_TYPE_WARP, D3D_FEATURE_LEVEL_11_0};
use windows::Win32::Graphics::Direct3D11::{
    D3D11CreateDevice, D3D11_BIND_SHADER_RESOURCE, D3D11_CREATE_DEVICE_BGRA_SUPPORT,
    D3D11_MAP_READ, D3D11_SDK_VERSION, D3D11_SUBRESOURCE_DATA, D3D11_USAGE_DEFAULT,
};

use super::super::pointer::{PointerKind, PointerShape};
use super::*;

const SIZE: u32 = 64;
const GREY: [u8; 4] = [0x40, 0x80, 0xC0, 0xFF];

/// A software device, so the test runs on any Windows, with or without a graphics card.
fn device() -> (ID3D11Device, ID3D11DeviceContext) {
    let mut device = None;
    let mut context = None;
    unsafe {
        D3D11CreateDevice(
            None,
            D3D_DRIVER_TYPE_WARP,
            HMODULE::default(),
            D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            Some(&[D3D_FEATURE_LEVEL_11_0]),
            D3D11_SDK_VERSION,
            Some(&raw mut device),
            None,
            Some(&raw mut context),
        )
        .unwrap();
    }
    (device.unwrap(), context.unwrap())
}

/// A grey frame on the GPU.
fn frame(device: &ID3D11Device) -> ID3D11Texture2D {
    let pixels: Vec<u8> = GREY
        .iter()
        .copied()
        .cycle()
        .take((SIZE * SIZE * 4) as usize)
        .collect();
    let description = D3D11_TEXTURE2D_DESC {
        Width: SIZE,
        Height: SIZE,
        MipLevels: 1,
        ArraySize: 1,
        Format: DXGI_FORMAT_B8G8R8A8_UNORM,
        SampleDesc: DXGI_SAMPLE_DESC {
            Count: 1,
            Quality: 0,
        },
        Usage: D3D11_USAGE_DEFAULT,
        BindFlags: D3D11_BIND_SHADER_RESOURCE.0 as u32,
        CPUAccessFlags: 0,
        MiscFlags: 0,
    };
    let data = D3D11_SUBRESOURCE_DATA {
        pSysMem: pixels.as_ptr().cast(),
        SysMemPitch: SIZE * 4,
        SysMemSlicePitch: 0,
    };
    let mut texture = None;
    unsafe {
        device
            .CreateTexture2D(
                &raw const description,
                Some(&raw const data),
                Some(&raw mut texture),
            )
            .unwrap();
    }
    texture.unwrap()
}

/// The frame's pixels, read back.
fn read(device: &ID3D11Device, context: &ID3D11DeviceContext, frame: &ID3D11Texture2D) -> Vec<u8> {
    let description = D3D11_TEXTURE2D_DESC {
        Width: SIZE,
        Height: SIZE,
        MipLevels: 1,
        ArraySize: 1,
        Format: DXGI_FORMAT_B8G8R8A8_UNORM,
        SampleDesc: DXGI_SAMPLE_DESC {
            Count: 1,
            Quality: 0,
        },
        Usage: D3D11_USAGE_STAGING,
        BindFlags: 0,
        CPUAccessFlags: D3D11_CPU_ACCESS_READ.0 as u32,
        MiscFlags: 0,
    };
    let mut staging = None;
    unsafe {
        device
            .CreateTexture2D(&raw const description, None, Some(&raw mut staging))
            .unwrap();
    }
    let staging = staging.unwrap();
    let mut mapped = D3D11_MAPPED_SUBRESOURCE::default();
    unsafe {
        context.CopyResource(&staging, frame);
        context
            .Map(&staging, 0, D3D11_MAP_READ, 0, Some(&raw mut mapped))
            .unwrap();
    }
    let mut pixels = vec![0u8; (SIZE * SIZE * 4) as usize];
    for row in 0..SIZE as usize {
        let source = unsafe {
            std::slice::from_raw_parts(
                mapped
                    .pData
                    .cast::<u8>()
                    .add(row * mapped.RowPitch as usize),
                SIZE as usize * 4,
            )
        };
        pixels[row * SIZE as usize * 4..][..SIZE as usize * 4].copy_from_slice(source);
    }
    unsafe { context.Unmap(&staging, 0) };
    pixels
}

fn pixel(pixels: &[u8], x: u32, y: u32) -> [u8; 4] {
    pixels[((y * SIZE + x) * 4) as usize..][..4]
        .try_into()
        .unwrap()
}

/// A white square pointer `side` pixels wide, its hotspot at its top left.
fn white(side: u32) -> PointerShape {
    PointerShape {
        kind: PointerKind::Color,
        width: side,
        height: side,
        pitch: side * 4,
        hotspot: (0, 0),
        data: vec![0xFF; (side * side * 4) as usize],
    }
}

fn track(place: Option<(i32, i32)>, shape: Option<PointerShape>) -> PointerTrack {
    let mut track = PointerTrack::default();
    if let Some(shape) = shape {
        track.moved_to(Some((0, 0)));
        track.reshape(shape);
    }
    track.moved_to(place);
    track
}

#[test]
fn a_platform_error_keeps_windows_own_words() {
    let error = windows::core::Error::from(windows::Win32::Foundation::E_FAIL);

    let CaptureError::Platform(message) = platform(error) else {
        panic!("a platform error");
    };
    assert!(!message.is_empty());
}

#[test]
fn the_pointer_is_drawn_onto_the_frame_and_nowhere_else() {
    let (device, context) = device();
    let target = frame(&device);
    let mut overlay = PointerOverlay::default();

    let drew = overlay
        .draw(
            &device,
            &context,
            &target,
            (SIZE, SIZE),
            Some(&track(Some((10, 20)), Some(white(4)))),
        )
        .unwrap();

    assert!(drew);
    let pixels = read(&device, &context, &target);
    assert_eq!(pixel(&pixels, 10, 20)[..3], [0xFF; 3]);
    assert_eq!(pixel(&pixels, 13, 23)[..3], [0xFF; 3]);
    assert_eq!(pixel(&pixels, 14, 20), GREY);
    assert_eq!(pixel(&pixels, 9, 20), GREY);
    assert_eq!(pixel(&pixels, 0, 0), GREY);
}

#[test]
fn a_pointer_over_the_edge_is_clipped_and_the_staging_texture_is_kept() {
    let (device, context) = device();
    let target = frame(&device);
    let mut overlay = PointerOverlay::default();
    let corner = track(Some((62, 62)), Some(white(8)));

    assert!(overlay
        .draw(&device, &context, &target, (SIZE, SIZE), Some(&corner))
        .unwrap());
    assert!(
        overlay
            .draw(&device, &context, &target, (SIZE, SIZE), Some(&corner))
            .unwrap(),
        "drawn again with the same staging texture"
    );

    let pixels = read(&device, &context, &target);
    assert_eq!(pixel(&pixels, 63, 63)[..3], [0xFF; 3]);
    assert_eq!(pixel(&pixels, 61, 61), GREY);
}

#[test]
fn nothing_is_drawn_without_a_place_a_shape_or_a_part_on_the_frame() {
    let (device, context) = device();
    let target = frame(&device);
    let mut overlay = PointerOverlay::default();
    let mut oversized = white(4);
    oversized.width = MAX_POINTER_SIDE + 1;

    for nothing in [
        track(None, Some(white(4))),
        track(Some((5, 5)), None),
        track(Some((SIZE as i32 + 5, 5)), Some(white(4))),
        track(Some((-10, -10)), Some(white(4))),
        track(Some((5, 5)), Some(oversized)),
    ] {
        assert!(!overlay
            .draw(&device, &context, &target, (SIZE, SIZE), Some(&nothing))
            .unwrap());
    }
    assert!(!overlay
        .draw(&device, &context, &target, (SIZE, SIZE), None)
        .unwrap());
    assert!(
        overlay.staging.is_none(),
        "nothing to draw makes no round trip to the GPU"
    );

    let pixels = read(&device, &context, &target);
    assert!(pixels.chunks_exact(4).all(|chunk| chunk == GREY));
}
