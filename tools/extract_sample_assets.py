"""Extract the approved asset sample locally from the supplied raster boards."""

import argparse
from collections import deque
import hashlib
import json
from pathlib import Path
import shutil

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont


SOURCES = {
    "brand-board.png": {
        "sha256": "4dd066a5b0889f7581ab3f80ea31181ae71bbaefd9d6ad4eefafc7e6446413e1",
        "description": "First brand board; supplied twice with identical bytes.",
    },
    "character-design-kit.png": {
        "sha256": "c0ead3377e667ac23568e45722a069cbb019ff3bee94dabf10e5375d7584954e",
        "description": "Character Design Kit with UI icons along the bottom.",
    },
    "asset-pack-v1.png": {
        "sha256": "f1f6e5fdff4a6cffe440628bae34e6dd89cbfdc2efc7468c83bc7b44338ddbcb",
        "description": "Approved primary palette and visual reference.",
    },
}

SAMPLES = (
    {
        "id": "ficodi-front",
        "title": "Ficodi - frontale",
        "source": "brand-board.png",
        "crop": [809, 18, 995, 261],
        "canvas": [224, 256],
        "file": "mascot\\ficodi-front.png",
        "css": [112, 128],
        "usage": "Small mascot beside a greeting; not a full-screen hero.",
    },
    {
        "id": "ficodi-study",
        "title": "Ficodi - studia",
        "source": "brand-board.png",
        "crop": [478, 779, 652, 960],
        "canvas": [224, 256],
        "file": "mascot\\ficodi-study.png",
        "css": [112, 128],
        "usage": "Reading pose for lesson introductions.",
        "backgroundSeeds": [[136, 79], [139, 100]],
        "preservePolygons": [[
            [70, 127], [71, 124], [79, 122], [88, 122], [98, 124],
            [110, 128], [119, 132], [121, 127], [126, 122], [133, 118],
            [142, 115], [151, 112], [154, 114], [155, 117], [150, 121],
            [141, 125], [133, 130], [125, 136], [120, 137], [108, 133],
            [96, 131], [82, 130], [70, 130],
        ]],
    },
    {
        "id": "icon-trophy",
        "title": "Icona - coppa",
        "source": "character-design-kit.png",
        "crop": [285, 936, 350, 991],
        "canvas": [80, 80],
        "file": "icons\\trophy.png",
        "css": [40, 40],
        "usage": "Small leaderboard icon; inspect the native detail before approval.",
        "backgroundSeeds": [[15, 18], [50, 18]],
    },
)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def connected_region(allowed, seeds):
    height, width = allowed.shape
    result = np.zeros_like(allowed, dtype=bool)
    queue = deque()
    for x, y in seeds:
        if allowed[y, x] and not result[y, x]:
            result[y, x] = True
            queue.append((x, y))
    while queue:
        x, y = queue.popleft()
        for nx, ny in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
            if 0 <= nx < width and 0 <= ny < height:
                if allowed[ny, nx] and not result[ny, nx]:
                    result[ny, nx] = True
                    queue.append((nx, ny))
    return result


def silhouette(rgb, minimum_component_size=None):
    high = rgb.max(axis=2)
    low = rgb.min(axis=2)
    saturation = (high - low) / np.maximum(high, 1)
    colored = saturation > 0.30
    remaining = colored.copy()
    largest = np.zeros_like(colored)
    selected = np.zeros_like(colored)
    while remaining.any():
        y, x = np.argwhere(remaining)[0]
        component = connected_region(remaining, [(int(x), int(y))])
        if component.sum() > largest.sum():
            largest = component
        if minimum_component_size is not None and component.sum() >= minimum_component_size:
            selected |= component
        remaining[component] = False
    if minimum_component_size is not None:
        largest = selected

    height, width = largest.shape
    border = (
        [(x, 0) for x in range(width)]
        + [(x, height - 1) for x in range(width)]
        + [(0, y) for y in range(height)]
        + [(width - 1, y) for y in range(height)]
    )
    exterior = connected_region(~largest, border)
    return ~exterior, saturation


def extract(crop, sample):
    rgb = np.asarray(crop.convert("RGB"), dtype=np.float64)
    shape, saturation = silhouette(rgb, sample.get("minComponentSize"))
    if sample.get("backgroundSeeds"):
        holes = connected_region(saturation < 0.30, sample["backgroundSeeds"])
        shape[holes] = False
    preserve_image = Image.new("L", crop.size)
    preserve_draw = ImageDraw.Draw(preserve_image)
    for polygon in sample.get("preservePolygons", []):
        preserve_draw.polygon([tuple(point) for point in polygon], fill=255)
    preserve = np.asarray(preserve_image) == 255
    shape |= preserve
    binary = Image.fromarray(shape.astype(np.uint8) * 255)
    inner = np.asarray(binary.filter(ImageFilter.MinFilter(sample.get("edgeKernel", 5)))) == 255
    outer = np.asarray(binary.filter(ImageFilter.MaxFilter(5))) == 255
    if not inner.any():
        raise ValueError("No solid foreground found in crop.")

    # Propagate interior colors only through a narrow edge band for local matting.
    height, width = shape.shape
    nearest = np.zeros_like(rgb)
    visited = inner.copy()
    queue = deque()
    for y, x in np.argwhere(inner):
        nearest[y, x] = rgb[y, x]
        queue.append((int(x), int(y)))
    while queue:
        x, y = queue.popleft()
        for nx, ny in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
            if 0 <= nx < width and 0 <= ny < height:
                if outer[ny, nx] and not visited[ny, nx]:
                    visited[ny, nx] = True
                    nearest[ny, nx] = nearest[y, x]
                    queue.append((nx, ny))

    # Pale backgrounds vary by row. The crop margins contain no subject.
    margins = np.concatenate((rgb[:, :3], rgb[:, -3:]), axis=1)
    background = np.median(margins, axis=1)[:, None, :]
    direction = nearest - background
    denominator = np.maximum((direction * direction).sum(axis=2), 1)
    alpha = np.clip(
        ((rgb - background) * direction).sum(axis=2) / denominator, 0, 1
    )
    alpha[~outer] = 0
    alpha[inner] = 1
    alpha[(~shape) & (saturation < 0.12)] = 0
    alpha[preserve] = 1
    alpha[alpha < 0.04] = 0
    for x, y in sample.get("backgroundSeeds", []):
        if alpha[y, x] != 0:
            raise ValueError(f"Background opening was not cleared at {(x, y)}.")
    recovered = np.clip(
        (rgb - (1 - alpha[:, :, None]) * background)
        / np.maximum(alpha[:, :, None], 0.001),
        0,
        255,
    )
    rgba = np.dstack((recovered, alpha * 255)).round().astype(np.uint8)
    rgba[rgba[:, :, 3] == 0, :3] = 0
    extracted = Image.fromarray(rgba)
    bounds = extracted.getbbox()
    if bounds is None:
        raise ValueError("Extraction produced an empty asset.")
    if bounds[0] == 0 or bounds[1] == 0 or bounds[2] == width or bounds[3] == height:
        raise ValueError(f"Subject touches crop edge: {bounds}; enlarge crop.")
    subject = extracted.crop(bounds)
    canvas = Image.new("RGBA", tuple(sample["canvas"]))
    if subject.width > canvas.width - 16 or subject.height > canvas.height - 16:
        raise ValueError("Canvas too small for native-resolution subject.")
    offset = ((canvas.width - subject.width) // 2, canvas.height - subject.height - 12)
    canvas.alpha_composite(subject, offset)
    return canvas, {
        "subjectBoundsInCrop": list(bounds),
        "subjectPixels": list(subject.size),
        "subjectOffsetInCanvas": list(offset),
        "resampled": False,
    }


def font(size, bold=False):
    name = "segoeuib.ttf" if bold else "segoeui.ttf"
    return ImageFont.truetype(str(Path("C:\\Windows\\Fonts") / name), size)


def review_board(root, rendered):
    board = Image.new("RGB", (1440, 1160), "#F5F1FC")
    draw = ImageDraw.Draw(board)
    draw.rectangle((0, 0, 1440, 134), fill="#36114F")
    draw.text((40, 22), "SICULINGO / CAMPIONE ASSET 01", font=font(31, True), fill="white")
    draw.text(
        (40, 72),
        "PNG trasparenti a risoluzione nativa. Campione da approvare, non asset HD.",
        font=font(21),
        fill="#E2D0F5",
    )
    columns = (40, 388, 736, 1084)
    labels = ("Ritaglio sorgente", "Trasparenza / scacchi", "Sfondo crema", "Sfondo scuro")
    for x, label in zip(columns, labels):
        draw.text((x, 152), label, font=font(20, True), fill="#36114F")

    for index, sample in enumerate(SAMPLES):
        top = (191, 510, 829)[index]
        draw.text((40, top), sample["title"], font=font(20, True), fill="#36114F")
        for column, left in enumerate(columns):
            tile = Image.new("RGBA", (316, 268), "white")
            tile_draw = ImageDraw.Draw(tile)
            if column == 1:
                for y in range(0, tile.height, 16):
                    for x in range(0, tile.width, 16):
                        color = "#DED7E7" if (x // 16 + y // 16) % 2 else "#F9F6FC"
                        tile_draw.rectangle((x, y, x + 15, y + 15), fill=color)
            elif column == 2:
                tile_draw.rectangle((0, 0, 316, 268), fill="#FFF4E6")
            elif column == 3:
                tile_draw.rectangle((0, 0, 316, 268), fill="#36114F")
            if column == 0:
                with Image.open(root / "assets" / "source" / sample["source"]) as source:
                    image = source.crop(sample["crop"]).convert("RGBA")
            else:
                image = rendered[sample["id"]]
            if index == 2:
                image = image.resize((image.width * 2, image.height * 2), Image.Resampling.NEAREST)
            tile.alpha_composite(image, ((316 - image.width) // 2, (268 - image.height) // 2))
            board.paste(tile.convert("RGB"), (left, top + 32))
        if index < 2:
            draw.text(
                (40, top + 302),
                "Scala 1:1 pixel sorgente; nessun ingrandimento degli asset.",
                font=font(15),
                fill="#624D72",
            )
    draw.text(
        (40, 1137),
        "Coppa al 200% per ispezione. Pagine e occhi preservati; aperture di occhiali e manici trasparenti.",
        font=font(15),
        fill="#624D72",
    )
    output = root / "assets" / "review" / "sample-assets-review.png"
    output.parent.mkdir(parents=True, exist_ok=True)
    board.save(output)
    return output


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-dir", type=Path, help="Folder containing supplied PNG boards.")
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    manifest_path = args.root / "assets" / "manifest.json"
    previous_assets = {}
    if manifest_path.exists():
        previous_manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        previous_assets = {asset["id"]: asset for asset in previous_manifest["assets"]}
    source_dir = args.root / "assets" / "source"
    source_dir.mkdir(parents=True, exist_ok=True)
    available = {}
    if args.source_dir:
        for path in sorted(args.source_dir.glob("*.png")):
            available.setdefault(digest(path), []).append(path)
    source_records = []
    for name, spec in SOURCES.items():
        target = source_dir / name
        matches = available.get(spec["sha256"], [])
        if not target.exists():
            if not matches:
                raise FileNotFoundError(f"Missing original {name}; pass --source-dir.")
            shutil.copyfile(matches[0], target)
        if digest(target) != spec["sha256"]:
            raise ValueError(f"Source checksum mismatch: {name}")
        with Image.open(target) as image:
            dimensions = list(image.size)
        source_records.append({
            "file": str(target.relative_to(args.root)),
            "sha256": spec["sha256"],
            "pixels": dimensions,
            "description": spec["description"],
        })

    rendered = {}
    records = []
    for sample in SAMPLES:
        with Image.open(source_dir / sample["source"]) as source:
            crop = source.crop(sample["crop"])
        image, details = extract(crop, sample)
        target = args.root / "frontend" / "siculingo-web" / "public" / "assets" / sample["file"]
        target.parent.mkdir(parents=True, exist_ok=True)
        image.save(target, optimize=True)
        with Image.open(target) as saved:
            pixels = np.asarray(saved)
            assert saved.mode == "RGBA"
            assert saved.size == tuple(sample["canvas"])
            assert pixels[:, :, 3].min() == 0 and pixels[:, :, 3].max() == 255
            assert ((pixels[:, :, 3] > 0) & (pixels[:, :, 3] < 255)).any()
            assert not pixels[0, :, 3].any() and not pixels[-1, :, 3].any()
            assert not pixels[:, 0, 3].any() and not pixels[:, -1, 3].any()
            white = (pixels[:, :, :3].min(axis=2) > 225) & (pixels[:, :, 3] == 255)
            if sample["id"].startswith("ficodi"):
                assert white.sum() > 50, "Internal eye/book whites were lost."
        rendered[sample["id"]] = image
        record = {
            "id": sample["id"],
            "status": "sample-awaiting-visual-approval",
            "source": sample["source"],
            "cropLTRBExclusive": sample["crop"],
            "file": str(target.relative_to(args.root)),
            "canvasPixels": sample["canvas"],
            "suggestedCssPixelsAt2x": sample["css"],
            "usage": sample["usage"],
            "backgroundSeedsInCrop": sample.get("backgroundSeeds", []),
            "preservePolygonsInCrop": sample.get("preservePolygons", []),
            "sha256": digest(target),
            **details,
        }
        previous = previous_assets.get(sample["id"], {})
        if (
            previous.get("status") == "sample-approved"
            and previous.get("sha256") == record["sha256"]
        ):
            record["status"] = "sample-approved"
            record["approvedOn"] = previous["approvedOn"]
        records.append(record)
        print(f"{sample['id']}: subject {details['subjectPixels']}, RGBA {image.size}, {target.stat().st_size} bytes")

    manifest = {
        "schemaVersion": 1,
        "status": (
            "sample-approved"
            if all(record["status"] == "sample-approved" for record in records)
            else "sample-awaiting-visual-approval"
        ),
        "rights": "User-supplied artwork; ownership/license not independently verified.",
        "primaryVisualReference": "asset-pack-v1.png",
        "coordinateConvention": "Source pixels, left/top inclusive, right/bottom exclusive.",
        "processing": "Local Pillow/NumPy; connected silhouette, explicit background-hole seeds and book-page contour; edge matting against row-wise background; no upscaling.",
        "requirements": ["Python 3", "Pillow", "NumPy"],
        "sources": source_records,
        "assets": records,
    }
    manifest_path.write_text(
        json.dumps(manifest, indent=2, ensure_ascii=True) + "\n", encoding="utf-8"
    )
    print("Review:", review_board(args.root, rendered))
    print("All sample PNG checks passed.")


if __name__ == "__main__":
    main()
