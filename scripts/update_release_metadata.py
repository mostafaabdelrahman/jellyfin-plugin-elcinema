import json
import os
import re
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
PLUGIN_GUID = "2b09a9db-923f-4e09-9ec6-8968f9a2de98"
README_START = "<!-- RELEASE_INFO_START -->"
README_END = "<!-- RELEASE_INFO_END -->"


def required(name: str) -> str:
    value = os.environ.get(name, "").strip()
    if not value:
        raise SystemExit(f"Missing required environment variable: {name}")
    return value


tag = required("RELEASE_TAG")
version = required("PLUGIN_VERSION")
target_abi = required("TARGET_ABI")
owner = required("REPOSITORY_OWNER")
source_url = required("SOURCE_URL")
release_url = required("RELEASE_URL")
manifest_url = required("MANIFEST_URL")
checksum = required("CHECKSUM").lower()
timestamp = required("RELEASE_TIMESTAMP")
changelog_path = Path(required("CHANGELOG_PATH"))

if not re.fullmatch(r"[0-9a-f]{32}", checksum):
    raise SystemExit("CHECKSUM must be a 32-character MD5 hex digest")

changelog = changelog_path.read_text(encoding="utf-8").strip()
if not changelog:
    changelog = f"Release {tag}."

manifest_path = REPOSITORY_ROOT / "manifest.json"
manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
plugin_entries = [entry for entry in manifest if entry.get("guid") == PLUGIN_GUID]
if len(plugin_entries) != 1:
    raise SystemExit(f"Expected exactly one manifest entry for plugin GUID {PLUGIN_GUID}")

plugin = plugin_entries[0]
plugin["owner"] = owner
new_version = {
    "version": version,
    "changelog": changelog,
    "targetAbi": target_abi,
    "sourceUrl": source_url,
    "checksum": checksum,
    "timestamp": timestamp,
}
plugin["versions"] = [
    new_version,
    *[entry for entry in plugin.get("versions", []) if entry.get("version") != version],
]
manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

readme_path = REPOSITORY_ROOT / "README.md"
readme = readme_path.read_text(encoding="utf-8")
release_info = (
    f"{README_START}\n"
    f"**Latest release:** [{tag}]({release_url}) (`{version}`; Jellyfin ABI `{target_abi}`).\n\n"
    f"**Plugin repository manifest:** [{manifest_url}]({manifest_url})\n"
    f"{README_END}"
)
pattern = re.compile(re.escape(README_START) + r".*?" + re.escape(README_END), re.DOTALL)
updated_readme, replacements = pattern.subn(lambda _: release_info, readme, count=1)
if replacements != 1:
    raise SystemExit("README release-info markers are missing or duplicated")
readme_path.write_text(updated_readme, encoding="utf-8")
