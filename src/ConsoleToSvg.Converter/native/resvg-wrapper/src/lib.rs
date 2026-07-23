use resvg::{
    tiny_skia,
    usvg::{fontdb::Database, Options, Tree},
};
use std::{
    os::raw::c_int,
    slice,
    str,
    sync::{Arc, OnceLock},
};

const SUCCESS: c_int = 0;
const PARSE_ERROR: c_int = 1;
const PNG_ERROR: c_int = 2;
const RENDER_ERROR: c_int = 3;
const ALLOCATION_ERROR: c_int = 4;

static SYSTEM_FONTDB: OnceLock<Arc<Database>> = OnceLock::new();

fn system_fontdb() -> Arc<Database> {
    SYSTEM_FONTDB
        .get_or_init(|| {
            let mut database = Database::new();
            database.load_system_fonts();
            Arc::new(database)
        })
        .clone()
}

#[no_mangle]
pub extern "C" fn c2s_resvg_warm_system_fonts() -> c_int {
    let _ = system_fontdb();
    SUCCESS
}

#[no_mangle]
pub extern "C" fn c2s_resvg_render_png(
    svg_data: *const u8,
    svg_length: usize,
    width: c_int,
    height: c_int,
    out_buffer: *mut *mut u8,
    out_length: *mut usize,
) -> c_int {
    if svg_data.is_null() || out_buffer.is_null() || out_length.is_null() {
        return RENDER_ERROR;
    }

    let svg = unsafe { slice::from_raw_parts(svg_data, svg_length) };
    let svg = match str::from_utf8(svg) {
        Ok(svg) => svg,
        Err(_) => return PARSE_ERROR,
    };

    let mut options = Options::default();
    options.fontdb = system_fontdb();
    let tree = match Tree::from_str(svg, &options) {
        Ok(tree) => tree,
        Err(_) => return PARSE_ERROR,
    };

    let size = tree.size();
    let (target_width, target_height) = match (width > 0, height > 0) {
        (true, true) => (width as u32, height as u32),
        (true, false) => (
            width as u32,
            (size.height() * width as f32 / size.width()) as u32,
        ),
        (false, true) => (
            (size.width() * height as f32 / size.height()) as u32,
            height as u32,
        ),
        (false, false) => (size.width() as u32, size.height() as u32),
    };
    let target_width = target_width.clamp(1, 16_384);
    let target_height = target_height.clamp(1, 16_384);

    let mut pixmap = match tiny_skia::Pixmap::new(target_width, target_height) {
        Some(pixmap) => pixmap,
        None => return ALLOCATION_ERROR,
    };
    let transform = tiny_skia::Transform::from_scale(
        target_width as f32 / size.width(),
        target_height as f32 / size.height(),
    );
    resvg::render(&tree, transform, &mut pixmap.as_mut());

    let png = match pixmap.encode_png() {
        Ok(png) => png,
        Err(_) => return PNG_ERROR,
    };
    let length = png.len();
    let buffer = Box::into_raw(png.into_boxed_slice()) as *mut u8;

    unsafe {
        *out_buffer = buffer;
        *out_length = length;
    }
    SUCCESS
}

#[no_mangle]
pub extern "C" fn c2s_resvg_free_buffer(buffer: *mut u8, length: usize) {
    if !buffer.is_null() {
        unsafe {
            drop(Box::from_raw(slice::from_raw_parts_mut(buffer, length)));
        }
    }
}
