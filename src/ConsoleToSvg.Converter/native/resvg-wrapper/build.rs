use std::env;
use std::fs;

fn main() {
    let manifest_dir = env::var("CARGO_MANIFEST_DIR").unwrap_or_else(|_| ".".to_string());
    let lock_path = format!("{manifest_dir}/Cargo.lock");
    println!("cargo:rerun-if-changed={lock_path}");

    let version = fs::read_to_string(&lock_path)
        .ok()
        .and_then(|lock| find_package_version(&lock, "resvg"))
        .unwrap_or_else(|| "unknown".to_string());
    println!("cargo:rustc-env=RESVG_VERSION={version}");
}

fn find_package_version(lock: &str, name: &str) -> Option<String> {
    let mut in_package = false;
    let mut is_target = false;
    let mut version: Option<String> = None;
    for line in lock.lines() {
        let line = line.trim();
        if line == "[[package]]" {
            if is_target {
                if let Some(v) = version.take() {
                    return Some(v);
                }
            }
            in_package = true;
            is_target = false;
            version = None;
            continue;
        }
        if !in_package {
            continue;
        }
        if let Some(v) = line.strip_prefix("name = ") {
            is_target = v.trim_matches(|c| c == (34 as char)) == name;
        } else if let Some(v) = line.strip_prefix("version = ") {
            if version.is_none() {
                version = Some(v.trim_matches(|c| c == (34 as char)).to_string());
            }
        }
        if is_target && version.is_some() && line.starts_with("source = ") {
            return version;
        }
    }
    if is_target {
        return version;
    }
    None
}
