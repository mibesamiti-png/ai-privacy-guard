"""Ephemeral evaluation fixtures from public fashion, adult cosplay, and adult fictional art.
Do not publish game/anime images or identifiable cosplayers as GitHub artifacts.
No covert/upskirt imagery, no child or child-coded character imagery.
"""
from __future__ import annotations
from pathlib import Path
from io import BytesIO
from urllib.request import Request, urlopen
from PIL import Image, ImageOps
import argparse, hashlib, json

# All categories are adult characters/contexts. These are *NOT* labeled positive
# examples of a panty-shot: that would require consented ground-truth material.
SOURCES = [
    dict(
        id="10_real_fashion_bikini_runway",
        group="real_professional_fashion",
        title="Adult runway swimwear model (public fashion event)",
        url="https://commons.wikimedia.org/wiki/Special:FilePath/Model_-_white_bikini.jpg?width=720",
        source_page="https://commons.wikimedia.org/wiki/File:Model_-_white_bikini.jpg",
        license="CC BY 2.0 (Max Bosio); subject personality rights remain",
        allowed_to_redistribute=False,
        expected="Swimwear visible, not voyeuristic underwear or genital exposure"),
    dict(
        id="11_real_raiden_cosplay",
        group="real_adult_character_cosplay",
        title="Raiden Shogun cosplay, conventional clothed pose",
        url="https://commons.wikimedia.org/wiki/Special:FilePath/Tasuno_as_Raiden_Shogun_standing,_front_view_20221016a.jpg?width=720",
        source_page="https://commons.wikimedia.org/wiki/File:Tasuno_as_Raiden_Shogun_standing,_front_view_20221016a.jpg",
        license="CC BY-SA 4.0 (Solomon203); subject personality rights remain",
        allowed_to_redistribute=False,
        expected="Clothed cosplay, negative/control for actual exposure"),
    dict(
        id="12_real_yor_cosplay",
        group="real_adult_character_cosplay",
        title="Yor Forger cosplay, conventional clothed pose",
        url="https://commons.wikimedia.org/wiki/Special:FilePath/Monica_as_Yor_Forger_standing_at_Expo_Dome_court_yard_20230527_161309.jpg?width=720",
        source_page="https://commons.wikimedia.org/wiki/File:Monica_as_Yor_Forger_standing_at_Expo_Dome_court_yard_20230527_161309.jpg",
        license="CC BY-SA 4.0 (Solomon203); subject personality rights remain",
        allowed_to_redistribute=False,
        expected="Clothed cosplay, negative/control"),
    dict(
        id="13_2d_yor_official",
        group="anime_2d_adult_character",
        title="Yor Forger official 2D character art",
        url="https://spy-family.net/assets/img/top/chara_yor_2.png",
        source_page="https://spy-family.net/",
        license="Copyright anime rights holder. Evaluation only; not redistributable",
        allowed_to_redistribute=False,
        expected="Fully dressed adult 2D character, negative/control"),
    dict(
        id="14_2d_ningguang_art",
        group="genshin_2d_adult_character",
        title="Ningguang 2D character portrait",
        url="https://paimon.moe/images/characters/full/ningguang.png",
        source_page="https://paimon.moe/characters/ningguang",
        license="Genshin Impact artwork copyright HoYoverse; evaluation only",
        allowed_to_redistribute=False,
        expected="Fully dressed Genshin adult character"),
    dict(
        id="15_3d_raiden_gameplay",
        group="genshin_3d_adult_character",
        title="Raiden Shogun in-game 3D gameplay screenshot",
        url="https://upload-os-bbs.hoyolab.com/upload/2023/01/07/104611936/d5553514eb4406099e9de1500f70349d_2097096432621085208.jpg",
        source_page="https://www.hoyolab.com/article/14870332",
        license="Genshin Impact game art/screenshots, copyright HoYoverse and uploader; evaluation only",
        allowed_to_redistribute=False,
        expected="Fully clothed 3D character, negative/control"),
    dict(
        id="16_3d_ningguang_gameplay",
        group="genshin_3d_adult_character",
        title="Ningguang in-game 3D gameplay screenshot",
        url="https://upload-os-bbs.hoyolab.com/upload/2023/08/25/239090145/f4dfab69b9eee344d51c6dd86b488db5_6076790458279653545.jpg",
        source_page="https://www.hoyolab.com/article/21160918",
        license="Genshin Impact game art/screenshots, copyright HoYoverse and uploader; evaluation only",
        allowed_to_redistribute=False,
        expected="Fully clothed 3D character, negative/control"),
]
def main(out: Path):
    out.mkdir(parents=True, exist_ok=True)
    inputs = out / "inputs"
    inputs.mkdir(exist_ok=True)
    entries, missing = [], []
    for src in SOURCES:
        try:
            request = Request(src["url"], headers={
                "User-Agent": "Mozilla/5.0 (compatible; AIPrivacyGuardResearch/1.0)",
                "Accept": "image/jpeg,image/png,image/webp,image/*;q=0.9"})
            with urlopen(request, timeout=30) as response:
                kind = response.headers.get("Content-Type","").lower()
                if kind and not kind.startswith("image/") and kind != "application/octet-stream":
                    raise ValueError("Expected image response, got "+kind)
                raw = response.read(9*1024*1024+1)
            if len(raw) > 9*1024*1024:
                raise ValueError("Remote image exceeds 9 MB limit")
            im = ImageOps.exif_transpose(Image.open(BytesIO(raw))).convert("RGB")
            im.thumbnail((720, 720), Image.Resampling.LANCZOS)
            if im.width<50 or im.height<50:
                raise ValueError("Invalid image size")
            png_path=inputs/(src["id"]+".png")
            bgra_path=inputs/(src["id"]+".bgra")
            im.save(png_path)
            bgra_path.write_bytes(im.convert("RGBA").tobytes("raw","BGRA"))
            entry = {
                "id": src["id"], "description":src["title"],
                "group":src["group"],"width":im.width,"height":im.height,
                "png":f"inputs/{png_path.name}", "bgra":f"inputs/{bgra_path.name}",
                "png_sha256":hashlib.sha256(png_path.read_bytes()).hexdigest(),
                "source":src["license"], "source_url":src["source_page"],
                "download_url":src["url"],
                "allowed_to_redistribute":src["allowed_to_redistribute"],
                "expected_context":src["expected"],
                "true_upskirt_positive":False
            }
            entries.append(entry)
            print("FETCH_OK", src["id"], str(im.size))
        except Exception as e:
            missing.append({"id":src["id"], "error_type":type(e).__name__,
                           "source_url":src["source_page"]})
            print("FETCH_FAILED",src["id"],type(e).__name__,str(e)[:100])
    data = {
        "schema_version":1,
        "purpose":"Evaluate actual ONNX on real adult runway/cosplay, 2D adult anime, 3D Genshin images; NOT an upskirt detection ground truth",
        "privacy_policy":"No covert images; no minors; originals kept temporarily in CI, then deleted, not redistributed",
        "evaluated_count":len(entries),
        "unavailable_count":len(missing),
        "missing":missing,
        "cases":entries}
    (out/"fixtures.json").write_text(json.dumps(data,indent=2,ensure_ascii=False),encoding="utf-8")
    print("PUBLIC_MEDIA_FIXTURES",len(entries),"/",len(SOURCES),"downloaded")
    # 4 valid cases cover actual clothing photograph, 2D, and 3D; we fail
    # instead of treating blocked remote resources as a successful evaluation.
    groups={e["group"] for e in entries}
    if len(entries)<4 or not any(g.startswith("genshin_3d") for g in groups) or "anime_2d_adult_character" not in groups or "real_professional_fashion" not in groups:
        raise SystemExit("Insufficient real images for the requested cross-domain evaluation")
if __name__=="__main__":
    p=argparse.ArgumentParser()
    p.add_argument("--output",default="dist/nudenet-public-media-eval")
    a=p.parse_args()
    main(Path(a.output))
