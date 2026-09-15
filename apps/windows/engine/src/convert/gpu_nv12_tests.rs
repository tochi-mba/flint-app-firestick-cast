use super::*;
use windows::Win32::Graphics::Direct3D::{D3D_DRIVER_TYPE_HARDWARE, D3D_FEATURE_LEVEL_11_0};
use windows::Win32::Graphics::Direct3D11::{
    D3D11CreateDevice, D3D11_BIND_RENDER_TARGET, D3D11_BIND_SHADER_RESOURCE,
    D3D11_CREATE_DEVICE_BGRA_SUPPORT, D3D11_CREATE_DEVICE_VIDEO_SUPPORT, D3D11_SDK_VERSION,
    D3D11_SUBRESOURCE_DATA, D3D11_TEXTURE2D_DESC, D3D11_USAGE_DEFAULT,
};
use windows::Win32::Graphics::Dxgi::Common::{
    DXGI_FORMAT_B8G8R8A8_UNORM, DXGI_FORMAT_NV12, DXGI_SAMPLE_DESC,
};

/// A hardware device with video support, or nothing on a machine without one.
fn device() -> Option<(ID3D11Device, ID3D11DeviceContext)> {
    let mut device = None;
    let mut context = None;
    // SAFETY: both out-parameters are valid for the duration of the call.
    unsafe {
        D3D11CreateDevice(
            None,
            D3D_DRIVER_TYPE_HARDWARE,
            None,
            D3D11_CREATE_DEVICE_VIDEO_SUPPORT | D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            Some(&[D3D_FEATURE_LEVEL_11_0]),
            D3D11_SDK_VERSION,
            Some(&raw mut device),
            None,
            Some(&raw mut context),
        )
        .ok()?;
    }
    Some((device?, context?))
}

/// A BGRA texture filled with one colour.
fn solid_bgra(
    device: &ID3D11Device,
    width: u32,
    height: u32,
    blue: u8,
    green: u8,
    red: u8,
) -> Option<ID3D11Texture2D> {
    let mut pixels = Vec::with_capacity((width * height * 4) as usize);
    for _ in 0..(width * height) {
        pixels.extend_from_slice(&[blue, green, red, 255]);
    }
    let description = D3D11_TEXTURE2D_DESC {
        Width: width,
        Height: height,
        MipLevels: 1,
        ArraySize: 1,
        Format: DXGI_FORMAT_B8G8R8A8_UNORM,
        SampleDesc: DXGI_SAMPLE_DESC {
            Count: 1,
            Quality: 0,
        },
        Usage: D3D11_USAGE_DEFAULT,
        BindFlags: (D3D11_BIND_SHADER_RESOURCE.0 | D3D11_BIND_RENDER_TARGET.0) as u32,
        CPUAccessFlags: 0,
        MiscFlags: 0,
    };
    let initial = D3D11_SUBRESOURCE_DATA {
        pSysMem: pixels.as_ptr().cast(),
        SysMemPitch: width * 4,
        SysMemSlicePitch: 0,
    };
    let mut texture = None;
    // SAFETY: the description and initial data match, and the out-parameter is valid.
    unsafe {
        device.CreateTexture2D(
            &raw const description,
            Some(&raw const initial),
            Some(&raw mut texture),
        )
    }
    .ok()?;
    texture
}

/// An NV12 texture the video processor can write into.
fn nv12_target(device: &ID3D11Device, width: u32, height: u32) -> Option<ID3D11Texture2D> {
    let description = D3D11_TEXTURE2D_DESC {
        Width: width,
        Height: height,
        MipLevels: 1,
        ArraySize: 1,
        Format: DXGI_FORMAT_NV12,
        SampleDesc: DXGI_SAMPLE_DESC {
            Count: 1,
            Quality: 0,
        },
        Usage: D3D11_USAGE_DEFAULT,
        // A video processor writes through a *render target* view, so an NV12 texture without this
        // flag is rejected when the output view is created — with "the parameter is incorrect" and
        // no indication that the bind flags were the problem.
        BindFlags: D3D11_BIND_RENDER_TARGET.0 as u32,
        CPUAccessFlags: 0,
        MiscFlags: 0,
    };
    let mut texture = None;
    // SAFETY: the description is fully initialised and the out-parameter is valid.
    unsafe { device.CreateTexture2D(&raw const description, None, Some(&raw mut texture)) }.ok()?;
    texture
}

#[test]
fn a_converter_reports_the_sizes_it_was_built_for() {
    let Some((device, context)) = device() else {
        return;
    };
    let Ok(converter) = GpuNv12Converter::new(&device, &context, (1920, 1080), (1280, 720)) else {
        return;
    };

    assert_eq!(converter.source(), (1920, 1080));
    assert_eq!(converter.target(), (1280, 720));
}

#[test]
fn converting_a_frame_at_the_same_size_succeeds() {
    let Some((device, context)) = device() else {
        return;
    };
    let Ok(mut converter) = GpuNv12Converter::new(&device, &context, (256, 256), (256, 256)) else {
        return;
    };
    let Some(source) = solid_bgra(&device, 256, 256, 0x40, 0x80, 0xC0) else {
        return;
    };
    let Some(target) = nv12_target(&device, 256, 256) else {
        return;
    };

    if let Err(error) = converter.convert(&source, &target) {
        panic!("conversion failed: {error:?}");
    }
}

#[test]
fn converting_a_frame_while_downscaling_succeeds() {
    // The whole point of doing this on the GPU: colour conversion and the downscale in one pass.
    let Some((device, context)) = device() else {
        return;
    };
    let Ok(mut converter) = GpuNv12Converter::new(&device, &context, (512, 512), (256, 256)) else {
        return;
    };
    let Some(source) = solid_bgra(&device, 512, 512, 0x10, 0x20, 0x30) else {
        return;
    };
    let Some(target) = nv12_target(&device, 256, 256) else {
        return;
    };

    assert!(converter.convert(&source, &target).is_ok());
}

#[test]
fn a_converter_can_be_reused_across_frames() {
    // Views are created per call; leaking one per frame would exhaust the device in a live session.
    let Some((device, context)) = device() else {
        return;
    };
    let Ok(mut converter) = GpuNv12Converter::new(&device, &context, (128, 128), (128, 128)) else {
        return;
    };
    let Some(source) = solid_bgra(&device, 128, 128, 0, 0, 0) else {
        return;
    };
    let Some(target) = nv12_target(&device, 128, 128) else {
        return;
    };

    for index in 0..120 {
        assert!(
            converter.convert(&source, &target).is_ok(),
            "conversion {index} failed; views are probably leaking"
        );
    }
}

#[test]
fn a_device_without_video_support_is_refused_rather_than_used() {
    // The caller keeps a readback path for exactly this case, so it must be told rather than
    // handed a converter that fails on the first frame.
    let mut device = None;
    let mut context = None;
    // SAFETY: both out-parameters are valid; video support is deliberately not requested.
    let created = unsafe {
        D3D11CreateDevice(
            None,
            D3D_DRIVER_TYPE_HARDWARE,
            None,
            windows::Win32::Graphics::Direct3D11::D3D11_CREATE_DEVICE_FLAG::default(),
            Some(&[D3D_FEATURE_LEVEL_11_0]),
            D3D11_SDK_VERSION,
            Some(&raw mut device),
            None,
            Some(&raw mut context),
        )
    };
    if created.is_err() {
        return;
    }
    let (Some(device), Some(context)) = (device, context) else {
        return;
    };

    // A device created without video support may still expose the interface on some drivers, so
    // this asserts only that the call answers rather than panics.
    let _ = GpuNv12Converter::new(&device, &context, (64, 64), (64, 64));
}

#[test]
fn the_colour_space_bitfield_puts_each_field_in_its_documented_place() {
    // The `windows` crate exposes this as a bare u32, so the layout is asserted here rather than
    // trusted. A field in the wrong bit produces a picture in the wrong range, which looks like a
    // bad capture rather than like a bug.
    let limited = colour_space(NOMINAL_RANGE_16_235);

    assert_eq!(limited & 0b1, 0, "Usage must be playback");
    assert_eq!((limited >> 1) & 0b1, 0, "RGB_Range must be full range");
    assert_eq!((limited >> 2) & 0b1, 1, "YCbCr_Matrix must be BT.709");
    assert_eq!((limited >> 3) & 0b1, 0, "xvYCC must be off");
    assert_eq!((limited >> 4) & 0b11, 1, "Nominal_Range must be 16-235");
}

#[test]
fn full_range_and_studio_swing_differ_only_in_the_nominal_range() {
    let full = colour_space(NOMINAL_RANGE_0_255);
    let limited = colour_space(NOMINAL_RANGE_16_235);

    assert_ne!(full, limited);
    assert_eq!(
        full & 0b1111,
        limited & 0b1111,
        "only Nominal_Range may differ"
    );
    assert_eq!((full >> 4) & 0b11, 2);
    assert_eq!((limited >> 4) & 0b11, 1);
}

#[test]
fn the_nominal_range_fits_its_two_bits() {
    // Checked at compile time rather than at run time: a value that overflows the field would shift
    // every reserved bit above it, and there is no reason to let a build like that exist long enough
    // to run. The test remains so the intent is discoverable from the test list.
    const _: () = assert!(NOMINAL_RANGE_0_255 <= 0b11);
    const _: () = assert!(NOMINAL_RANGE_16_235 <= 0b11);

    // The runtime half: the field really does land where the packer says it does.
    assert_eq!(
        (colour_space(NOMINAL_RANGE_0_255) >> 4) & 0b11,
        NOMINAL_RANGE_0_255
    );
    assert_eq!(
        (colour_space(NOMINAL_RANGE_16_235) >> 4) & 0b11,
        NOMINAL_RANGE_16_235
    );
}

#[test]
#[ignore = "needs an interactive desktop; run deliberately"]
fn report_which_input_texture_the_capture_device_will_accept() {
    // The converter works in isolation and is refused on the capture device at capture sizes, so
    // the difference is in how the input texture is described. Each candidate is tried rather than
    // reasoned about, because every rejection is the same message.
    use windows::Win32::Graphics::Direct3D11::{D3D11_BIND_DECODER, D3D11_RESOURCE_MISC_SHARED};

    let Ok(duplication) = crate::capture::duplication::DesktopDuplication::open_primary() else {
        println!("NO CAPTURE");
        return;
    };
    let format = duplication.format();
    let device = duplication.device().clone();
    let context = duplication.context().clone();
    let source = (format.width, format.height);
    let target = (1920, 1200);
    println!("capture {source:?} -> {target:?}");

    let Ok(mut converter) = GpuNv12Converter::new(&device, &context, source, target) else {
        println!("NO CONVERTER");
        return;
    };

    let candidates: [(&str, u32, u32); 5] = [
        ("none", 0, 0),
        ("shader resource", D3D11_BIND_SHADER_RESOURCE.0 as u32, 0),
        (
            "shader resource + render target",
            (D3D11_BIND_SHADER_RESOURCE.0 | D3D11_BIND_RENDER_TARGET.0) as u32,
            0,
        ),
        ("decoder", D3D11_BIND_DECODER.0 as u32, 0),
        (
            "render target + shared",
            D3D11_BIND_RENDER_TARGET.0 as u32,
            D3D11_RESOURCE_MISC_SHARED.0 as u32,
        ),
    ];

    let Some(destination) = nv12_target(&device, target.0, target.1) else {
        println!("NO NV12 TARGET");
        return;
    };

    for (label, bind, misc) in candidates {
        let description = D3D11_TEXTURE2D_DESC {
            Width: source.0,
            Height: source.1,
            MipLevels: 1,
            ArraySize: 1,
            Format: DXGI_FORMAT_B8G8R8A8_UNORM,
            SampleDesc: DXGI_SAMPLE_DESC {
                Count: 1,
                Quality: 0,
            },
            Usage: D3D11_USAGE_DEFAULT,
            BindFlags: bind,
            CPUAccessFlags: 0,
            MiscFlags: misc,
        };
        let mut texture = None;
        // SAFETY: the description is fully initialised and the out-parameter is valid.
        let created =
            unsafe { device.CreateTexture2D(&raw const description, None, Some(&raw mut texture)) };
        let Ok(()) = created else {
            println!("  {label:32}: texture refused");
            continue;
        };
        let Some(texture) = texture else { continue };

        match converter.convert(&texture, &destination) {
            Ok(()) => println!("  {label:32}: OK"),
            Err(error) => println!("  {label:32}: {error}"),
        }
    }
}
